using System.Linq;
using AutomationLauncher.App;
using Serilog;

namespace AutomationLauncher.App.Services;

/// <summary>
/// Executes configured PowerShell scripts for control-file events.
/// This service is stateless and injectable — it replaces the private
/// ExecuteControlFileSequenceAsync logic previously embedded in App.
/// </summary>
public sealed class ControlFileScriptOrchestrator : IControlFileScriptOrchestrator
{
    private readonly AutomationLauncherSettings _settings;
    private readonly PowerShellScriptRunner _scriptRunner;

    public ControlFileScriptOrchestrator(AutomationLauncherSettings settings, PowerShellScriptRunner scriptRunner)
    {
        _settings = settings;
        _scriptRunner = scriptRunner;
    }

    public async Task<ControlFileScriptResult> ExecuteAsync(
        string controlFileType,
        bool isPreExecution,
        string hostControlState,
        string controlFilesDirectory,
        CancellationToken cancellationToken = default)
    {
        var binding = _settings.ControlFiles.Bindings
            .FirstOrDefault(b => string.Equals(b.ControlFileType, controlFileType, StringComparison.OrdinalIgnoreCase));

        var steps = isPreExecution ? binding?.PreExecutionSteps : binding?.PostExecutionSteps;
        if (steps is null || steps.Count == 0)
        {
            return ControlFileScriptResult.Continue("No configured script steps.");
        }

        var phase = isPreExecution ? "pre" : "post";

        foreach (var step in steps)
        {
            var script = _settings.Project.PowerShellScripts
                .FirstOrDefault(s => string.Equals(s.Id, step.ScriptId, StringComparison.OrdinalIgnoreCase));

            var stepDescription = $"{controlFileType} {phase} step";

            if (script is null)
            {
                var missingMessage = $"Configured script with id '{step.ScriptId}' was not found.";
                Log.Logger.Warning("{StepDescription}: {Message}", stepDescription, missingMessage);

                if (step.OnFailure == ControlFileScriptOutcomeAction.RunNextScript) continue;
                return step.OnFailure == ControlFileScriptOutcomeAction.AbortControlFlow
                    ? ControlFileScriptResult.Abort(missingMessage)
                    : ControlFileScriptResult.Continue(missingMessage);
            }

            var scriptLabel = ProjectScriptExecutionContextFactory.GetScriptLabel(script);
            var context = ProjectScriptExecutionContextFactory.CreateForControlFile(
                script,
                step,
                controlFileType,
                phase,
                hostControlState,
                controlFilesDirectory);
            var runResult = await _scriptRunner.RunAsync(script.ScriptBody, script.TimeoutSeconds, context, cancellationToken);
            var action = runResult.IsSuccess ? step.OnSuccess : step.OnFailure;

            Log.Logger.Information(
                "Control-file script executed. Type={ControlFileType} Phase={Phase} Script={Script} Success={Success} ExitCode={ExitCode} Action={Action}",
                controlFileType, phase, scriptLabel, runResult.IsSuccess, runResult.ExitCode, action);

            switch (action)
            {
                case ControlFileScriptOutcomeAction.RunNextScript:
                    continue;
                case ControlFileScriptOutcomeAction.AbortControlFlow:
                    return ControlFileScriptResult.Abort($"Script '{scriptLabel}' requested abort. {runResult.StatusMessage}");
                default:
                    return ControlFileScriptResult.Continue($"Script '{scriptLabel}' finished.");
            }
        }

        return ControlFileScriptResult.Continue("All configured script steps finished.");
    }
}
