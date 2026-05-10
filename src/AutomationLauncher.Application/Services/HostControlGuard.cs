using AutomationLauncher.Domain.Contracts;
using AutomationLauncher.Domain.Models;

namespace AutomationLauncher.Application.Services;

/// <summary>
/// Evaluates host-control command preconditions.
/// Pure business-rule logic — no I/O, no side effects.
/// </summary>
public sealed class HostControlGuard : IHostControlGuard
{
    /// <inheritdoc/>
    public HostControlCommandReadiness CheckStart(
        HostControlState currentState,
        bool isSequenceRunning,
        int configuredEntryCount)
    {
        if (isSequenceRunning
            || currentState == HostControlState.Running
            || currentState == HostControlState.Stopping)
        {
            return HostControlCommandReadiness.AlreadyRunning;
        }

        if (configuredEntryCount == 0)
        {
            return HostControlCommandReadiness.NoEntriesConfigured;
        }

        return HostControlCommandReadiness.Ready;
    }

    /// <inheritdoc/>
    public HostControlCommandReadiness CheckStop(
        HostControlState currentState,
        bool isSequenceRunning)
    {
        if (currentState != HostControlState.Running && !isSequenceRunning)
        {
            return HostControlCommandReadiness.NothingToStop;
        }

        return HostControlCommandReadiness.Ready;
    }

    /// <inheritdoc/>
    public HostControlCommandReadiness CheckMarch(bool isArchiveBusy)
    {
        return isArchiveBusy
            ? HostControlCommandReadiness.AlreadyRunning
            : HostControlCommandReadiness.Ready;
    }
}
