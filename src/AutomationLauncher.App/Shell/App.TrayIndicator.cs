using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Threading;
using AutomationLauncher.Domain.Models;
using AutomationLauncher.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace AutomationLauncher.App;
public partial class App : System.Windows.Application
{
    private void StartStartupIndicator()
    {
        SetTrayIndicatorMode(TrayIndicatorMode.Startup);
    }

    private void SetTrayIndicatorMode(TrayIndicatorMode mode)
    {
        if (_notifyIcon is null)
        {
            return;
        }

        if (mode != TrayIndicatorMode.Error && _hasErrorControlFile)
        {
            mode = TrayIndicatorMode.Error;
        }

        _trayIndicatorMode = mode;

        if (mode == TrayIndicatorMode.None)
        {
            if (_startupIndicatorTimer is not null)
            {
                _startupIndicatorTimer.Stop();
                _startupIndicatorTimer.Tick -= HandleStartupIndicatorTick;
            }

            _startupIndicatorUsesWarningIcon = false;
            _notifyIcon.Icon = AppIconFactory.GetTrayIcon();
            return;
        }

        _startupIndicatorTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };

        _startupIndicatorTimer.Tick -= HandleStartupIndicatorTick;
        _startupIndicatorTimer.Tick += HandleStartupIndicatorTick;
        _startupIndicatorUsesWarningIcon = true;
        _notifyIcon.Icon = GetIndicatorIconForCurrentMode();
        _startupIndicatorTimer.Start();
    }

    private void StopStartupIndicator()
    {
        if (_trayIndicatorMode == TrayIndicatorMode.Startup)
        {
            SetTrayIndicatorMode(TrayIndicatorMode.None);
        }
    }

    private void HandleStartupIndicatorTick(object? sender, EventArgs e)
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Icon = _startupIndicatorUsesWarningIcon
            ? AppIconFactory.GetTrayIcon()
            : GetIndicatorIconForCurrentMode();
        _startupIndicatorUsesWarningIcon = !_startupIndicatorUsesWarningIcon;
    }

    private Icon GetIndicatorIconForCurrentMode()
    {
        return _trayIndicatorMode switch
        {
            TrayIndicatorMode.Archiving => AppIconFactory.GetArchiveTrayIcon(),
            TrayIndicatorMode.StopPending => AppIconFactory.GetStopTrayIcon(),
            TrayIndicatorMode.Error => AppIconFactory.GetErrorTrayIcon(),
            _ => AppIconFactory.GetStartupTrayIcon()
        };
    }

    private void HandleArchiveWorkflowStateChanged(object? sender, ArchiveWorkflowStateChangedEventArgs e)
    {
        SetTrayIndicatorMode(e.IsRunning ? TrayIndicatorMode.Archiving : GetPreferredTrayIndicatorMode());
        UpdateTrayMenuState();
    }

    private TrayIndicatorMode GetPreferredTrayIndicatorMode()
    {
        if (_hasErrorControlFile || _hostControlState == HostControlState.Error)
        {
            return TrayIndicatorMode.Error;
        }

        if (_hostControlState == HostControlState.Stopping)
        {
            return TrayIndicatorMode.StopPending;
        }

        if (_isStartupSequenceRunning)
        {
            return TrayIndicatorMode.Startup;
        }

        return TrayIndicatorMode.None;
    }
}

