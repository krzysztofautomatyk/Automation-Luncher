namespace AutomationLauncher.App;

public interface IProtectedApplicationSettingsStore
{
    string SettingsFilePath { get; }

    string CachedSettingsFilePath { get; }

    bool HasProtectedSettings();

    bool TryLoadCachedSettings(out AutomationLauncherSettings? settings, out string errorMessage);

    bool ValidatePasswordRequirements(string password, out string validationMessage);

    void Create(AutomationLauncherSettings settings, string password);

    bool TryLoad(string password, out AutomationLauncherSettings? settings, out string errorMessage);

    void Save(AutomationLauncherSettings settings, string password);
}