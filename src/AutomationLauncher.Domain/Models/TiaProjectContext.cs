namespace AutomationLauncher.Domain.Models;

public sealed class TiaProjectContext
{
    public TiaProjectContext(
        bool isTiaRunning,
        string? openProjectPath,
        string? projectName,
        string? sessionId,
        bool? hasUnsavedChanges,
        bool unsavedStateDetectedReliably,
        string? diagnosticCode = null,
        string? diagnosticMessage = null)
    {
        IsTiaRunning = isTiaRunning;
        OpenProjectPath = openProjectPath;
        ProjectName = projectName;
        SessionId = sessionId;
        HasUnsavedChanges = hasUnsavedChanges;
        UnsavedStateDetectedReliably = unsavedStateDetectedReliably;
        DiagnosticCode = diagnosticCode;
        DiagnosticMessage = diagnosticMessage;
    }

    public bool IsTiaRunning { get; }

    public string? OpenProjectPath { get; }

    public string? ProjectName { get; }

    public string? SessionId { get; }

    public bool? HasUnsavedChanges { get; }

    public bool UnsavedStateDetectedReliably { get; }

    public string? DiagnosticCode { get; }

    public string? DiagnosticMessage { get; }
}
