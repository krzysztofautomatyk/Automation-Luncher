using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using AutomationLauncher.Domain.Contracts;
using AutomationLauncher.Domain.Models;
using Polly;
using Polly.Retry;
using Serilog;

namespace AutomationLauncher.Infrastructure.Tia;

public sealed class TiaPortalGateway : ITiaPortalGateway
{
    private const string TiaNotRunningCode = "TiaNotRunning";
    private const string OpennessAssemblyLoadFailedCode = "OpennessAssemblyLoadFailed";
    private const string OpennessRuntimeIncompatibleCode = "OpennessRuntimeIncompatible";
    private const string GetProcessesFailedCode = "GetProcessesFailed";

    private static readonly string[] KnownTiaProcessNames =
    {
        "Siemens.Automation.Portal",
        "Siemens.Automation.Portalx",
        "Portal"
    };

    private readonly TiaPortalRuntimeResolver _runtimeResolver;
    private readonly IReadOnlyList<IOpennessVersionProvider> _providers;
    private readonly AsyncRetryPolicy _tiaRetryPolicy;

    public TiaPortalGateway(TiaPortalRuntimeResolver runtimeResolver, IEnumerable<IOpennessVersionProvider> providers)
    {
        _runtimeResolver = runtimeResolver;
        _providers = providers.ToList();
        _tiaRetryPolicy = Policy
            .Handle<Exception>(IsRetryableTiaException)
            .WaitAndRetryAsync(
                3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                (exception, delay, retryCount, _) =>
                {
                    Log.Warning(exception,
                        "Transient TIA operation failure. RetryAttempt={RetryAttempt} DelaySeconds={DelaySeconds}",
                        retryCount,
                        delay.TotalSeconds);
                });
    }

    public Task<TiaProjectContext> GetCurrentContextAsync(CancellationToken cancellationToken)
    {
        var process = FindRunningTiaProcess();
        if (process is null)
        {
            return Task.FromResult(new TiaProjectContext(false, null, null, null, null, false, TiaNotRunningCode, "TIA Portal process was not found."));
        }

        var resolution = _runtimeResolver.Resolve(process);
        if (!resolution.IsSuccess || resolution.SelectedRuntime is null)
        {
            return Task.FromResult(new TiaProjectContext(true, null, null, process.Id.ToString(), null, false, resolution.DiagnosticCode, resolution.DiagnosticMessage, null, null, null, resolution.SelectionReason, resolution.DetectedProcessVersion));
        }

        try
        {
            var assembly = Assembly.LoadFrom(resolution.SelectedRuntime.OpennessAssemblyPath);
            var provider = ResolveProvider(resolution.SelectedRuntime);
            var context = provider.TryReadOpenProject(assembly, process.Id, resolution.SelectedRuntime);
            return Task.FromResult(AttachSelectionDiagnostics(context, resolution, provider));
        }
        catch (Exception ex)
        {
            var provider = ResolveProvider(resolution.SelectedRuntime);
            var context = BuildContextFromFailure(process.Id.ToString(), resolution.SelectedRuntime, ex, provider, resolution.SelectionReason, resolution.DetectedProcessVersion);
            Log.Warning(ex, "Unable to query TIA Openness context. Code={DiagnosticCode} Message={DiagnosticMessage} Version={TiaVersion}", context.DiagnosticCode, context.DiagnosticMessage, context.TiaVersion);
            return Task.FromResult(context);
        }
    }

    public async Task<OnlineStateResult> CheckOnlineStateAsync(string sessionId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            return await _tiaRetryPolicy.ExecuteAsync(async token =>
            {
                token.ThrowIfCancellationRequested();
                return await Task.Run(() =>
                {
                    var process = FindProcessById(sessionId)
                        ?? throw new InvalidOperationException($"TIA process for session {sessionId} was not found.");

                    var resolution = _runtimeResolver.Resolve(process);
                    if (!resolution.IsSuccess || resolution.SelectedRuntime is null)
                    {
                        throw new InvalidOperationException(
                            $"TIA runtime resolution failed for session {sessionId}. Code={resolution.DiagnosticCode} Message={resolution.DiagnosticMessage}");
                    }

                    var assembly = Assembly.LoadFrom(resolution.SelectedRuntime.OpennessAssemblyPath);
                    var provider = ResolveProvider(resolution.SelectedRuntime);
                    return provider.TryCheckOnlineState(assembly, sessionId, resolution.SelectedRuntime);
                }, token);
            }, cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var root = Unwrap(ex);
            Log.Warning(root, "CheckOnlineStateAsync failed for session {SessionId}", sessionId);
            return new OnlineStateResult(false, false, 0, "OnlineStateCheckFailed", root.Message);
        }
    }

    public async Task<bool> SaveProjectAsync(string sessionId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            return await _tiaRetryPolicy.ExecuteAsync(async token =>
            {
                token.ThrowIfCancellationRequested();
                return await Task.Run(() =>
                {
                    var process = FindProcessById(sessionId)
                        ?? throw new InvalidOperationException($"TIA process for session {sessionId} was not found.");

                    var resolution = _runtimeResolver.Resolve(process);
                    if (!resolution.IsSuccess || resolution.SelectedRuntime is null)
                    {
                        throw new InvalidOperationException(
                            $"TIA runtime resolution failed for session {sessionId}. Code={resolution.DiagnosticCode} Message={resolution.DiagnosticMessage}");
                    }

                    var assembly = Assembly.LoadFrom(resolution.SelectedRuntime.OpennessAssemblyPath);
                    var provider = ResolveProvider(resolution.SelectedRuntime);
                    var saved = provider.TrySaveProject(assembly, sessionId, resolution.SelectedRuntime);
                    if (!saved)
                    {
                        throw new InvalidOperationException($"TIA save operation returned false for session {sessionId}.");
                    }

                    return true;
                }, token);
            }, cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (IsSaveForbiddenInOnlineMode(ex))
            {
                Log.Warning(ex,
                    "SaveProjectAsync blocked by TIA online mode for session {SessionId}. Save is not allowed while project is in online mode.",
                    sessionId);
                return false;
            }

            Log.Warning(ex, "SaveProjectAsync failed for session {SessionId}", sessionId);
            return false;
        }
    }

    public async Task<PlcOnlineOfflineComparisonResult> CompareOnlineOfflineAsync(string sessionId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            return await _tiaRetryPolicy.ExecuteAsync(async token =>
            {
                token.ThrowIfCancellationRequested();
                return await Task.Run(() =>
                {
                    var process = FindProcessById(sessionId)
                        ?? throw new InvalidOperationException($"TIA process for session {sessionId} was not found.");

                    var resolution = _runtimeResolver.Resolve(process);
                    if (!resolution.IsSuccess || resolution.SelectedRuntime is null)
                    {
                        throw new InvalidOperationException(
                            $"TIA runtime resolution failed for session {sessionId}. Code={resolution.DiagnosticCode} Message={resolution.DiagnosticMessage}");
                    }

                    var assembly = Assembly.LoadFrom(resolution.SelectedRuntime.OpennessAssemblyPath);
                    var provider = ResolveProvider(resolution.SelectedRuntime);
                    return provider.TryCompareOnlineOffline(assembly, sessionId, resolution.SelectedRuntime);
                }, token);
            }, cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var root = Unwrap(ex);
            Log.Warning(root, "CompareOnlineOfflineAsync failed for session {SessionId}", sessionId);
            return new PlcOnlineOfflineComparisonResult(false, false, "PlcCompareGatewayFailed", root.Message);
        }
    }

    public async Task<GoOfflineResult> GoOfflineAsync(string sessionId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            return await _tiaRetryPolicy.ExecuteAsync(async token =>
            {
                token.ThrowIfCancellationRequested();
                return await Task.Run(() =>
                {
                    var process = FindProcessById(sessionId)
                        ?? throw new InvalidOperationException($"TIA process for session {sessionId} was not found.");

                    var resolution = _runtimeResolver.Resolve(process);
                    if (!resolution.IsSuccess || resolution.SelectedRuntime is null)
                    {
                        throw new InvalidOperationException(
                            $"TIA runtime resolution failed for session {sessionId}. Code={resolution.DiagnosticCode} Message={resolution.DiagnosticMessage}");
                    }

                    var assembly = Assembly.LoadFrom(resolution.SelectedRuntime.OpennessAssemblyPath);
                    var provider = ResolveProvider(resolution.SelectedRuntime);
                    return provider.TryGoOffline(assembly, sessionId, resolution.SelectedRuntime);
                }, token);
            }, cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var root = Unwrap(ex);
            Log.Warning(root, "GoOfflineAsync failed for session {SessionId}", sessionId);
            return new GoOfflineResult(false, 0, 0, "GoOfflineGatewayFailed", root.Message);
        }
    }

    public async Task<bool> ArchiveProjectAsync(string sessionId, string destinationArchivePath, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            return await _tiaRetryPolicy.ExecuteAsync(async token =>
            {
                token.ThrowIfCancellationRequested();
                return await Task.Run(() =>
                {
                    var process = FindProcessById(sessionId)
                        ?? throw new InvalidOperationException($"TIA process for session {sessionId} was not found.");

                    var resolution = _runtimeResolver.Resolve(process);
                    if (!resolution.IsSuccess || resolution.SelectedRuntime is null)
                    {
                        throw new InvalidOperationException(
                            $"TIA runtime resolution failed for session {sessionId}. Code={resolution.DiagnosticCode} Message={resolution.DiagnosticMessage}");
                    }

                    var assembly = Assembly.LoadFrom(resolution.SelectedRuntime.OpennessAssemblyPath);
                    var provider = ResolveProvider(resolution.SelectedRuntime);
                    var archived = provider.TryArchiveProject(assembly, sessionId, destinationArchivePath, resolution.SelectedRuntime);
                    if (!archived)
                    {
                        throw new InvalidOperationException(
                            $"TIA archive operation returned false for session {sessionId}. Destination={destinationArchivePath}");
                    }

                    return true;
                }, token);
            }, cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ArchiveProjectAsync failed for session {SessionId} and destination {DestinationArchivePath}", sessionId, destinationArchivePath);
            return false;
        }
    }

    private IOpennessVersionProvider ResolveProvider(TiaPortalRuntimeInfo runtime)
    {
        var provider = _providers.FirstOrDefault(candidate => candidate.CanHandle(runtime));
        if (provider is not null)
        {
            return provider;
        }

        return _providers.Last();
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

    private static Process? FindProcessById(string sessionId)
    {
        if (!int.TryParse(sessionId, out var processId))
        {
            return null;
        }

        try
        {
            return Process.GetProcessById(processId);
        }
        catch
        {
            return null;
        }
    }

    private static TiaProjectContext AttachSelectionDiagnostics(TiaProjectContext context, TiaPortalRuntimeResolution resolution, IOpennessVersionProvider provider)
    {
        return new TiaProjectContext(
            context.IsTiaRunning,
            context.OpenProjectPath,
            context.ProjectName,
            context.SessionId,
            context.HasUnsavedChanges,
            context.UnsavedStateDetectedReliably,
            context.DiagnosticCode,
            context.DiagnosticMessage,
            context.TiaVersion,
            context.OpennessAssemblyPath,
            provider.GetType().Name,
            resolution.SelectionReason ?? $"Provider {provider.GetType().Name} selected for runtime {resolution.SelectedRuntime?.Version}.",
            resolution.DetectedProcessVersion);
    }

    private static TiaProjectContext BuildContextFromFailure(string sessionId, TiaPortalRuntimeInfo runtime, Exception ex, IOpennessVersionProvider provider, string? selectionReason, string? detectedProcessVersion)
    {
        var root = Unwrap(ex);

        if (IsRuntimeIncompatible(root))
        {
            return new TiaProjectContext(true, null, null, sessionId, null, false, OpennessRuntimeIncompatibleCode, "Siemens Openness runtime is not compatible with the current launcher runtime. Run AutomationLauncher on .NET Framework 4.8.", runtime.Version, runtime.OpennessAssemblyPath, provider.GetType().Name, selectionReason, detectedProcessVersion);
        }

        if (root is FileNotFoundException or FileLoadException)
        {
            return new TiaProjectContext(true, null, null, sessionId, null, false, OpennessAssemblyLoadFailedCode, $"Failed to load Siemens Openness dependency: {root.Message}", runtime.Version, runtime.OpennessAssemblyPath, provider.GetType().Name, selectionReason, detectedProcessVersion);
        }

        return new TiaProjectContext(true, null, null, sessionId, null, false, GetProcessesFailedCode, $"Unable to query Siemens Openness: {root.Message}", runtime.Version, runtime.OpennessAssemblyPath, provider.GetType().Name, selectionReason, detectedProcessVersion);
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

    private static bool IsRetryableTiaException(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            return false;
        }

        if (IsSaveForbiddenInOnlineMode(ex))
        {
            return false;
        }

        var root = Unwrap(ex);
        if (root is COMException or ObjectDisposedException)
        {
            return true;
        }

        if (IsSiemensEngineeringException(root))
        {
            return true;
        }

        // Runtime resolution/process visibility can temporarily fail in unstable environments.
        return root is InvalidOperationException;
    }

    private static bool IsSiemensEngineeringException(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            var fullName = current.GetType().FullName;
            if (string.Equals(fullName, "Siemens.Engineering.EngineeringException", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSaveForbiddenInOnlineMode(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current.Message.IndexOf("not permitted in online mode", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}