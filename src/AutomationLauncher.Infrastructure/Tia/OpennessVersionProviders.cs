using System.Reflection;
using AutomationLauncher.Domain.Models;
using Serilog;

namespace AutomationLauncher.Infrastructure.Tia;

internal abstract class OpennessVersionProviderBase : IOpennessVersionProvider
{
    private const string OpennessTypesMissingCode = "OpennessTypesMissing";
    private const string GetProcessesFailedCode = "GetProcessesFailed";
    private const string AttachFailedCode = "AttachFailed";
    private const string NoProjectOpenCode = "NoProjectOpen";
    private const string PlcCompareUnavailableCode = "PlcCompareUnavailable";
    private const string PlcCompareInvocationFailedCode = "PlcCompareInvocationFailed";
    private const string GoOfflineNoDevicesCode = "GoOfflineNoDevices";
    private const string GoOfflineFailedCode = "GoOfflineFailed";
    private const string GoOfflineOnlineProviderMissingCode = "GoOfflineOnlineProviderMissing";

    protected virtual string[] TiaPortalTypeNames => new[] { "Siemens.Engineering.TiaPortal" };

    protected virtual string[] TiaPortalProcessTypeNames => new[] { "Siemens.Engineering.TiaPortalProcess" };

    protected virtual string[] ProjectDirtyPropertyNames => new[] { "IsModified", "Modified" };

    protected virtual string[] ProjectPathPropertyNames => new[] { "Path", "ProjectPath", "FilePath" };

    protected virtual string[] ProjectCollectionPropertyNames => new[] { "Projects", "Project" };

    protected virtual string[] ArchiveEnumTypeNames => new[] { "Siemens.Engineering.ProjectArchivationMode" };

    protected virtual string[] ArchiveModeNames => new[] { "Compressed" };

    protected virtual string[] SoftwareContainerTypeNames => new[] { "Siemens.Engineering.HW.Features.SoftwareContainer" };

    protected virtual string[] OnlineProviderTypeNames => new[] { "Siemens.Engineering.Online.OnlineProvider" };

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

    public OnlineStateResult TryCheckOnlineState(Assembly assembly, string sessionId, TiaPortalRuntimeInfo runtime)
    {
        var onlineProviderType = FindType(assembly, OnlineProviderTypeNames);
        if (onlineProviderType is null)
        {
            return new OnlineStateResult(false, false, 0, GoOfflineOnlineProviderMissingCode, $"OnlineProvider type not found for runtime {runtime.Version}.");
        }

        var firstProject = TryGetProject(assembly, sessionId);
        if (firstProject is null)
        {
            return new OnlineStateResult(false, false, 0, NoProjectOpenCode, "No open project available for online state check.");
        }

        var devices = ReadDevices(firstProject);
        if (devices is null || devices.Count == 0)
        {
            Log.Information("OnlineStateCheck: No devices found in project for session {SessionId}", sessionId);
            return new OnlineStateResult(true, false, 0, null, "No devices found in project.");
        }

        Log.Information("OnlineStateCheck: Found {DeviceCount} device(s) in project for session {SessionId}", devices.Count, sessionId);

        var onlineCount = 0;
        foreach (var device in devices)
        {
            var deviceName = ReadStringProperty(device, "Name") ?? "unknown";
            var deviceItems = ReadDeviceItems(device);
            if (deviceItems is null)
            {
                Log.Information("OnlineStateCheck: Device {DeviceName} has no DeviceItems", deviceName);
                continue;
            }

            Log.Information("OnlineStateCheck: Device {DeviceName} has {ItemCount} top-level DeviceItem(s)", deviceName, deviceItems.Count);

            foreach (var deviceItem in deviceItems)
            {
                onlineCount += CountOnlineRecursive(deviceItem, onlineProviderType, deviceName);
            }
        }

        Log.Information("OnlineStateCheck: Total online providers found: {OnlineCount}", onlineCount);
        return new OnlineStateResult(true, onlineCount > 0, onlineCount);
    }

    private int CountOnlineRecursive(object deviceItem, Type onlineProviderType, string deviceName, string parentPath = "")
    {
        var count = 0;
        var itemName = ReadStringProperty(deviceItem, "Name") ?? "?";
        var path = string.IsNullOrEmpty(parentPath) ? itemName : $"{parentPath}/{itemName}";

        var onlineProvider = ResolveServiceByType(deviceItem, onlineProviderType);
        if (onlineProvider is not null)
        {
            var stateName = ReadOnlineState(onlineProvider);
            var isOnline = string.Equals(stateName, "Online", StringComparison.OrdinalIgnoreCase);
            Log.Information("OnlineStateCheck: [{DeviceName}] {ItemPath} → OnlineProvider.State={State} (online={IsOnline})",
                deviceName, path, stateName, isOnline);
            if (isOnline)
            {
                count++;
            }
        }

        var childItems = ReadDeviceItems(deviceItem);
        if (childItems is not null)
        {
            foreach (var child in childItems)
            {
                count += CountOnlineRecursive(child, onlineProviderType, deviceName, path);
            }
        }

        return count;
    }

    public PlcOnlineOfflineComparisonResult TryCompareOnlineOffline(Assembly assembly, string sessionId, TiaPortalRuntimeInfo runtime)
    {
        var firstProject = TryGetProject(assembly, sessionId);
        if (firstProject is null)
        {
            return new PlcOnlineOfflineComparisonResult(false, false, NoProjectOpenCode, "No open project available for PLC comparison.");
        }

        var softwareContainerType = FindType(assembly, SoftwareContainerTypeNames);
        if (softwareContainerType is null)
        {
            return new PlcOnlineOfflineComparisonResult(false, false, PlcCompareUnavailableCode, $"SoftwareContainer type not found for runtime {runtime.Version}.");
        }

        var devices = ReadDevices(firstProject);
        if (devices is null || devices.Count == 0)
        {
            return new PlcOnlineOfflineComparisonResult(true, true, null, "No devices in project; nothing to compare.");
        }

        Log.Information("CompareOnlineOffline: Found {DeviceCount} device(s) in project for session {SessionId}", devices.Count, sessionId);

        var comparedAny = false;
        foreach (var device in devices)
        {
            var deviceName = ReadStringProperty(device, "Name") ?? "unknown";
            var deviceItems = ReadDeviceItems(device);
            if (deviceItems is null)
            {
                Log.Information("CompareOnlineOffline: Device {DeviceName} has no DeviceItems", deviceName);
                continue;
            }

            foreach (var deviceItem in deviceItems)
            {
                var result = CompareOnlineRecursive(deviceItem, softwareContainerType, runtime, sessionId, deviceName);
                if (result is not null)
                {
                    if (!result.IsEqual)
                    {
                        return result;
                    }

                    comparedAny = true;
                }
            }
        }

        if (comparedAny)
        {
            return new PlcOnlineOfflineComparisonResult(true, true);
        }

        return new PlcOnlineOfflineComparisonResult(false, false, PlcCompareUnavailableCode, "No PLC software with CompareToOnline found in project.");
    }

    private PlcOnlineOfflineComparisonResult? CompareOnlineRecursive(object deviceItem, Type softwareContainerType, TiaPortalRuntimeInfo runtime, string sessionId, string parentPath = "")
    {
        var itemName = ReadStringProperty(deviceItem, "Name") ?? "?";
        var path = string.IsNullOrEmpty(parentPath) ? itemName : $"{parentPath}/{itemName}";

        var container = ResolveServiceByType(deviceItem, softwareContainerType);
        if (container is not null)
        {
            var software = container.GetType().GetProperty("Software")?.GetValue(container);
            if (software is not null)
            {
                var softwareType = software.GetType().Name;
                Log.Information("CompareOnlineOffline: [{ItemPath}] Found SoftwareContainer → {SoftwareType}", path, softwareType);

                var compareToOnline = software.GetType().GetMethod("CompareToOnline", Type.EmptyTypes);
                if (compareToOnline is not null)
                {
                    try
                    {
                        Log.Information("CompareOnlineOffline: [{ItemPath}] Invoking CompareToOnline()...", path);
                        var compareResult = compareToOnline.Invoke(software, null);
                        var hasDifferences = CheckCompareResultForDifferences(compareResult);
                        LogCompareResultTree(compareResult, path);

                        var diagnosticMsg = hasDifferences
                            ? $"Differences found at [{path}]. See preceding CompareDetail log entries."
                            : null;
                        return new PlcOnlineOfflineComparisonResult(true, !hasDifferences, null, diagnosticMsg);
                    }
                    catch (Exception ex)
                    {
                        var root = Unwrap(ex);
                        Log.Warning(root, "CompareToOnline failed at [{ItemPath}] for runtime {TiaVersion} and session {SessionId}", path, runtime.Version, sessionId);
                        return new PlcOnlineOfflineComparisonResult(false, false, PlcCompareInvocationFailedCode, $"[{path}] {root.Message}");
                    }
                }
                else
                {
                    Log.Information("CompareOnlineOffline: [{ItemPath}] {SoftwareType} does not expose CompareToOnline()", path, softwareType);
                }
            }
        }

        var childItems = ReadDeviceItems(deviceItem);
        if (childItems is not null)
        {
            foreach (var child in childItems)
            {
                var result = CompareOnlineRecursive(child, softwareContainerType, runtime, sessionId, path);
                if (result is not null)
                {
                    return result;
                }
            }
        }

        return null;
    }

    private static bool CheckCompareResultForDifferences(object? compareResult)
    {
        if (compareResult is null)
        {
            return false;
        }

        var rootElement = compareResult.GetType().GetProperty("RootElement")?.GetValue(compareResult);
        if (rootElement is null)
        {
            return false;
        }

        var comparisonResult = rootElement.GetType().GetProperty("ComparisonResult")?.GetValue(rootElement);
        if (comparisonResult is null)
        {
            return false;
        }

        var stateName = comparisonResult.ToString() ?? string.Empty;
        return IsDifferenceState(stateName);
    }

    private static bool IsDifferenceState(string stateName)
    {
        return stateName.IndexOf("Different", StringComparison.OrdinalIgnoreCase) >= 0
            || stateName.IndexOf("Missing", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void LogCompareResultTree(object? compareResult, string plcPath)
    {
        if (compareResult is null)
        {
            Log.Information("CompareDetail: [{PlcPath}] CompareResult is null", plcPath);
            return;
        }

        var rootElement = compareResult.GetType().GetProperty("RootElement")?.GetValue(compareResult);
        if (rootElement is null)
        {
            Log.Information("CompareDetail: [{PlcPath}] CompareResult.RootElement is null", plcPath);
            return;
        }

        LogCompareElement(rootElement, plcPath, depth: 0);
    }

    private static void LogCompareElement(object element, string plcPath, int depth)
    {
        var leftName = ReadStringProperty(element, "LeftName") ?? "";
        var rightName = ReadStringProperty(element, "RightName") ?? "";
        var comparisonResult = element.GetType().GetProperty("ComparisonResult")?.GetValue(element);
        var stateName = comparisonResult?.ToString() ?? "unknown";
        var detailedInfo = ReadStringProperty(element, "DetailedInformation") ?? "";

        var indent = new string(' ', depth * 2);
        var displayName = !string.IsNullOrEmpty(leftName) ? leftName : rightName;

        if (IsDifferenceState(stateName))
        {
            Log.Warning("CompareDetail: [{PlcPath}] {Indent}{ObjectName} → {CompareState} Left={LeftName} Right={RightName} Detail={DetailedInfo}",
                plcPath, indent, displayName, stateName, leftName, rightName, detailedInfo);
        }
        else if (depth <= 1)
        {
            Log.Information("CompareDetail: [{PlcPath}] {Indent}{ObjectName} → {CompareState}",
                plcPath, indent, displayName, stateName);
        }

        var elementsProperty = element.GetType().GetProperty("Elements");
        if (elementsProperty is null)
        {
            return;
        }

        var elements = elementsProperty.GetValue(element) as System.Collections.IEnumerable;
        if (elements is null)
        {
            return;
        }

        foreach (var child in elements)
        {
            if (child is not null)
            {
                LogCompareElement(child, plcPath, depth + 1);
            }
        }
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

    public GoOfflineResult TryGoOffline(Assembly assembly, string sessionId, TiaPortalRuntimeInfo runtime)
    {
        var firstProject = TryGetProject(assembly, sessionId);
        if (firstProject is null)
        {
            return new GoOfflineResult(false, 0, 0, GoOfflineNoDevicesCode, "No open project available for GoOffline.");
        }

        var onlineProviderType = FindType(assembly, OnlineProviderTypeNames);
        if (onlineProviderType is null)
        {
            return new GoOfflineResult(false, 0, 0, GoOfflineOnlineProviderMissingCode, $"OnlineProvider type not found for runtime {runtime.Version}.");
        }

        var devices = ReadDevices(firstProject);
        if (devices is null || devices.Count == 0)
        {
            return new GoOfflineResult(true, 0, 0, null, "No devices found in project.");
        }

        var devicesProcessed = 0;
        var devicesSetOffline = 0;
        var errors = new List<string>();

        foreach (var device in devices)
        {
            var deviceName = ReadStringProperty(device, "Name") ?? "unknown";
            var deviceItems = ReadDeviceItems(device);
            if (deviceItems is null)
            {
                Log.Information("GoOffline: Device {DeviceName} has no DeviceItems", deviceName);
                continue;
            }

            foreach (var deviceItem in deviceItems)
            {
                devicesProcessed++;
                try
                {
                    var count = GoOfflineRecursive(deviceItem, onlineProviderType, deviceName);
                    devicesSetOffline += count;
                }
                catch (Exception ex)
                {
                    var root = Unwrap(ex);
                    Log.Warning(root, "GoOffline failed for device {DeviceName} in session {SessionId}", deviceName, sessionId);
                    errors.Add($"{deviceName}: {root.Message}");
                }
            }
        }

        if (errors.Count > 0)
        {
            return new GoOfflineResult(false, devicesProcessed, devicesSetOffline, GoOfflineFailedCode, $"GoOffline partially failed. {string.Join("; ", errors)}");
        }

        return new GoOfflineResult(true, devicesProcessed, devicesSetOffline);
    }

    private int GoOfflineRecursive(object deviceItem, Type onlineProviderType, string deviceName, string parentPath = "")
    {
        var count = 0;
        var itemName = ReadStringProperty(deviceItem, "Name") ?? "?";
        var path = string.IsNullOrEmpty(parentPath) ? itemName : $"{parentPath}/{itemName}";

        var onlineProvider = ResolveServiceByType(deviceItem, onlineProviderType);
        if (onlineProvider is not null)
        {
            var stateName = ReadOnlineState(onlineProvider);
            if (string.Equals(stateName, "Online", StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("GoOffline: [{DeviceName}] {ItemPath} State={State} → invoking GoOffline()", deviceName, path, stateName);
                InvokeGoOffline(onlineProvider);
                count++;
            }
            else
            {
                Log.Information("GoOffline: [{DeviceName}] {ItemPath} State={State} → already offline, skipping", deviceName, path, stateName);
            }
        }

        var childItems = ReadDeviceItems(deviceItem);
        if (childItems is not null)
        {
            foreach (var child in childItems)
            {
                count += GoOfflineRecursive(child, onlineProviderType, deviceName, path);
            }
        }

        return count;
    }

    private static bool IsOnline(object onlineProvider)
    {
        var stateName = ReadOnlineState(onlineProvider);
        return string.Equals(stateName, "Online", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadOnlineState(object onlineProvider)
    {
        var stateProperty = onlineProvider.GetType().GetProperty("State");
        if (stateProperty is null)
        {
            return "NoStateProperty";
        }

        var stateValue = stateProperty.GetValue(onlineProvider);
        return stateValue?.ToString() ?? "null";
    }

    private static void InvokeGoOffline(object onlineProvider)
    {
        var method = onlineProvider.GetType().GetMethod("GoOffline", Type.EmptyTypes);
        if (method is null)
        {
            throw new InvalidOperationException($"GoOffline method not found on {onlineProvider.GetType().FullName}.");
        }

        method.Invoke(onlineProvider, null);
    }

    private static object? ResolveServiceByType(object target, Type serviceType)
    {
        var genericGetService = FindGenericGetService(target.GetType());

        if (genericGetService is null)
        {
            return null;
        }

        try
        {
            var typedMethod = genericGetService.MakeGenericMethod(serviceType);
            return typedMethod.Invoke(target, null);
        }
        catch
        {
            return null;
        }
    }

    private static List<object>? ReadDevices(object project)
    {
        var devicesProperty = project.GetType().GetProperty("Devices");
        if (devicesProperty is null)
        {
            return null;
        }

        var devices = devicesProperty.GetValue(project) as System.Collections.IEnumerable;
        return devices?.Cast<object>().ToList();
    }

    private static List<object>? ReadDeviceItems(object deviceOrItem)
    {
        var property = deviceOrItem.GetType().GetProperty("DeviceItems");
        if (property is null)
        {
            return null;
        }

        var items = property.GetValue(deviceOrItem) as System.Collections.IEnumerable;
        return items?.Cast<object>().ToList();
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

    /// <summary>
    /// Finds a generic GetService&lt;T&gt;() method on the target type.
    /// Checks public instance methods first, then falls back to interface methods
    /// (explicit IEngineeringServiceProvider implementations in TIA V19+).
    /// </summary>
    private static MethodInfo? FindGenericGetService(Type targetType)
    {
        var method = targetType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m =>
                m.Name == "GetService"
                && m.IsGenericMethodDefinition
                && m.GetParameters().Length == 0);

        if (method is not null)
        {
            return method;
        }

        foreach (var iface in targetType.GetInterfaces())
        {
            method = iface.GetMethods()
                .FirstOrDefault(m =>
                    m.Name == "GetService"
                    && m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 0);

            if (method is not null)
            {
                var map = targetType.GetInterfaceMap(iface);
                for (var i = 0; i < map.InterfaceMethods.Length; i++)
                {
                    if (map.InterfaceMethods[i] == method)
                    {
                        return map.TargetMethods[i];
                    }
                }

                return method;
            }
        }

        return null;
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
