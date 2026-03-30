using System;
using System.Windows;

namespace AutomationLauncher.App;

public interface ISessionCoordinator
{
    event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged;

    bool IsAuthenticated { get; }

    TimeSpan InactivityTimeout { get; }

    bool EnsureAuthenticated(Window? owner = null);

    void RegisterActivity();

    TimeSpan GetRemainingInactivity();

    bool HasTimedOut();

    void Logout(string reason, bool isAutomatic);
}