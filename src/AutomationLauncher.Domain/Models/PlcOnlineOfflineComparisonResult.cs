namespace AutomationLauncher.Domain.Models;

public sealed class PlcOnlineOfflineComparisonResult
{
    public PlcOnlineOfflineComparisonResult(bool verified, bool isEqual, string? diagnosticCode = null, string? diagnosticMessage = null)
    {
        Verified = verified;
        IsEqual = isEqual;
        DiagnosticCode = diagnosticCode;
        DiagnosticMessage = diagnosticMessage;
    }

    public bool Verified { get; }

    public bool IsEqual { get; }

    public string? DiagnosticCode { get; }

    public string? DiagnosticMessage { get; }
}
