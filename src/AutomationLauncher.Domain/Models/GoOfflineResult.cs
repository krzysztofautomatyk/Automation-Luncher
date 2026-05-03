namespace AutomationLauncher.Domain.Models;

public sealed class GoOfflineResult
{
    public GoOfflineResult(bool success, int devicesProcessed, int devicesSetOffline, string? diagnosticCode = null, string? diagnosticMessage = null)
    {
        Success = success;
        DevicesProcessed = devicesProcessed;
        DevicesSetOffline = devicesSetOffline;
        DiagnosticCode = diagnosticCode;
        DiagnosticMessage = diagnosticMessage;
    }

    public bool Success { get; }

    public int DevicesProcessed { get; }

    public int DevicesSetOffline { get; }

    public string? DiagnosticCode { get; }

    public string? DiagnosticMessage { get; }
}
