using System.Linq;
using AutomationLauncher.Domain.Models;

namespace AutomationLauncher.App.Services;

public sealed class ProjectScriptWorkflowService : IProjectScriptWorkflowService
{
    private readonly PowerShellScriptRunner _powerShellScriptRunner;

    public ProjectScriptWorkflowService(PowerShellScriptRunner powerShellScriptRunner)
    {
        _powerShellScriptRunner = powerShellScriptRunner;
    }

    public string BuildManualPreview(ProjectScriptEntry script, HostControlState hostControlState, string controlFilesDirectory)
    {
        var executionContext = ProjectScriptExecutionContextFactory.CreateManual(
            script,
            hostControlState.ToString(),
            controlFilesDirectory);

        return _powerShellScriptRunner.PreviewScript(script.ScriptBody, executionContext);
    }

    public ControlFileStepPreviewResult BuildControlFileStepPreview(
        IEnumerable<ProjectScriptEntry> availableScripts,
        ControlFileScriptBinding? binding,
        ControlFileScriptSequenceStep? step,
        string phase,
        HostControlState hostControlState,
        string controlFilesDirectory)
    {
        if (binding is null || step is null)
        {
            return new ControlFileStepPreviewResult(
                "Select a control-file step to edit parameter overrides and preview the final script.",
                string.Empty);
        }

        var script = availableScripts.FirstOrDefault(candidate => string.Equals(candidate.Id, step.ScriptId, StringComparison.OrdinalIgnoreCase));
        if (script is null)
        {
            return new ControlFileStepPreviewResult(
                "The selected step does not reference an existing script.",
                string.Empty);
        }

        var executionContext = ProjectScriptExecutionContextFactory.CreateForControlFile(
            script,
            step,
            binding.ControlFileType,
            phase,
            hostControlState.ToString(),
            controlFilesDirectory);

        return new ControlFileStepPreviewResult(
            $"Preview for {binding.DisplayName} ({phase}) using script '{ProjectScriptExecutionContextFactory.GetScriptLabel(script)}'.",
            _powerShellScriptRunner.PreviewScript(script.ScriptBody, executionContext));
    }

    public async Task<ProjectScriptExecutionResult> RunManualScriptAsync(
        ProjectScriptEntry script,
        HostControlState hostControlState,
        string controlFilesDirectory,
        CancellationToken cancellationToken)
    {
        var scriptLabel = ProjectScriptExecutionContextFactory.GetScriptLabel(script);

        try
        {
            var executionContext = ProjectScriptExecutionContextFactory.CreateManual(
                script,
                hostControlState.ToString(),
                controlFilesDirectory);

            var result = await _powerShellScriptRunner.RunAsync(
                script.ScriptBody,
                script.TimeoutSeconds,
                executionContext,
                cancellationToken);

            var finishedAt = DateTimeOffset.Now;
            var statusMessage = result.IsSuccess
                ? $"Success. Exit code {result.ExitCode}. Finished {finishedAt:yyyy-MM-dd HH:mm:ss}."
                : $"Failure. Exit code {result.ExitCode}. Finished {finishedAt:yyyy-MM-dd HH:mm:ss}. {result.StatusMessage}";

            return new ProjectScriptExecutionResult(
                scriptLabel,
                result.IsSuccess,
                isRunnerError: false,
                finishedAt,
                result.ExitCode,
                result.CombinedOutput,
                statusMessage);
        }
        catch (Exception ex)
        {
            var finishedAt = DateTimeOffset.Now;
            return new ProjectScriptExecutionResult(
                scriptLabel,
                isSuccess: false,
                isRunnerError: true,
                finishedAt,
                exitCode: null,
                combinedOutput: ex.ToString(),
                statusMessage: $"Failure. Runner error: {ex.Message}");
        }
    }
}
