namespace AutomationLauncher.App.Services;

/// <summary>
/// Executes configured PowerShell scripts for a given control-file event phase.
/// </summary>
public interface IControlFileScriptOrchestrator
{
    /// <summary>
    /// Runs the configured pre- or post-execution scripts for the given control file type.
    /// </summary>
    Task<ControlFileScriptResult> ExecuteAsync(
        string controlFileType,
        bool isPreExecution,
        string hostControlState,
        string controlFilesDirectory,
        CancellationToken cancellationToken = default);
}

public sealed class ControlFileScriptResult
{
    private ControlFileScriptResult(bool shouldContinue, string message)
    {
        ShouldContinueControlFlow = shouldContinue;
        Message = message;
    }

    public bool ShouldContinueControlFlow { get; }
    public string Message { get; }

    public static ControlFileScriptResult Continue(string message) => new(true, message);
    public static ControlFileScriptResult Abort(string message) => new(false, message);
}
