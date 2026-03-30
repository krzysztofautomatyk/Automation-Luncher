using System;

namespace AutomationLauncher.App;

public sealed class SessionStateChangedEventArgs : EventArgs
{
    public SessionStateChangedEventArgs(bool isAuthenticated, string message, bool isAutomatic)
    {
        IsAuthenticated = isAuthenticated;
        Message = message;
        IsAutomatic = isAutomatic;
    }

    public bool IsAuthenticated { get; }

    public string Message { get; }

    public bool IsAutomatic { get; }
}