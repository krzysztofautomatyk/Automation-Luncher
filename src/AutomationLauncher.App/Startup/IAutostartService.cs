namespace AutomationLauncher.App;

public interface IAutostartService
{
    string GetStartupFolderPath();

    bool IsEnabled();

    void SetEnabled(bool enabled);
}