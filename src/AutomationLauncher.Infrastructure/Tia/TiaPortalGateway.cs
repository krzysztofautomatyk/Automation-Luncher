using System.Diagnostics;
using System.Reflection;
using AutomationLauncher.Domain.Contracts;
using AutomationLauncher.Domain.Models;
using Serilog;

namespace AutomationLauncher.Infrastructure.Tia;

public sealed class TiaPortalGateway : ITiaPortalGateway
{
    private const string TiaNotRunningCode = "TiaNotRunning";
    private const string OpennessAssemblyMissingCode = "OpennessAssemblyMissing";
    private const string OpennessAssemblyLoadFailedCode = "OpennessAssemblyLoadFailed";
    private const string OpennessRuntimeIncompatibleCode = "OpennessRuntimeIncompatible";
    private const string OpennessTypesMissingCode = "OpennessTypesMissing";
    private const string GetProcessesFailedCode = "GetProcessesFailed";
    private const string AttachFailedCode = "AttachFailed";
    private const string NoProjectOpenCode = "NoProjectOpen";

    private static readonly string[] KnownTiaProcessNames =
    {
        "Siemens.Automation.Portal",
        "Siemens.Automation.Portalx",
        "Portal"
    };

    private readonly ArchiveOptions _options;

    public TiaPortalGateway(ArchiveOptions options)
    {
        _options = options;
    }

    public Task<TiaProjectContext> GetCurrentContextAsync(CancellationToken cancellationToken)
    {
        var process = FindRunningTiaProcess();
        if (process is null)
        {
            return Task.FromResult(new TiaProjectContext(false, null, null, null, null, false, TiaNotRunningCode, "TIA Portal process was not found."));
        }

        if (string.IsNullOrWhiteSpace(_options.OpennessAssemblyPath) || !File.Exists(_options.OpennessAssemblyPath))
        {
            var message = $"Configured Siemens Openness assembly was not found: {_options.OpennessAssemblyPath}";
            return Task.FromResult(new TiaProjectContext(true, null, null, process.Id.ToString(), null, false, OpennessAssemblyMissingCode, message));
        }

        try
        {
            var assembly = Assembly.LoadFrom(_options.OpennessAssemblyPath);
            var probe = new OpennessReflectionProbe(assembly);
            return Task.FromResult(probe.TryReadOpenProject(process.Id));
        }
        catch (Exception ex)
        {
            var context = BuildContextFromFailure(process.Id.ToString(), ex);
            Log.Warning(ex, "Unable to query TIA Openness context. Code={DiagnosticCode} Message={DiagnosticMessage}", context.DiagnosticCode, context.DiagnosticMessage);
            return Task.FromResult(context);
        }
    }

    public async Task<bool> SaveProjectAsync(string sessionId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(_options.OpennessAssemblyPath) || !File.Exists(_options.OpennessAssemblyPath))
                {
                    return true;
                }

                var assembly = Assembly.LoadFrom(_options.OpennessAssemblyPath);
                var probe = new OpennessReflectionProbe(assembly);
                return probe.TrySaveProject(sessionId);
            }, cts.Token);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SaveProjectAsync failed for session {SessionId}", sessionId);
            return false;
        }
    }

    public async Task<bool> ArchiveProjectAsync(string sessionId, string destinationArchivePath, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(_options.OpennessAssemblyPath) || !File.Exists(_options.OpennessAssemblyPath))
                {
                    return false;
                }

                var assembly = Assembly.LoadFrom(_options.OpennessAssemblyPath);
                var probe = new OpennessReflectionProbe(assembly);
                return probe.TryArchiveProject(sessionId, destinationArchivePath);
            }, cts.Token);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ArchiveProjectAsync failed for session {SessionId} and destination {DestinationArchivePath}", sessionId, destinationArchivePath);
            return false;
        }
    }

    private static Process? FindRunningTiaProcess()
    {
        foreach (var processName in KnownTiaProcessNames)
        {
            var process = Process.GetProcessesByName(processName).FirstOrDefault();
            if (process is not null)
            {
                return process;
            }
        }

        return null;
    }

    private sealed class OpennessReflectionProbe
    {
        private readonly Assembly _assembly;

        public OpennessReflectionProbe(Assembly assembly)
        {
            _assembly = assembly;
        }

        public TiaProjectContext TryReadOpenProject(int processId)
        {
            var tiaPortalType = _assembly.GetType("Siemens.Engineering.TiaPortal");
            var processType = _assembly.GetType("Siemens.Engineering.TiaPortalProcess");
            if (tiaPortalType is null || processType is null)
            {
                return CreateFailure(processId, OpennessTypesMissingCode, "Configured Siemens Openness assembly does not expose TiaPortal types.");
            }

            var getProcesses = tiaPortalType.GetMethod("GetProcesses", BindingFlags.Public | BindingFlags.Static);
            if (getProcesses is null)
            {
                return CreateFailure(processId, OpennessTypesMissingCode, "Configured Siemens Openness assembly does not expose TiaPortal.GetProcesses().");
            }

            System.Collections.IEnumerable? processList;
            try
            {
                processList = getProcesses.Invoke(null, null) as System.Collections.IEnumerable;
            }
            catch (Exception ex)
            {
                return CreateFailure(processId, GetProcessesFailedCode, DescribeInvocationFailure(ex));
            }

            if (processList is null)
            {
                return CreateFailure(processId, GetProcessesFailedCode, "TIA Openness returned no process list.");
            }

            var entries = processList.Cast<object?>().Where(x => x is not null).Cast<object>().ToList();
            if (entries.Count == 0)
            {
                return CreateFailure(processId, GetProcessesFailedCode, "TIA Openness did not expose any attachable Portal processes.");
            }

            var selectedProcess = entries.FirstOrDefault(p => ReadIntProperty(p, "Id") == processId) ?? entries[0];
            var selectedSessionId = ReadIntProperty(selectedProcess, "Id").ToString();

            object? attachedPortal;
            try
            {
                attachedPortal = processType.GetMethod("Attach")?.Invoke(selectedProcess, null);
            }
            catch (Exception ex)
            {
                return CreateFailure(selectedSessionId, AttachFailedCode, DescribeAttachFailure(ex));
            }

            if (attachedPortal is null)
            {
                return CreateFailure(selectedSessionId, AttachFailedCode, "TIA Openness returned null from Attach(). Access may have been denied.");
            }

            var firstProject = ReadFirstProject(attachedPortal);
            if (firstProject is null)
            {
                return CreateFailure(selectedSessionId, NoProjectOpenCode, "Connected to TIA Portal, but Openness did not expose any open project.");
            }

            var pathValue = ReadProjectPath(firstProject);
            var nameValue = ReadStringProperty(firstProject, "Name");

            var dirtyProp = firstProject.GetType().GetProperty("IsModified")
                ?? firstProject.GetType().GetProperty("Modified");
            var hasUnsaved = ReadNullableBool(firstProject, dirtyProp);
            var reliable = dirtyProp is not null;

            return new TiaProjectContext(true, pathValue, nameValue, selectedSessionId, hasUnsaved, reliable);
        }

        public bool TrySaveProject(string sessionId)
        {
            var processId = int.Parse(sessionId);
            var tiaPortalType = _assembly.GetType("Siemens.Engineering.TiaPortal");
            var processType = _assembly.GetType("Siemens.Engineering.TiaPortalProcess");
            if (tiaPortalType is null || processType is null)
            {
                return false;
            }

            var getProcesses = tiaPortalType.GetMethod("GetProcesses", BindingFlags.Public | BindingFlags.Static);
            var processList = getProcesses?.Invoke(null, null) as System.Collections.IEnumerable;
            if (processList is null)
            {
                return false;
            }

            object? selectedProcess = null;
            foreach (var p in processList)
            {
                var idValue = ReadIntProperty(p!, "Id");
                if (idValue == processId)
                {
                    selectedProcess = p;
                    break;
                }
            }

            if (selectedProcess is null)
            {
                return false;
            }

            var attachedPortal = processType.GetMethod("Attach")?.Invoke(selectedProcess, null);
            if (attachedPortal is null)
            {
                return false;
            }

            var firstProject = ReadFirstProject(attachedPortal);
            if (firstProject is null)
            {
                return false;
            }

            var saveMethod = firstProject.GetType().GetMethod("Save", Type.EmptyTypes);
            if (saveMethod is null)
            {
                return false;
            }

            saveMethod.Invoke(firstProject, null);
            return true;
        }

        public bool TryArchiveProject(string sessionId, string destinationArchivePath)
        {
            var processId = int.Parse(sessionId);
            var tiaPortalType = _assembly.GetType("Siemens.Engineering.TiaPortal");
            var processType = _assembly.GetType("Siemens.Engineering.TiaPortalProcess");
            if (tiaPortalType is null || processType is null)
            {
                return false;
            }

            var getProcesses = tiaPortalType.GetMethod("GetProcesses", BindingFlags.Public | BindingFlags.Static);
            var processList = getProcesses?.Invoke(null, null) as System.Collections.IEnumerable;
            if (processList is null)
            {
                return false;
            }

            object? selectedProcess = null;
            foreach (var p in processList)
            {
                var idValue = ReadIntProperty(p!, "Id");
                if (idValue == processId)
                {
                    selectedProcess = p;
                    break;
                }
            }

            if (selectedProcess is null)
            {
                return false;
            }

            var attachedPortal = processType.GetMethod("Attach")?.Invoke(selectedProcess, null);
            if (attachedPortal is null)
            {
                return false;
            }

            var firstProject = ReadFirstProject(attachedPortal);
            if (firstProject is null)
            {
                Log.Warning("TryArchiveProject failed because no project was exposed for session {SessionId}", sessionId);
                return false;
            }

            var archivationModeType = _assembly.GetType("Siemens.Engineering.ProjectArchivationMode");
            if (archivationModeType is null)
            {
                Log.Warning("TryArchiveProject failed because Siemens.Engineering.ProjectArchivationMode was not found.");
                return false;
            }

            var projectType = firstProject.GetType();
            var archiveMethod = projectType.GetMethod(
                "Archive",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(DirectoryInfo), typeof(string), archivationModeType },
                modifiers: null);

            if (archiveMethod is null)
            {
                Log.Warning("TryArchiveProject failed because Archive(DirectoryInfo, string, ProjectArchivationMode) was not found on {ProjectType}", projectType.FullName);
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
            var archivationMode = Enum.Parse(archivationModeType, "Compressed");
            var extensionlessArchivePath = Path.Combine(targetDirectory.FullName, targetName);

            if (TryFinalizeArchiveArtifact(extensionlessArchivePath, destinationArchivePath))
            {
                return true;
            }

            try
            {
                archiveMethod.Invoke(firstProject, new[] { targetDirectory, targetName, archivationMode });
            }
            catch (Exception ex)
            {
                var root = Unwrap(ex);
                Log.Warning(root, "TryArchiveProject invoke failed for session {SessionId}, directory {TargetDirectory}, name {TargetName}", sessionId, targetDirectory.FullName, targetName);
                return false;
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

        private static bool? ReadNullableBool(object target, PropertyInfo? prop)
        {
            if (prop is null)
            {
                return null;
            }

            var value = prop.GetValue(target);
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

        private static object? ReadFirstProject(object attachedPortal)
        {
            var projectsProp = attachedPortal.GetType().GetProperty("Projects");
            var projects = projectsProp?.GetValue(attachedPortal) as System.Collections.IEnumerable;
            if (projects is not null)
            {
                var first = projects.Cast<object?>().FirstOrDefault();
                if (first is not null)
                {
                    return first;
                }
            }

            return attachedPortal.GetType().GetProperty("Project")?.GetValue(attachedPortal);
        }

        private static string? ReadProjectPath(object project)
        {
            var candidateProperties = new[] { "Path", "ProjectPath", "FilePath" };
            foreach (var propertyName in candidateProperties)
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

        private static TiaProjectContext CreateFailure(int processId, string diagnosticCode, string diagnosticMessage)
        {
            return CreateFailure(processId.ToString(), diagnosticCode, diagnosticMessage);
        }

        private static TiaProjectContext CreateFailure(string sessionId, string diagnosticCode, string diagnosticMessage)
        {
            return new TiaProjectContext(true, null, null, sessionId, null, false, diagnosticCode, diagnosticMessage);
        }

        private static string DescribeInvocationFailure(Exception ex)
        {
            var root = Unwrap(ex);
            if (IsRuntimeIncompatible(root))
            {
                return "Siemens Openness V19 is not compatible with the current runtime. Run AutomationLauncher on .NET Framework 4.8.";
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

        private static Exception Unwrap(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException is not null)
            {
                ex = ex.InnerException;
            }

            return ex;
        }

        private static bool IsRuntimeIncompatible(Exception ex)
        {
            return ex is MissingMethodException
                || ex.Message.IndexOf("Assembly.Load(Byte[], Byte[], System.Security.SecurityContextSource)", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    private static TiaProjectContext BuildContextFromFailure(string sessionId, Exception ex)
    {
        var root = Unwrap(ex);

        if (IsRuntimeIncompatible(root))
        {
            return new TiaProjectContext(
                true,
                null,
                null,
                sessionId,
                null,
                false,
                OpennessRuntimeIncompatibleCode,
                "Siemens Openness V19 is not compatible with the current runtime. Run AutomationLauncher on .NET Framework 4.8.");
        }

        if (root is FileNotFoundException or FileLoadException)
        {
            return new TiaProjectContext(
                true,
                null,
                null,
                sessionId,
                null,
                false,
                OpennessAssemblyLoadFailedCode,
                $"Failed to load Siemens Openness dependency: {root.Message}");
        }

        return new TiaProjectContext(
            true,
            null,
            null,
            sessionId,
            null,
            false,
            GetProcessesFailedCode,
            $"Unable to query Siemens Openness: {root.Message}");
    }

    private static Exception Unwrap(Exception ex)
    {
        while (ex is TargetInvocationException && ex.InnerException is not null)
        {
            ex = ex.InnerException;
        }

        return ex;
    }

    private static bool IsRuntimeIncompatible(Exception ex)
    {
        return ex is MissingMethodException
            || ex.Message.IndexOf("Assembly.Load(Byte[], Byte[], System.Security.SecurityContextSource)", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
