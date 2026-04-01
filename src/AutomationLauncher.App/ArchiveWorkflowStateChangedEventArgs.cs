using System;

namespace AutomationLauncher.App;

public sealed class ArchiveWorkflowStateChangedEventArgs : EventArgs
{
    public ArchiveWorkflowStateChangedEventArgs(bool isRunning)
    {
        IsRunning = isRunning;
    }

    public bool IsRunning { get; }
}