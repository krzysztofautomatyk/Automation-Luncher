using AutomationLauncher.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace AutomationLauncher.App;

public partial class App : System.Windows.Application
{
    private async Task<bool> TryRunControlFilePhaseAsync(
        AutomationLauncherSettings settings,
        string controlFileType,
        bool isPreExecution,
        string abortMessage)
    {
        var orchestrator = _host?.Services.GetService<IControlFileScriptOrchestrator>();
        if (orchestrator is null)
            return true;

        var result = await orchestrator.ExecuteAsync(
            controlFileType,
            isPreExecution,
            _hostControlState.ToString(),
            GetControlFilesRootDirectory());

        if (result.ShouldContinueControlFlow)
            return true;

        Log.Logger.Warning(
            "Control-file sequence aborted the control flow. Type={ControlFileType} Phase={Phase} Details={Details}",
            controlFileType,
            isPreExecution ? "Pre" : "Post",
            result.Message);

        _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", abortMessage, System.Windows.Forms.ToolTipIcon.Info);
        return false;
    }
}