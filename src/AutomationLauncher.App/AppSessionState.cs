namespace AutomationLauncher.App;

public sealed class AppSessionState
{
    public string? SettingsPassword { get; private set; }

    public bool HasUnlockedSettings => !string.IsNullOrWhiteSpace(SettingsPassword);

    public void Unlock(string password)
    {
        SettingsPassword = password;
    }

    public void Lock()
    {
        SettingsPassword = null;
    }
}