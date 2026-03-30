using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using AutomationLauncher.Domain.Contracts;
using AutomationLauncher.Domain.Models;

namespace AutomationLauncher.Infrastructure.Tia;

public sealed class TiaPortalRuntimeResolver
{
    private static readonly Regex DirectVersionRegex = new(@"(?i)^v?(?<version>\d+)$", RegexOptions.Compiled);
    private static readonly Regex VersionRegex = new(@"(?i)portal\s+v(?<version>\d+)", RegexOptions.Compiled);

    private readonly ArchiveOptions _options;
    private readonly ITiaPortalRuntimeCatalog _runtimeCatalog;

    public TiaPortalRuntimeResolver(ArchiveOptions options, ITiaPortalRuntimeCatalog runtimeCatalog)
    {
        _options = options;
        _runtimeCatalog = runtimeCatalog;
    }

    internal TiaPortalRuntimeResolution Resolve(Process? process)
    {
        return ResolveDetectedVersion(TryReadProcessVersion(process));
    }

    internal TiaPortalRuntimeResolution ResolveDetectedVersion(string? detectedProcessVersion)
    {
        var runtimes = _runtimeCatalog.GetAvailableRuntimes();
        if (runtimes.Count == 0)
        {
            return TiaPortalRuntimeResolution.Fail("NoOpennessRuntimeFound", "No Siemens Openness runtime was discovered. Configure a version override or install TIA Portal PublicAPI.");
        }

        if (_options.TiaVersionSelectionMode == TiaPortalVersionSelectionMode.Manual)
        {
            var preferredVersion = _options.PreferredTiaVersion;
            if (string.IsNullOrWhiteSpace(preferredVersion))
            {
                return TiaPortalRuntimeResolution.Fail("ManualRuntimeVersionMissing", "Manual runtime selection is enabled, but PreferredTiaVersion is empty.", detectedProcessVersion);
            }

            var manualRuntime = FindRuntime(runtimes, preferredVersion!);
            if (manualRuntime is null)
            {
                return TiaPortalRuntimeResolution.Fail("ManualRuntimeUnavailable", $"Configured TIA runtime {preferredVersion} is not available on this machine.", detectedProcessVersion);
            }

            return TiaPortalRuntimeResolution.Success(manualRuntime, detectedProcessVersion);
        }

        if (!string.IsNullOrWhiteSpace(detectedProcessVersion))
        {
            var processRuntime = FindRuntime(runtimes, detectedProcessVersion!);
            if (processRuntime is not null)
            {
                return TiaPortalRuntimeResolution.Success(processRuntime, detectedProcessVersion);
            }
        }

        var autoPreferredVersion = _options.PreferredTiaVersion;
        if (!string.IsNullOrWhiteSpace(autoPreferredVersion))
        {
            var preferredRuntime = FindRuntime(runtimes, autoPreferredVersion!);
            if (preferredRuntime is not null)
            {
                return TiaPortalRuntimeResolution.Success(preferredRuntime, detectedProcessVersion);
            }
        }

        return TiaPortalRuntimeResolution.Success(runtimes[0], detectedProcessVersion);
    }

    private static TiaPortalRuntimeInfo? FindRuntime(IReadOnlyList<TiaPortalRuntimeInfo> runtimes, string version)
    {
        var normalized = NormalizeVersion(version);
        if (normalized is null)
        {
            return null;
        }

        return runtimes.FirstOrDefault(runtime => string.Equals(runtime.Version, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryReadProcessVersion(Process? process)
    {
        if (process is null)
        {
            return null;
        }

        try
        {
            return NormalizeVersion(process.MainModule?.FileName);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? NormalizeVersion(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var directMatch = DirectVersionRegex.Match(input!.Trim());
        if (directMatch.Success && int.TryParse(directMatch.Groups["version"].Value, out var directVersion))
        {
            return $"V{directVersion}";
        }

        var match = VersionRegex.Match(input);
        if (!match.Success || !int.TryParse(match.Groups["version"].Value, out var versionNumber))
        {
            return null;
        }

        return $"V{versionNumber}";
    }
}

internal sealed class TiaPortalRuntimeResolution
{
    private TiaPortalRuntimeResolution(TiaPortalRuntimeInfo? selectedRuntime, string? diagnosticCode, string? diagnosticMessage, string? detectedProcessVersion, string? selectionReason)
    {
        SelectedRuntime = selectedRuntime;
        DiagnosticCode = diagnosticCode;
        DiagnosticMessage = diagnosticMessage;
        DetectedProcessVersion = detectedProcessVersion;
        SelectionReason = selectionReason;
    }

    public TiaPortalRuntimeInfo? SelectedRuntime { get; }

    public string? DiagnosticCode { get; }

    public string? DiagnosticMessage { get; }

    public string? DetectedProcessVersion { get; }

    public string? SelectionReason { get; }

    public bool IsSuccess => SelectedRuntime is not null;

    public static TiaPortalRuntimeResolution Success(TiaPortalRuntimeInfo runtime, string? detectedProcessVersion)
    {
        var reason = !string.IsNullOrWhiteSpace(detectedProcessVersion) && string.Equals(runtime.Version, detectedProcessVersion, StringComparison.OrdinalIgnoreCase)
            ? $"Runtime {runtime.Version} selected because it matches the detected TIA process version."
            : $"Runtime {runtime.Version} selected by configuration fallback or highest discovered version.";
        return new TiaPortalRuntimeResolution(runtime, null, null, detectedProcessVersion, reason);
    }

    public static TiaPortalRuntimeResolution Fail(string diagnosticCode, string diagnosticMessage, string? detectedProcessVersion = null)
    {
        return new TiaPortalRuntimeResolution(null, diagnosticCode, diagnosticMessage, detectedProcessVersion, diagnosticMessage);
    }
}