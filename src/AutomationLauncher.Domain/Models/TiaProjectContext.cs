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
        string? diagnosticMessage = null,
        string? tiaVersion = null,
        string? opennessAssemblyPath = null,
        string? providerName = null,
        string? runtimeSelectionReason = null,
        string? detectedProcessVersion = null)
    {
        IsTiaRunning = isTiaRunning;
        OpenProjectPath = openProjectPath;
        ProjectName = projectName;
        SessionId = sessionId;
        HasUnsavedChanges = hasUnsavedChanges;
        UnsavedStateDetectedReliably = unsavedStateDetectedReliably;
        DiagnosticCode = diagnosticCode;
        DiagnosticMessage = diagnosticMessage;
        TiaVersion = tiaVersion;
        OpennessAssemblyPath = opennessAssemblyPath;
        ProviderName = providerName;
        RuntimeSelectionReason = runtimeSelectionReason;
        DetectedProcessVersion = detectedProcessVersion;
    }

    public bool IsTiaRunning { get; }

    public string? OpenProjectPath { get; }

    public string? ProjectName { get; }

    public string? SessionId { get; }

    public bool? HasUnsavedChanges { get; }

    public bool UnsavedStateDetectedReliably { get; }

    public string? DiagnosticCode { get; }

    public string? DiagnosticMessage { get; }

    public string? TiaVersion { get; }

    public string? OpennessAssemblyPath { get; }

    public string? ProviderName { get; }

    public string? RuntimeSelectionReason { get; }

    public string? DetectedProcessVersion { get; }
}
