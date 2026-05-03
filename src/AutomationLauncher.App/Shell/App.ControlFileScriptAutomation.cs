using System.Linq;
using AutomationLauncher.Domain.Models;
using Serilog;

namespace AutomationLauncher.App;

public partial class App : System.Windows.Application
{
    private async Task<ControlFileSequenceExecutionResult> ExecuteControlFileSequenceAsync(
        AutomationLauncherSettings settings,
        string controlFileType,
        bool isPreExecution,
        CancellationToken cancellationToken = default)
    {
        var binding = settings.ControlFiles.Bindings
            .FirstOrDefault(candidate => string.Equals(candidate.ControlFileType, controlFileType, StringComparison.OrdinalIgnoreCase));
        var steps = isPreExecution ? binding?.PreExecutionSteps : binding?.PostExecutionSteps;
        if (steps is null || steps.Count == 0)
        {
            return ControlFileSequenceExecutionResult.Continue("No configured script steps.");
        }

        foreach (var step in steps)
        {
            var script = settings.Project.PowerShellScripts.FirstOrDefault(candidate => string.Equals(candidate.Id, step.ScriptId, StringComparison.OrdinalIgnoreCase));
            var stepDescription = $"{controlFileType} {(isPreExecution ? "pre" : "post")} step";

            if (script is null)
            {
                var missingAction = step.OnFailure;
                var missingMessage = $"Configured script with id '{step.ScriptId}' was not found.";
                Log.Logger.Warning("{StepDescription}: {Message}", stepDescription, missingMessage);
                if (missingAction == ControlFileScriptOutcomeAction.RunNextScript)
                {
                    continue;
                }

                return missingAction == ControlFileScriptOutcomeAction.AbortControlFlow
                    ? ControlFileSequenceExecutionResult.Abort(missingMessage)
                    : ControlFileSequenceExecutionResult.Continue(missingMessage);
            }

            var scriptLabel = string.IsNullOrWhiteSpace(script.Name) ? script.Id : script.Name;
            var executionContext = BuildExecutionContext(script, step, controlFileType, isPreExecution ? "pre" : "post");

            var result = await _controlFileScriptRunner.RunAsync(script.ScriptBody, script.TimeoutSeconds, executionContext, cancellationToken);
            var action = result.IsSuccess ? step.OnSuccess : step.OnFailure;

            Log.Logger.Information(
                "Control-file script executed. Type={ControlFileType} Phase={Phase} Script={ScriptName} Success={IsSuccess} ExitCode={ExitCode} Action={Action}",
                controlFileType,
                isPreExecution ? "Pre" : "Post",
                scriptLabel,
                result.IsSuccess,
                result.ExitCode,
                action);

            switch (action)
            {
                case ControlFileScriptOutcomeAction.RunNextScript:
                    continue;
                case ControlFileScriptOutcomeAction.AbortControlFlow:
                    return ControlFileSequenceExecutionResult.Abort($"Script '{scriptLabel}' requested abort. {result.StatusMessage}");
                default:
                    return ControlFileSequenceExecutionResult.Continue($"Script '{scriptLabel}' finished and requested normal continuation.");
            }
        }

        return ControlFileSequenceExecutionResult.Continue("All configured script steps finished.");
    }

    private async Task<bool> TryRunControlFilePhaseAsync(
        AutomationLauncherSettings settings,
        string controlFileType,
        bool isPreExecution,
        string abortMessage)
    {
        var result = await ExecuteControlFileSequenceAsync(settings, controlFileType, isPreExecution);
        if (result.ShouldContinueControlFlow)
        {
            return true;
        }

        Log.Logger.Warning("Control-file sequence aborted the control flow. Type={ControlFileType} Phase={Phase} Details={Details}",
            controlFileType,
            isPreExecution ? "Pre" : "Post",
            result.Message);
        _notifyIcon?.ShowBalloonTip(2500, "Automation Launcher", abortMessage, System.Windows.Forms.ToolTipIcon.Info);
        return false;
    }

    internal PowerShellScriptExecutionContext BuildExecutionContext(
        ProjectScriptEntry script,
        ControlFileScriptSequenceStep? step,
        string controlFileType,
        string executionPhase)
    {
        var parameterMap = script.Parameters.ToDictionary(
            parameter => parameter.Name,
            parameter => parameter.DefaultValue ?? string.Empty,
            StringComparer.OrdinalIgnoreCase);

        if (step is not null)
        {
            foreach (var overrideEntry in step.ParameterOverrides.Where(overrideEntry => !string.IsNullOrWhiteSpace(overrideEntry.Name)))
            {
                parameterMap[overrideEntry.Name] = overrideEntry.Value ?? string.Empty;
            }
        }

        return new PowerShellScriptExecutionContext
        {
            ScriptName = string.IsNullOrWhiteSpace(script.Name) ? script.Id : script.Name,
            ControlFileType = controlFileType,
            ExecutionPhase = executionPhase,
            MachineName = Environment.MachineName,
            HostState = _hostControlState.ToString(),
            AppBaseDirectory = AppContext.BaseDirectory,
            ControlFilesDirectory = GetControlFilesRootDirectory(),
            StartedAtUtc = DateTimeOffset.UtcNow,
            Parameters = parameterMap
        };
    }
}

internal sealed class ControlFileSequenceExecutionResult
{
    private ControlFileSequenceExecutionResult(bool shouldContinueControlFlow, string message)
    {
        ShouldContinueControlFlow = shouldContinueControlFlow;
        Message = message;
    }

    public bool ShouldContinueControlFlow { get; }

    public string Message { get; }

    public static ControlFileSequenceExecutionResult Continue(string message) => new(true, message);

    public static ControlFileSequenceExecutionResult Abort(string message) => new(false, message);
}