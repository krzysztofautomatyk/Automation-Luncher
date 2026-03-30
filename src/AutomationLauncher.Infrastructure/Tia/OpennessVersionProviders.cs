using System.Reflection;
using AutomationLauncher.Domain.Models;
using Serilog;

namespace AutomationLauncher.Infrastructure.Tia;

public interface IOpennessVersionProvider
{
    bool CanHandle(TiaPortalRuntimeInfo runtime);

    TiaProjectContext TryReadOpenProject(Assembly assembly, int processId, TiaPortalRuntimeInfo runtime);

    bool TrySaveProject(Assembly assembly, string sessionId, TiaPortalRuntimeInfo runtime);

    bool TryArchiveProject(Assembly assembly, string sessionId, string destinationArchivePath, TiaPortalRuntimeInfo runtime);
}

internal abstract class OpennessVersionProviderBase : IOpennessVersionProvider
{
    private const string OpennessTypesMissingCode = "OpennessTypesMissing";
    private const string GetProcessesFailedCode = "GetProcessesFailed";
    private const string AttachFailedCode = "AttachFailed";
    private const string NoProjectOpenCode = "NoProjectOpen";

    protected virtual string[] TiaPortalTypeNames => new[] { "Siemens.Engineering.TiaPortal" };

    protected virtual string[] TiaPortalProcessTypeNames => new[] { "Siemens.Engineering.TiaPortalProcess" };

    protected virtual string[] ProjectDirtyPropertyNames => new[] { "IsModified", "Modified" };

    protected virtual string[] ProjectPathPropertyNames => new[] { "Path", "ProjectPath", "FilePath" };

    protected virtual string[] ProjectCollectionPropertyNames => new[] { "Projects", "Project" };

    protected virtual string[] ArchiveEnumTypeNames => new[] { "Siemens.Engineering.ProjectArchivationMode" };

    protected virtual string[] ArchiveModeNames => new[] { "Compressed" };

    public abstract bool CanHandle(TiaPortalRuntimeInfo runtime);

    public TiaProjectContext TryReadOpenProject(Assembly assembly, int processId, TiaPortalRuntimeInfo runtime)
    {
        var tiaPortalType = FindType(assembly, TiaPortalTypeNames);
        var processType = FindType(assembly, TiaPortalProcessTypeNames);
        if (tiaPortalType is null || processType is null)
        {
            return CreateFailure(processId, runtime, OpennessTypesMissingCode, "Configured Siemens Openness assembly does not expose TiaPortal types.");
        }

        var getProcesses = tiaPortalType.GetMethod("GetProcesses", BindingFlags.Public | BindingFlags.Static);
        if (getProcesses is null)
        {
            return CreateFailure(processId, runtime, OpennessTypesMissingCode, "Configured Siemens Openness assembly does not expose TiaPortal.GetProcesses().");
        }

        System.Collections.IEnumerable? processList;
        try
        {
            processList = getProcesses.Invoke(null, null) as System.Collections.IEnumerable;
        }
        catch (Exception ex)
        {
            return CreateFailure(processId, runtime, GetProcessesFailedCode, DescribeInvocationFailure(ex));
        }

        if (processList is null)
        {
            return CreateFailure(processId, runtime, GetProcessesFailedCode, "TIA Openness returned no process list.");
        }

        var entries = processList.Cast<object?>().Where(entry => entry is not null).Cast<object>().ToList();
        if (entries.Count == 0)
        {
            return CreateFailure(processId, runtime, GetProcessesFailedCode, "TIA Openness did not expose any attachable Portal processes.");
        }

        var selectedProcess = entries.FirstOrDefault(entry => ReadIntProperty(entry, "Id") == processId) ?? entries[0];
        var selectedSessionId = ReadIntProperty(selectedProcess, "Id").ToString();

        object? attachedPortal;
        try
        {
            attachedPortal = processType.GetMethod("Attach")?.Invoke(selectedProcess, null);
        }
        catch (Exception ex)
        {
            return CreateFailure(selectedSessionId, runtime, AttachFailedCode, DescribeAttachFailure(ex));
        }

        if (attachedPortal is null)
        {
            return CreateFailure(selectedSessionId, runtime, AttachFailedCode, "TIA Openness returned null from Attach(). Access may have been denied.");
        }

        var firstProject = ReadFirstProject(attachedPortal);
        if (firstProject is null)
        {
            return CreateFailure(selectedSessionId, runtime, NoProjectOpenCode, "Connected to TIA Portal, but Openness did not expose any open project.");
        }

        var pathValue = ReadProjectPath(firstProject);
        var nameValue = ReadStringProperty(firstProject, "Name");
        var dirtyProperty = FindProperty(firstProject.GetType(), ProjectDirtyPropertyNames);
        var hasUnsaved = ReadNullableBool(firstProject, dirtyProperty);

        return new TiaProjectContext(true, pathValue, nameValue, selectedSessionId, hasUnsaved, dirtyProperty is not null, tiaVersion: runtime.Version, opennessAssemblyPath: runtime.OpennessAssemblyPath);
    }

    public bool TrySaveProject(Assembly assembly, string sessionId, TiaPortalRuntimeInfo runtime)
    {
        var firstProject = TryGetProject(assembly, sessionId);
        if (firstProject is null)
        {
            return false;
        }

        var saveMethod = firstProject.GetType().GetMethod("Save", Type.EmptyTypes);
        if (saveMethod is null)
        {
            Log.Warning("Save method was not found on project type {ProjectType} for runtime {TiaVersion}", firstProject.GetType().FullName, runtime.Version);
            return false;
        }

        saveMethod.Invoke(firstProject, null);
        return true;
    }

    public bool TryArchiveProject(Assembly assembly, string sessionId, string destinationArchivePath, TiaPortalRuntimeInfo runtime)
    {
        var firstProject = TryGetProject(assembly, sessionId);
        if (firstProject is null)
        {
            Log.Warning("TryArchiveProject failed because no project was exposed for session {SessionId} and runtime {TiaVersion}", sessionId, runtime.Version);
            return false;
        }

        var projectType = firstProject.GetType();
        var archiveTargets = ResolveArchiveTargets(assembly, projectType).ToList();
        if (archiveTargets.Count == 0)
        {
            Log.Warning("TryArchiveProject failed because no supported archive signature was found for runtime {TiaVersion}", runtime.Version);
            return false;
        }

        var targetDirectoryPath = Path.GetDirectoryName(destinationArchivePath);
        var targetName = Path.GetFileNameWithoutExtension(destinationArchivePath);
        if (string.IsNullOrWhiteSpace(targetDirectoryPath) || string.IsNullOrWhiteSpace(targetName))
        {
            Log.Warning("TryArchiveProject failed because destination archive path is invalid: {DestinationArchivePath}", destinationArchivePath);
            return false;
        }

        Directory.CreateDirectory(targetDirectoryPath);
        var targetDirectory = new DirectoryInfo(targetDirectoryPath);

        var extensionlessArchivePath = Path.Combine(targetDirectory.FullName, targetName);
        if (TryFinalizeArchiveArtifact(extensionlessArchivePath, destinationArchivePath))
        {
            return true;
        }

        foreach (var archiveTarget in archiveTargets)
        {
            try
            {
                archiveTarget.Method.Invoke(firstProject, new[] { targetDirectory, targetName, archiveTarget.Mode });
                break;
            }
            catch (Exception ex)
            {
                var root = Unwrap(ex);
                Log.Warning(root, "TryArchiveProject invoke failed for session {SessionId}, directory {TargetDirectory}, name {TargetName}, runtime {TiaVersion}, archive enum {ArchiveEnumType}", sessionId, targetDirectory.FullName, targetName, runtime.Version, archiveTarget.EnumType.FullName);
            }
        }

        if (TryFinalizeArchiveArtifact(extensionlessArchivePath, destinationArchivePath))
        {
            return true;
        }

        var candidateFiles = Directory
            .EnumerateFiles(targetDirectory.FullName, targetName + "*.zap*", SearchOption.TopDirectoryOnly)
            .ToList();

        if (candidateFiles.Count > 0)
        {
            Log.Information("TryArchiveProject created archive candidate(s): {ArchiveCandidates}", string.Join(", ", candidateFiles));
            return true;
        }

        Log.Warning("TryArchiveProject completed without creating an archive file for session {SessionId}. Expected path {DestinationArchivePath}", sessionId, destinationArchivePath);
        return false;
    }

    private IEnumerable<ArchiveTarget> ResolveArchiveTargets(Assembly assembly, Type projectType)
    {
        foreach (var enumTypeName in ArchiveEnumTypeNames)
        {
            var enumType = assembly.GetType(enumTypeName);
            if (enumType is null)
            {
                continue;
            }

            var archiveMethod = projectType.GetMethod(
                "Archive",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(DirectoryInfo), typeof(string), enumType },
                modifiers: null);

            if (archiveMethod is null)
            {
                continue;
            }

            var archiveMode = TryResolveArchiveMode(enumType);
            if (archiveMode is null)
            {
                continue;
            }

            yield return new ArchiveTarget(enumType, archiveMethod, archiveMode);
        }
    }

    protected virtual object? ReadFirstProject(object attachedPortal)
    {
        foreach (var propertyName in ProjectCollectionPropertyNames)
        {
            var property = attachedPortal.GetType().GetProperty(propertyName);
            if (property is null)
            {
                continue;
            }

            var propertyValue = property.GetValue(attachedPortal);
            if (propertyValue is System.Collections.IEnumerable enumerable && propertyName != "Project")
            {
                var first = enumerable.Cast<object?>().FirstOrDefault();
                if (first is not null)
                {
                    return first;
                }
            }

            if (propertyValue is not null)
            {
                return propertyValue;
            }
        }

        return null;
    }

    protected virtual string? ReadProjectPath(object project)
    {
        foreach (var propertyName in ProjectPathPropertyNames)
        {
            var value = project.GetType().GetProperty(propertyName)?.GetValue(project);
            if (value is null)
            {
                continue;
            }

            if (value is FileInfo fileInfo)
            {
                return fileInfo.FullName;
            }

            if (value is DirectoryInfo directoryInfo)
            {
                return directoryInfo.FullName;
            }

            var stringValue = value.ToString();
            if (!string.IsNullOrWhiteSpace(stringValue))
            {
                return stringValue;
            }
        }

        return null;
    }

    private object? TryGetProject(Assembly assembly, string sessionId)
    {
        if (!int.TryParse(sessionId, out var processId))
        {
            return null;
        }

        var tiaPortalType = FindType(assembly, TiaPortalTypeNames);
        var processType = FindType(assembly, TiaPortalProcessTypeNames);
        if (tiaPortalType is null || processType is null)
        {
            return null;
        }

        var getProcesses = tiaPortalType.GetMethod("GetProcesses", BindingFlags.Public | BindingFlags.Static);
        var processList = getProcesses?.Invoke(null, null) as System.Collections.IEnumerable;
        if (processList is null)
        {
            return null;
        }

        object? selectedProcess = null;
        foreach (var entry in processList)
        {
            if (entry is null)
            {
                continue;
            }

            if (ReadIntProperty(entry, "Id") == processId)
            {
                selectedProcess = entry;
                break;
            }
        }

        if (selectedProcess is null)
        {
            return null;
        }

        var attachedPortal = processType.GetMethod("Attach")?.Invoke(selectedProcess, null);
        return attachedPortal is null ? null : ReadFirstProject(attachedPortal);
    }

    private object? TryResolveArchiveMode(Type archivationModeType)
    {
        foreach (var modeName in ArchiveModeNames)
        {
            if (Enum.GetNames(archivationModeType).Any(name => string.Equals(name, modeName, StringComparison.OrdinalIgnoreCase)))
            {
                return Enum.Parse(archivationModeType, modeName);
            }
        }

        return null;
    }

    private static Type? FindType(Assembly assembly, IEnumerable<string> candidateNames)
    {
        foreach (var candidateName in candidateNames)
        {
            var type = assembly.GetType(candidateName);
            if (type is not null)
            {
                return type;
            }
        }

        return null;
    }

    private static PropertyInfo? FindProperty(Type targetType, IEnumerable<string> candidateNames)
    {
        foreach (var candidateName in candidateNames)
        {
            var property = targetType.GetProperty(candidateName);
            if (property is not null)
            {
                return property;
            }
        }

        return null;
    }

    private static bool TryFinalizeArchiveArtifact(string extensionlessArchivePath, string destinationArchivePath)
    {
        if (File.Exists(destinationArchivePath))
        {
            return true;
        }

        if (!File.Exists(extensionlessArchivePath))
        {
            return false;
        }

        try
        {
            if (string.Equals(extensionlessArchivePath, destinationArchivePath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (File.Exists(destinationArchivePath))
            {
                return true;
            }

            File.Move(extensionlessArchivePath, destinationArchivePath);
            Log.Information("Renamed Siemens archive artifact from {SourceArchivePath} to {DestinationArchivePath}", extensionlessArchivePath, destinationArchivePath);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Archive artifact was created without extension at {SourceArchivePath} but could not be renamed to {DestinationArchivePath}", extensionlessArchivePath, destinationArchivePath);
            return File.Exists(extensionlessArchivePath);
        }
    }

    private static int ReadIntProperty(object target, string propertyName)
    {
        var value = target.GetType().GetProperty(propertyName)?.GetValue(target);
        if (value is null)
        {
            return -1;
        }

        try
        {
            return Convert.ToInt32(value);
        }
        catch
        {
            return -1;
        }
    }

    private static string? ReadStringProperty(object target, string propertyName)
    {
        return target.GetType().GetProperty(propertyName)?.GetValue(target)?.ToString();
    }

    private static bool? ReadNullableBool(object target, PropertyInfo? property)
    {
        if (property is null)
        {
            return null;
        }

        var value = property.GetValue(target);
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToBoolean(value);
        }
        catch
        {
            return null;
        }
    }

    private static TiaProjectContext CreateFailure(int processId, TiaPortalRuntimeInfo runtime, string diagnosticCode, string diagnosticMessage)
    {
        return CreateFailure(processId.ToString(), runtime, diagnosticCode, diagnosticMessage);
    }

    private static TiaProjectContext CreateFailure(string sessionId, TiaPortalRuntimeInfo runtime, string diagnosticCode, string diagnosticMessage)
    {
        return new TiaProjectContext(true, null, null, sessionId, null, false, diagnosticCode, diagnosticMessage, runtime.Version, runtime.OpennessAssemblyPath);
    }

    private static string DescribeInvocationFailure(Exception ex)
    {
        var root = Unwrap(ex);
        if (IsRuntimeIncompatible(root))
        {
            return "Siemens Openness runtime is not compatible with the current launcher runtime. Run AutomationLauncher on .NET Framework 4.8.";
        }

        if (root is FileLoadException or FileNotFoundException)
        {
            return $"Failed to load Siemens Openness dependency: {root.Message}";
        }

        return $"TIA Openness GetProcesses() failed: {root.Message}";
    }

    private static string DescribeAttachFailure(Exception ex)
    {
        var root = Unwrap(ex);
        var message = root.Message;
        if (message.IndexOf("access", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("security", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "TIA Portal denied Openness attach. Confirm the Openness access dialog in TIA Portal and grant permission."
                + $" Details: {message}";
        }

        return $"TIA Openness Attach() failed: {message}";
    }

    protected static Exception Unwrap(Exception ex)
    {
        while (ex is TargetInvocationException && ex.InnerException is not null)
        {
            ex = ex.InnerException;
        }

        return ex;
    }

    protected static bool IsRuntimeIncompatible(Exception ex)
    {
        return ex is MissingMethodException
            || ex.Message.IndexOf("Assembly.Load(Byte[], Byte[], System.Security.SecurityContextSource)", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private sealed class ArchiveTarget
    {
        public ArchiveTarget(Type enumType, MethodInfo method, object mode)
        {
            EnumType = enumType;
            Method = method;
            Mode = mode;
        }

        public Type EnumType { get; }

        public MethodInfo Method { get; }

        public object Mode { get; }
    }
}

internal sealed class V15OpennessVersionProvider : OpennessVersionProviderBase
{
    protected override string[] ProjectDirtyPropertyNames => new[] { "Modified", "IsModified" };

    protected override string[] ProjectPathPropertyNames => new[] { "FilePath", "Path", "ProjectPath" };

    protected override string[] ArchiveEnumTypeNames => new[] { "Siemens.Engineering.ProjectArchivationMode", "Siemens.Engineering.ProjectArchiveMode" };

    protected override string[] ArchiveModeNames => new[] { "Compressed", "Default" };

    public override bool CanHandle(TiaPortalRuntimeInfo runtime)
    {
        return TryParseVersion(runtime.Version, 15);
    }

    private static bool TryParseVersion(string version, int expected)
    {
        return int.TryParse(version.TrimStart('V', 'v'), out var parsed) && parsed == expected;
    }
}

internal sealed class V16OpennessVersionProvider : OpennessVersionProviderBase
{
    protected override string[] ProjectDirtyPropertyNames => new[] { "Modified", "IsModified" };

    protected override string[] ProjectPathPropertyNames => new[] { "FilePath", "Path", "ProjectPath" };

    public override bool CanHandle(TiaPortalRuntimeInfo runtime)
    {
        return TryParseVersion(runtime.Version, 16);
    }

    private static bool TryParseVersion(string version, int expected)
    {
        return int.TryParse(version.TrimStart('V', 'v'), out var parsed) && parsed == expected;
    }
}

internal sealed class V17OpennessVersionProvider : OpennessVersionProviderBase
{
    public override bool CanHandle(TiaPortalRuntimeInfo runtime)
    {
        return TryParseVersion(runtime.Version, 17);
    }

    private static bool TryParseVersion(string version, int expected)
    {
        return int.TryParse(version.TrimStart('V', 'v'), out var parsed) && parsed == expected;
    }
}

internal sealed class V18OpennessVersionProvider : OpennessVersionProviderBase
{
    public override bool CanHandle(TiaPortalRuntimeInfo runtime)
    {
        return TryParseVersion(runtime.Version, 18);
    }

    private static bool TryParseVersion(string version, int expected)
    {
        return int.TryParse(version.TrimStart('V', 'v'), out var parsed) && parsed == expected;
    }
}

internal sealed class V19OpennessVersionProvider : OpennessVersionProviderBase
{
    public override bool CanHandle(TiaPortalRuntimeInfo runtime)
    {
        return TryParseVersion(runtime.Version, 19);
    }

    private static bool TryParseVersion(string version, int expected)
    {
        return int.TryParse(version.TrimStart('V', 'v'), out var parsed) && parsed == expected;
    }
}

internal sealed class LatestOpennessVersionProvider : OpennessVersionProviderBase
{
    protected override string[] ProjectPathPropertyNames => new[] { "Path", "ProjectPath", "FilePath", "Location" };

    protected override string[] ArchiveModeNames => new[] { "Compressed", "Default" };

    public override bool CanHandle(TiaPortalRuntimeInfo runtime)
    {
        return TryParseVersion(runtime.Version, out var version) && version >= 20;
    }

    private static bool TryParseVersion(string version, out int parsed)
    {
        return int.TryParse(version.TrimStart('V', 'v'), out parsed);
    }
}