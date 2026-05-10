using AutomationLauncher.Domain.Models;

namespace AutomationLauncher.App.Services;

public interface IProjectScriptWorkflowService
{
    string BuildManualPreview(ProjectScriptEntry script, HostControlState hostControlState, string controlFilesDirectory);

    ControlFileStepPreviewResult BuildControlFileStepPreview(
        IEnumerable<ProjectScriptEntry> availableScripts,
        ControlFileScriptBinding? binding,
        ControlFileScriptSequenceStep? step,
        string phase,
        HostControlState hostControlState,
        string controlFilesDirectory);

    Task<ProjectScriptExecutionResult> RunManualScriptAsync(
        ProjectScriptEntry script,
        HostControlState hostControlState,
        string controlFilesDirectory,
        CancellationToken cancellationToken);
}

public sealed class ControlFileStepPreviewResult
{
    public ControlFileStepPreviewResult(string status, string preview)
    {
        Status = status;
        Preview = preview;
    }

    public string Status { get; }

    public string Preview { get; }
}

public sealed class ProjectScriptExecutionResult
{
    public ProjectScriptExecutionResult(
        string scriptLabel,
        bool isSuccess,
        bool isRunnerError,
        DateTimeOffset finishedAt,
        int? exitCode,
        string combinedOutput,
        string statusMessage)
    {
        ScriptLabel = scriptLabel;
        IsSuccess = isSuccess;
        IsRunnerError = isRunnerError;
        FinishedAt = finishedAt;
        ExitCode = exitCode;
        CombinedOutput = combinedOutput;
        StatusMessage = statusMessage;
    }

    public string ScriptLabel { get; }

    public bool IsSuccess { get; }

    public bool IsRunnerError { get; }

    public DateTimeOffset FinishedAt { get; }

    public int? ExitCode { get; }

    public string CombinedOutput { get; }

    public string StatusMessage { get; }
}
