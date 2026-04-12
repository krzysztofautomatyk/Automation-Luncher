using System.Diagnostics;
using System.IO;

namespace AutomationLauncher.App;

public sealed class StartupScriptAutostartService : IAutostartService
{
    private const string ScriptFileName = "AutomationLauncher.cmd";

    public string GetStartupFolderPath()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.Startup);
    }

    public bool IsEnabled()
    {
        return File.Exists(GetScriptPath());
    }

    public void SetEnabled(bool enabled)
    {
        var scriptPath = GetScriptPath();
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath) ?? GetStartupFolderPath());

        if (!enabled)
        {
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }

            return;
        }

        var executablePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Unable to resolve application executable path for startup registration.");
        }

        var scriptContent = string.Join(
            Environment.NewLine,
            "@echo off",
            $"start \"AutomationLauncher\" \"{executablePath}\" {AppLaunchArguments.StartupLaunch}");

        File.WriteAllText(scriptPath, scriptContent);
    }

    private string GetScriptPath()
    {
        return Path.Combine(GetStartupFolderPath(), ScriptFileName);
    }
}