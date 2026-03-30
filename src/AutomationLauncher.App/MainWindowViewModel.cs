using System.Collections.ObjectModel;
using AutomationLauncher.Application.UseCases;
using AutomationLauncher.Domain.Contracts;
using AutomationLauncher.Domain.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AutomationLauncher.App;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ArchiveProjectUseCase _archiveProjectUseCase;
    private readonly ITiaPortalGateway _tiaPortalGateway;
    private readonly ITiaPortalRuntimeCatalog _runtimeCatalog;
    private readonly IRuntimeSelectionSettingsStore _runtimeSelectionSettingsStore;
    private readonly AutomationLauncherSettings _settings;
    private bool _isInitializing = true;

    [ObservableProperty]
    private string expectedProjectPath = string.Empty;

    [ObservableProperty]
    private string archiveOutputDirectory = string.Empty;

    [ObservableProperty]
    private bool tryDetectUnsavedChanges;

    [ObservableProperty]
    private bool forceSaveWhenDetectionUnavailable;

    [ObservableProperty]
    private string tiaRuntimeSelectionMode = TiaPortalVersionSelectionMode.Auto.ToString();

    [ObservableProperty]
    private TiaPortalRuntimeInfo? selectedTiaRuntime;

    [ObservableProperty]
    private string runtimeCatalogStatus = "Scanning installed TIA Portal runtimes...";

    [ObservableProperty]
    private string runtimeDecisionMessage = "Runtime provider diagnostics will appear here after connection or synchronization.";

    [ObservableProperty]
    private string detectedProcessVersion = "n/a";

    [ObservableProperty]
    private string selectedRuntimeVersion = "n/a";

    [ObservableProperty]
    private string selectedProviderName = "n/a";

    [ObservableProperty]
    private string selectedAssemblyPath = "n/a";

    [ObservableProperty]
    private string runtimeSelectionReason = "n/a";

    [ObservableProperty]
    private string statusMessage = "Ready";

    [ObservableProperty]
    private bool isBusy;

    public ObservableCollection<string> History { get; } = new();

    public ObservableCollection<string> TiaRuntimeSelectionModes { get; } = new()
    {
        TiaPortalVersionSelectionMode.Auto.ToString(),
        TiaPortalVersionSelectionMode.Manual.ToString()
    };

    public ObservableCollection<TiaPortalRuntimeInfo> AvailableTiaRuntimes { get; } = new();

    public MainWindowViewModel(
        ArchiveProjectUseCase archiveProjectUseCase,
        ITiaPortalGateway tiaPortalGateway,
        ITiaPortalRuntimeCatalog runtimeCatalog,
        IRuntimeSelectionSettingsStore runtimeSelectionSettingsStore,
        AutomationLauncherSettings settings)
    {
        _archiveProjectUseCase = archiveProjectUseCase;
        _tiaPortalGateway = tiaPortalGateway;
        _runtimeCatalog = runtimeCatalog;
        _runtimeSelectionSettingsStore = runtimeSelectionSettingsStore;
        _settings = settings;

        ExpectedProjectPath = _settings.Archive.ExpectedProjectPath;
        ArchiveOutputDirectory = _settings.Archive.ArchiveOutputDirectory;
        TryDetectUnsavedChanges = _settings.Archive.TryDetectUnsavedChanges;
        ForceSaveWhenDetectionUnavailable = _settings.Archive.ForceSaveWhenDetectionUnavailable;
        TiaRuntimeSelectionMode = _settings.Archive.TiaVersionSelectionMode.ToString();

        LoadRuntimeCatalog();
        _isInitializing = false;
    }

    [RelayCommand(CanExecute = nameof(CanArchive))]
    private async Task SyncProjectFromTiaAsync()
    {
        IsBusy = true;
        StatusMessage = "Reading project from TIA Portal...";
        ArchiveCommand.NotifyCanExecuteChanged();
        SyncProjectFromTiaCommand.NotifyCanExecuteChanged();
        CheckTiaConnectionCommand.NotifyCanExecuteChanged();

        try
        {
            var context = await _tiaPortalGateway.GetCurrentContextAsync(CancellationToken.None);
            if (!context.IsTiaRunning)
            {
                StatusMessage = context.DiagnosticMessage ?? "TIA Portal is not running.";
                ApplyRuntimeDiagnostics(context);
                AddHistory("INFO", context.DiagnosticCode, StatusMessage);
                return;
            }

            if (string.IsNullOrWhiteSpace(context.OpenProjectPath))
            {
                StatusMessage = context.DiagnosticMessage ?? "TIA Portal is running, but no open project was detected through Openness.";
                ApplyRuntimeDiagnostics(context);
                AddHistory("WARN", context.DiagnosticCode, StatusMessage);
                return;
            }

            ExpectedProjectPath = context.OpenProjectPath!;
            StatusMessage = $"Project path synchronized from TIA: {ExpectedProjectPath} | runtime {BuildRuntimeLabel(context)}";
            ApplyRuntimeDiagnostics(context);
            AddHistory("INFO", "SyncOk", $"Synchronized project path from TIA: {ExpectedProjectPath} | runtime {BuildRuntimeLabel(context)}");
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "SyncProjectFromTia failed");
            StatusMessage = ex.Message;
            AddHistory("ERROR", "SyncFailed", ex.Message);
        }
        finally
        {
            IsBusy = false;
            ArchiveCommand.NotifyCanExecuteChanged();
            SyncProjectFromTiaCommand.NotifyCanExecuteChanged();
            CheckTiaConnectionCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanArchive))]
    private async Task CheckTiaConnectionAsync()
    {
        IsBusy = true;
        StatusMessage = "Checking TIA Portal connection...";
        CheckTiaConnectionCommand.NotifyCanExecuteChanged();
        ArchiveCommand.NotifyCanExecuteChanged();
        SyncProjectFromTiaCommand.NotifyCanExecuteChanged();

        try
        {
            var context = await _tiaPortalGateway.GetCurrentContextAsync(CancellationToken.None);

            if (!context.IsTiaRunning)
            {
                StatusMessage = context.DiagnosticMessage ?? "TIA Portal is NOT running. Start TIA Portal to enable communication.";
                ApplyRuntimeDiagnostics(context);
                AddHistory("ERROR", context.DiagnosticCode, StatusMessage);
                return;
            }

            if (string.IsNullOrWhiteSpace(context.OpenProjectPath))
            {
                StatusMessage = context.DiagnosticMessage ?? $"TIA Portal is running (PID: {context.SessionId}) but Openness could not connect or no project is open.";
                ApplyRuntimeDiagnostics(context);
                AddHistory("WARN", context.DiagnosticCode, $"{StatusMessage} | PID: {context.SessionId}");
                return;
            }

            var unsavedInfo = context.UnsavedStateDetectedReliably
                ? (context.HasUnsavedChanges == true ? "unsaved changes detected" : "no unsaved changes")
                : "unsaved state unknown";

            StatusMessage = $"Connected to TIA Portal. Project: {context.ProjectName ?? context.OpenProjectPath} | {unsavedInfo} | runtime {BuildRuntimeLabel(context)}";
            ApplyRuntimeDiagnostics(context);
            AddHistory("OK", "Connected", $"Project: {context.ProjectName ?? context.OpenProjectPath} | PID: {context.SessionId} | {unsavedInfo} | runtime {BuildRuntimeLabel(context)}");
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "CheckTiaConnection failed");
            StatusMessage = $"Connection check failed: {ex.Message}";
            AddHistory("ERROR", "ConnectionCheckFailed", $"Connection check failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            CheckTiaConnectionCommand.NotifyCanExecuteChanged();
            ArchiveCommand.NotifyCanExecuteChanged();
            SyncProjectFromTiaCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanArchive))]
    private async Task ArchiveAsync()
    {
        IsBusy = true;
        StatusMessage = "Running archive workflow...";
        ArchiveCommand.NotifyCanExecuteChanged();

        try
        {
            var options = new ArchiveOptions
            {
                ExpectedProjectPath = ExpectedProjectPath,
                ArchiveOutputDirectory = ArchiveOutputDirectory,
                TryDetectUnsavedChanges = TryDetectUnsavedChanges,
                ForceSaveWhenDetectionUnavailable = ForceSaveWhenDetectionUnavailable,
                SaveTimeoutSeconds = _settings.Archive.SaveTimeoutSeconds,
                ArchiveTimeoutSeconds = _settings.Archive.ArchiveTimeoutSeconds,
                RetryCount = _settings.Archive.RetryCount,
                RetryDelayMilliseconds = _settings.Archive.RetryDelayMilliseconds,
                TiaVersionSelectionMode = _settings.Archive.TiaVersionSelectionMode,
                PreferredTiaVersion = _settings.Archive.PreferredTiaVersion,
                OpennessAssemblyPath = _settings.Archive.OpennessAssemblyPath,
                KnownVersions = _settings.Archive.KnownVersions
            };

            var result = await _archiveProjectUseCase.ExecuteAsync(options, CancellationToken.None);
            var summary = $"{result.Outcome} | {result.Message}";

            if (result.RuntimeContext is not null)
            {
                ApplyRuntimeDiagnostics(result.RuntimeContext);
            }

            if (!string.IsNullOrWhiteSpace(result.ArchivePath))
            {
                summary += $" | {result.ArchivePath}";
            }

            AddHistory(result.Outcome == ArchiveOutcome.Success ? "OK" : "WARN", result.Outcome.ToString(), summary);
            StatusMessage = result.Message;
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Archive command failed");
            StatusMessage = ex.Message;
            AddHistory("ERROR", "ArchiveFailed", ex.Message);
        }
        finally
        {
            IsBusy = false;
            ArchiveCommand.NotifyCanExecuteChanged();
            SyncProjectFromTiaCommand.NotifyCanExecuteChanged();
            CheckTiaConnectionCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanArchive()
    {
        return !IsBusy;
    }

    partial void OnTiaRuntimeSelectionModeChanged(string value)
    {
        _settings.Archive.TiaVersionSelectionMode = string.Equals(value, TiaPortalVersionSelectionMode.Manual.ToString(), StringComparison.OrdinalIgnoreCase)
            ? TiaPortalVersionSelectionMode.Manual
            : TiaPortalVersionSelectionMode.Auto;

        PersistRuntimeSelection();
        UpdateRuntimeCatalogStatus();
    }

    partial void OnSelectedTiaRuntimeChanged(TiaPortalRuntimeInfo? value)
    {
        _settings.Archive.PreferredTiaVersion = value?.Version;
        PersistRuntimeSelection();
        UpdateRuntimeCatalogStatus();
    }

    private void LoadRuntimeCatalog()
    {
        AvailableTiaRuntimes.Clear();

        var runtimes = _runtimeCatalog.GetAvailableRuntimes();
        foreach (var runtime in runtimes)
        {
            AvailableTiaRuntimes.Add(runtime);
        }

        if (!string.IsNullOrWhiteSpace(_settings.Archive.PreferredTiaVersion))
        {
            SelectedTiaRuntime = AvailableTiaRuntimes.FirstOrDefault(runtime => string.Equals(runtime.Version, _settings.Archive.PreferredTiaVersion, StringComparison.OrdinalIgnoreCase));
        }

        SelectedTiaRuntime ??= AvailableTiaRuntimes.FirstOrDefault();
        UpdateRuntimeCatalogStatus();
    }

    private void UpdateRuntimeCatalogStatus()
    {
        if (AvailableTiaRuntimes.Count == 0)
        {
            RuntimeCatalogStatus = "No TIA Portal PublicAPI runtimes were detected. Add version overrides or install PublicAPI for the target version.";
            return;
        }

        var selectedRuntimeLabel = SelectedTiaRuntime is null
            ? "none"
            : $"{SelectedTiaRuntime.DisplayName} ({SelectedTiaRuntime.Source})";

        RuntimeCatalogStatus = $"Detected {AvailableTiaRuntimes.Count} runtime(s). Selection mode: {TiaRuntimeSelectionMode}. Selected runtime: {selectedRuntimeLabel}.";
    }

    private void PersistRuntimeSelection()
    {
        if (_isInitializing)
        {
            return;
        }

        try
        {
            _runtimeSelectionSettingsStore.SaveRuntimeSelection(_settings.Archive);
        }
        catch (Exception ex)
        {
            AddHistory("WARN", "RuntimeSelectionPersistFailed", ex.Message);
        }
    }

    private void ApplyRuntimeDiagnostics(TiaProjectContext context)
    {
        DetectedProcessVersion = context.DetectedProcessVersion ?? "n/a";
        SelectedRuntimeVersion = context.TiaVersion ?? SelectedTiaRuntime?.Version ?? "n/a";
        SelectedProviderName = context.ProviderName ?? "n/a";
        SelectedAssemblyPath = context.OpennessAssemblyPath ?? SelectedTiaRuntime?.OpennessAssemblyPath ?? "n/a";
        RuntimeSelectionReason = context.RuntimeSelectionReason ?? "No runtime selection reason available.";
        RuntimeDecisionMessage = BuildDecisionMessage(context);
    }

    private string BuildRuntimeLabel(TiaProjectContext context)
    {
        var runtimeVersion = context.TiaVersion ?? SelectedTiaRuntime?.Version ?? "auto";
        var providerLabel = string.IsNullOrWhiteSpace(context.ProviderName) ? string.Empty : $" | provider {context.ProviderName}";
        return string.IsNullOrWhiteSpace(context.OpennessAssemblyPath)
            ? runtimeVersion + providerLabel
            : $"{runtimeVersion} | {context.OpennessAssemblyPath}{providerLabel}";
    }

    private static string BuildDecisionMessage(TiaProjectContext context)
    {
        var provider = string.IsNullOrWhiteSpace(context.ProviderName) ? "not selected" : context.ProviderName;
        var reason = string.IsNullOrWhiteSpace(context.RuntimeSelectionReason) ? "No runtime selection reason available." : context.RuntimeSelectionReason;
        return $"Provider: {provider}. Reason: {reason}";
    }

    private void AddHistory(string level, string? code, string message)
    {
        var normalizedCode = string.IsNullOrWhiteSpace(code) ? "General" : code;
        History.Insert(0, $"{DateTime.Now:HH:mm:ss} | {level} | {normalizedCode} | {message}");
    }
}
