using AutomationLauncher.Application.Services;
using AutomationLauncher.Domain.Models;
using Xunit;

namespace AutomationLauncher.Application.Tests;

public sealed class HostControlGuardTests
{
    private readonly HostControlGuard _guard = new();

    // ─── CheckStart ──────────────────────────────────────────────────────────

    [Fact]
    public void CheckStart_WhenReady_WithEntries_ReturnsReady()
    {
        var result = _guard.CheckStart(HostControlState.Ready, isSequenceRunning: false, configuredEntryCount: 3);
        Assert.Equal(HostControlCommandReadiness.Ready, result);
    }

    [Theory]
    [InlineData(HostControlState.Running, false)]
    [InlineData(HostControlState.Stopping, false)]
    [InlineData(HostControlState.Ready, true)]
    [InlineData(HostControlState.Running, true)]
    public void CheckStart_WhenAlreadyActiveOrRunning_ReturnsAlreadyRunning(
        HostControlState state, bool isSequenceRunning)
    {
        var result = _guard.CheckStart(state, isSequenceRunning, configuredEntryCount: 3);
        Assert.Equal(HostControlCommandReadiness.AlreadyRunning, result);
    }

    [Fact]
    public void CheckStart_WhenNoEntriesConfigured_ReturnsNoEntriesConfigured()
    {
        var result = _guard.CheckStart(HostControlState.Ready, isSequenceRunning: false, configuredEntryCount: 0);
        Assert.Equal(HostControlCommandReadiness.NoEntriesConfigured, result);
    }

    [Fact]
    public void CheckStart_AlreadyRunningTakesPriorityOverNoEntries()
    {
        // If running AND no entries → AlreadyRunning wins (checked first)
        var result = _guard.CheckStart(HostControlState.Running, isSequenceRunning: false, configuredEntryCount: 0);
        Assert.Equal(HostControlCommandReadiness.AlreadyRunning, result);
    }

    // ─── CheckStop ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(HostControlState.Running, false)]
    [InlineData(HostControlState.Ready, true)]
    [InlineData(HostControlState.Running, true)]
    public void CheckStop_WhenManagedRuntimeActive_ReturnsReady(
        HostControlState state, bool isSequenceRunning)
    {
        var result = _guard.CheckStop(state, isSequenceRunning);
        Assert.Equal(HostControlCommandReadiness.Ready, result);
    }

    [Theory]
    [InlineData(HostControlState.Ready)]
    [InlineData(HostControlState.Error)]
    [InlineData(HostControlState.Stopping)]
    public void CheckStop_WhenNothingActive_ReturnsNothingToStop(HostControlState state)
    {
        var result = _guard.CheckStop(state, isSequenceRunning: false);
        Assert.Equal(HostControlCommandReadiness.NothingToStop, result);
    }

    // ─── CheckMarch ──────────────────────────────────────────────────────────

    [Fact]
    public void CheckMarch_WhenNotBusy_ReturnsReady()
    {
        var result = _guard.CheckMarch(isArchiveBusy: false);
        Assert.Equal(HostControlCommandReadiness.Ready, result);
    }

    [Fact]
    public void CheckMarch_WhenBusy_ReturnsAlreadyRunning()
    {
        var result = _guard.CheckMarch(isArchiveBusy: true);
        Assert.Equal(HostControlCommandReadiness.AlreadyRunning, result);
    }
}
