namespace AutomationLauncher.Domain.Models;

public sealed class OnlineStateResult
{
    public OnlineStateResult(bool checked_, bool hasOnlineDevices, int onlineDeviceCount, string? diagnosticCode = null, string? diagnosticMessage = null)
    {
        Checked = checked_;
        HasOnlineDevices = hasOnlineDevices;
        OnlineDeviceCount = onlineDeviceCount;
        DiagnosticCode = diagnosticCode;
        DiagnosticMessage = diagnosticMessage;
    }

    public bool Checked { get; }

    public bool HasOnlineDevices { get; }

    public int OnlineDeviceCount { get; }

    public string? DiagnosticCode { get; }

    public string? DiagnosticMessage { get; }
}
