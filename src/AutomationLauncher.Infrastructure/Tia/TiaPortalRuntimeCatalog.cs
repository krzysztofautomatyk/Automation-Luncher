using System.Reflection;
using System.Text.RegularExpressions;
using AutomationLauncher.Domain.Contracts;
using AutomationLauncher.Domain.Models;

namespace AutomationLauncher.Infrastructure.Tia;

public sealed class TiaPortalRuntimeCatalog : ITiaPortalRuntimeCatalog
{
    private const string InstalledSource = "Installed scan";
    private const string ConfiguredSource = "Configured path";
    private const string OverrideSource = "Version override";

    private static readonly Regex PortalVersionRegex = new(@"(?i)portal\s+v(?<version>\d+)", RegexOptions.Compiled);
    private static readonly Regex GenericVersionRegex = new(@"(?i)(?:^|[^\d])v?(?<version>\d{2})(?:[^\d]|$)", RegexOptions.Compiled);

    private readonly ArchiveOptions _options;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, bool> _directoryExists;
    private readonly Func<string, string, IEnumerable<string>> _enumerateDirectories;
    private readonly Func<string, Version?> _readAssemblyVersion;
    private readonly Func<Environment.SpecialFolder, string> _getFolderPath;

    public TiaPortalRuntimeCatalog(ArchiveOptions options)
        : this(
            options,
            File.Exists,
            Directory.Exists,
            Directory.EnumerateDirectories,
            TryGetAssemblyVersion,
            Environment.GetFolderPath)
    {
    }

    internal TiaPortalRuntimeCatalog(
        ArchiveOptions options,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists,
        Func<string, string, IEnumerable<string>> enumerateDirectories,
        Func<string, Version?> readAssemblyVersion,
        Func<Environment.SpecialFolder, string> getFolderPath)
    {
        _options = options;
        _fileExists = fileExists;
        _directoryExists = directoryExists;
        _enumerateDirectories = enumerateDirectories;
        _readAssemblyVersion = readAssemblyVersion;
        _getFolderPath = getFolderPath;
    }

    public IReadOnlyList<TiaPortalRuntimeInfo> GetAvailableRuntimes()
    {
        var runtimes = new Dictionary<string, RuntimeCandidate>(StringComparer.OrdinalIgnoreCase);

        AddConfiguredPath(runtimes);
        AddKnownVersionOverrides(runtimes);
        ScanInstalledPortals(runtimes, _getFolderPath(Environment.SpecialFolder.ProgramFiles));
        ScanInstalledPortals(runtimes, _getFolderPath(Environment.SpecialFolder.ProgramFilesX86));

        return runtimes
            .Values
            .OrderByDescending(candidate => candidate.VersionNumber)
            .ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => new TiaPortalRuntimeInfo(candidate.Version, candidate.DisplayName, candidate.OpennessAssemblyPath, candidate.Source))
            .ToList();
    }

    private void AddConfiguredPath(IDictionary<string, RuntimeCandidate> runtimes)
    {
        var configuredPath = _options.OpennessAssemblyPath;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return;
        }

        if (!_fileExists(configuredPath!))
        {
            return;
        }

        var version = TryResolveVersion(configuredPath!) ?? "Configured";
        UpsertRuntime(runtimes, new RuntimeCandidate(version, BuildDisplayName(version), configuredPath!, ConfiguredSource, GetVersionNumber(version), priority: 2));
    }

    private void AddKnownVersionOverrides(IDictionary<string, RuntimeCandidate> runtimes)
    {
        foreach (var knownVersion in _options.KnownVersions)
        {
            var overridePath = knownVersion.OpennessAssemblyPath;
            if (string.IsNullOrWhiteSpace(knownVersion.Version)
                || string.IsNullOrWhiteSpace(overridePath))
            {
                continue;
            }

            if (!_fileExists(overridePath!))
            {
                continue;
            }

            var normalizedVersion = NormalizeVersion(knownVersion.Version) ?? knownVersion.Version.Trim();
            UpsertRuntime(runtimes, new RuntimeCandidate(normalizedVersion, BuildDisplayName(normalizedVersion), overridePath, OverrideSource, GetVersionNumber(normalizedVersion), priority: 3));
        }
    }

    private void ScanInstalledPortals(IDictionary<string, RuntimeCandidate> runtimes, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return;
        }

        var automationDirectory = Path.Combine(rootPath, "Siemens", "Automation");
        if (!_directoryExists(automationDirectory))
        {
            return;
        }

        IEnumerable<string> portalDirectories;
        try
        {
            portalDirectories = _enumerateDirectories(automationDirectory, "Portal V*");
        }
        catch
        {
            return;
        }

        foreach (var portalDirectory in portalDirectories)
        {
            var version = NormalizeVersion(portalDirectory);
            if (version is null)
            {
                continue;
            }

            var assemblyPath = Path.Combine(portalDirectory, "PublicAPI", version, "Siemens.Engineering.dll");
            if (!_fileExists(assemblyPath))
            {
                continue;
            }

            UpsertRuntime(runtimes, new RuntimeCandidate(version, BuildDisplayName(version), assemblyPath, InstalledSource, GetVersionNumber(version), priority: 1));
        }
    }

    private static void UpsertRuntime(IDictionary<string, RuntimeCandidate> runtimes, RuntimeCandidate candidate)
    {
        if (!runtimes.TryGetValue(candidate.Version, out var existing) || candidate.Priority >= existing.Priority)
        {
            runtimes[candidate.Version] = candidate;
        }
    }

    private string? TryResolveVersion(string opennessAssemblyPath)
    {
        var fromPath = NormalizeVersion(opennessAssemblyPath);
        if (fromPath is not null)
        {
            return fromPath;
        }

        var assemblyVersion = _readAssemblyVersion(opennessAssemblyPath);
        if (assemblyVersion?.Major is int major && major > 0)
        {
            return $"V{major}";
        }

        return null;
    }

    private static Version? TryGetAssemblyVersion(string opennessAssemblyPath)
    {
        try
        {
            return AssemblyName.GetAssemblyName(opennessAssemblyPath).Version;
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeVersion(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var portalMatch = PortalVersionRegex.Match(input);
        if (portalMatch.Success && int.TryParse(portalMatch.Groups["version"].Value, out var portalVersion))
        {
            return $"V{portalVersion}";
        }

        var genericMatch = GenericVersionRegex.Match(input);
        if (genericMatch.Success && int.TryParse(genericMatch.Groups["version"].Value, out var genericVersion))
        {
            return $"V{genericVersion}";
        }

        return null;
    }

    private static int GetVersionNumber(string version)
    {
        return int.TryParse(version.TrimStart('V', 'v'), out var parsed) ? parsed : -1;
    }

    private static string BuildDisplayName(string version)
    {
        return string.Equals(version, "Configured", StringComparison.OrdinalIgnoreCase)
            ? "Configured Openness runtime"
            : $"TIA Portal {version}";
    }

    private sealed class RuntimeCandidate
    {
        public RuntimeCandidate(string version, string displayName, string opennessAssemblyPath, string source, int versionNumber, int priority)
        {
            Version = version;
            DisplayName = displayName;
            OpennessAssemblyPath = opennessAssemblyPath;
            Source = source;
            VersionNumber = versionNumber;
            Priority = priority;
        }

        public string Version { get; }

        public string DisplayName { get; }

        public string OpennessAssemblyPath { get; }

        public string Source { get; }

        public int VersionNumber { get; }

        public int Priority { get; }
    }
}