namespace AutomationLauncher.App;

public partial class App : System.Windows.Application
{
    private static int GetReactionCountdownSeconds(
        ControlFileScriptBinding? binding,
        HostControlCommandAction action,
        int fallbackSeconds)
    {
        if (binding is null)
        {
            return fallbackSeconds;
        }

        if (binding.SplashCountdownSeconds >= 0)
        {
            return Math.Max(0, binding.SplashCountdownSeconds);
        }

        return ControlFileScriptBinding.BuildDefaultSplashCountdownSeconds(action);
    }

    private static void ApplyReactionSplashSettings(
        StartupSequenceSplashWindow splashWindow,
        AutomationLauncherSettings settings,
        ControlFileScriptBinding? binding,
        HostControlCommandAction action,
        string fallbackTitle)
    {
        var splashTitle = binding is null
            ? fallbackTitle
            : binding.EffectiveSplashTitle;
        var splashBackgroundImagePath = binding is not null
            && !string.IsNullOrWhiteSpace(binding.SplashBackgroundImagePath)
                ? binding.SplashBackgroundImagePath
                : settings.Startup.SplashBackgroundImagePath;

        splashWindow.SetApplicationTitle(string.IsNullOrWhiteSpace(splashTitle)
            ? ControlFileScriptBinding.BuildDefaultSplashTitle(action)
            : splashTitle);
        splashWindow.SetBackgroundImage(splashBackgroundImagePath);
    }
}
