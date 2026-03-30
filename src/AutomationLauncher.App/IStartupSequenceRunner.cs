using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AutomationLauncher.App;

public interface IStartupSequenceRunner
{
    Task<StartupSequenceRunResult> RunAsync(
        IReadOnlyList<StartupSequenceEntry> entries,
        StartupSequenceSplashWindow splashWindow,
        CancellationToken cancellationToken);
}