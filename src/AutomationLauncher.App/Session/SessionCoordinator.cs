using System;
using System.Windows;
using AutomationLauncher.Domain.Models;

namespace AutomationLauncher.App;

public sealed class SessionCoordinator : ISessionCoordinator
{
    private readonly IProtectedApplicationSettingsStore _settingsStore;
    private readonly AutomationLauncherSettings _settings;
    private readonly AppSessionState _sessionState;
    private DateTime _lastActivityUtc = DateTime.UtcNow;

    public SessionCoordinator(
        IProtectedApplicationSettingsStore settingsStore,
        AutomationLauncherSettings settings,
        AppSessionState sessionState)
    {
        _settingsStore = settingsStore;
        _settings = settings;
        _sessionState = sessionState;
    }

    public event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged;

    public bool IsAuthenticated => _sessionState.HasUnlockedSettings;

    public TimeSpan InactivityTimeout { get; } = TimeSpan.FromMinutes(5);

    public bool EnsureAuthenticated(Window? owner = null)
    {
        if (IsAuthenticated)
        {
            RegisterActivity();
            return true;
        }

        if (!_settingsStore.HasProtectedSettings())
        {
            var setupWindow = new PasswordSetupWindow(_settingsStore);
            if (owner is not null && owner.IsLoaded && owner.IsVisible)
            {
                setupWindow.Owner = owner;
            }

            var setupResult = setupWindow.ShowDialog();
            if (setupResult != true || string.IsNullOrWhiteSpace(setupWindow.Password))
            {
                return false;
            }

            _settingsStore.Create(_settings, setupWindow.Password!);
            _sessionState.Unlock(setupWindow.Password!);
            RegisterActivity();
            SessionStateChanged?.Invoke(this, new SessionStateChangedEventArgs(true, "Settings unlocked.", false));
            return true;
        }

        string? lastErrorMessage = null;
        while (true)
        {
            var promptWindow = new PasswordPromptWindow();
            if (owner is not null && owner.IsLoaded && owner.IsVisible)
            {
                promptWindow.Owner = owner;
            }

            if (!string.IsNullOrWhiteSpace(lastErrorMessage))
            {
                promptWindow.ShowValidation(lastErrorMessage!);
            }

            var result = promptWindow.ShowDialog();
            if (result != true || string.IsNullOrWhiteSpace(promptWindow.Password))
            {
                return false;
            }

            if (!_settingsStore.TryLoad(promptWindow.Password!, out var loadedSettings, out var errorMessage))
            {
                lastErrorMessage = errorMessage;
                continue;
            }

            ApplyLoadedSettings(_settings, loadedSettings!);
            _sessionState.Unlock(promptWindow.Password!);
            RegisterActivity();
            SessionStateChanged?.Invoke(this, new SessionStateChangedEventArgs(true, "Settings unlocked.", false));
            return true;
        }
    }

    public void RegisterActivity()
    {
        if (!IsAuthenticated)
        {
            return;
        }

        _lastActivityUtc = DateTime.UtcNow;
    }

    public TimeSpan GetRemainingInactivity()
    {
        if (!IsAuthenticated)
        {
            return TimeSpan.Zero;
        }

        var elapsed = DateTime.UtcNow - _lastActivityUtc;
        var remaining = InactivityTimeout - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public bool HasTimedOut()
    {
        return IsAuthenticated && DateTime.UtcNow - _lastActivityUtc >= InactivityTimeout;
    }

    public void Logout(string reason, bool isAutomatic)
    {
        if (!IsAuthenticated)
        {
            return;
        }

        _sessionState.Lock();
        SessionStateChanged?.Invoke(this, new SessionStateChangedEventArgs(false, reason, isAutomatic));
    }

    private static void ApplyLoadedSettings(AutomationLauncherSettings target, AutomationLauncherSettings source)
    {
        target.Archive = source.Archive ?? new ArchiveOptions();
        target.Project = source.Project ?? new ProjectSettings();
        target.ControlFiles = source.ControlFiles ?? new ControlFilesSettings();
        target.Startup = source.Startup ?? new StartupSettings();
        target.Logging = source.Logging ?? new LoggingSettings();
        target.Ui = source.Ui ?? new UiSettings();
    }
}