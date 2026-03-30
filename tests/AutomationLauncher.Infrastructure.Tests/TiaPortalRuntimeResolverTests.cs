using AutomationLauncher.Domain.Contracts;
using AutomationLauncher.Domain.Models;
using AutomationLauncher.Infrastructure.Tia;
using Xunit;

namespace AutomationLauncher.Infrastructure.Tests;

public sealed class TiaPortalRuntimeResolverTests
{
    [Fact]
    public void ManualModePrefersConfiguredVersion()
    {
        var options = new ArchiveOptions
        {
            TiaVersionSelectionMode = TiaPortalVersionSelectionMode.Manual,
            PreferredTiaVersion = "V17"
        };

        var resolver = new TiaPortalRuntimeResolver(options, new FakeRuntimeCatalog(
            new TiaPortalRuntimeInfo("V19", "TIA Portal V19", @"C:\V19\Siemens.Engineering.dll", "Installed scan"),
            new TiaPortalRuntimeInfo("V17", "TIA Portal V17", @"C:\V17\Siemens.Engineering.dll", "Installed scan")));

        var resolution = resolver.ResolveDetectedVersion("V19");

        Assert.True(resolution.IsSuccess);
        Assert.Equal("V17", resolution.SelectedRuntime?.Version);
    }

    [Fact]
    public void AutoModeUsesDetectedProcessVersionWhenAvailable()
    {
        var options = new ArchiveOptions
        {
            TiaVersionSelectionMode = TiaPortalVersionSelectionMode.Auto,
            PreferredTiaVersion = "V19"
        };

        var resolver = new TiaPortalRuntimeResolver(options, new FakeRuntimeCatalog(
            new TiaPortalRuntimeInfo("V19", "TIA Portal V19", @"C:\V19\Siemens.Engineering.dll", "Installed scan"),
            new TiaPortalRuntimeInfo("V16", "TIA Portal V16", @"C:\V16\Siemens.Engineering.dll", "Installed scan")));

        var resolution = resolver.ResolveDetectedVersion("V16");

        Assert.True(resolution.IsSuccess);
        Assert.Equal("V16", resolution.SelectedRuntime?.Version);
    }

    [Fact]
    public void AutoModeFallsBackToPreferredThenHighestAvailable()
    {
        var options = new ArchiveOptions
        {
            TiaVersionSelectionMode = TiaPortalVersionSelectionMode.Auto,
            PreferredTiaVersion = "V18"
        };

        var resolver = new TiaPortalRuntimeResolver(options, new FakeRuntimeCatalog(
            new TiaPortalRuntimeInfo("V20", "TIA Portal V20", @"C:\V20\Siemens.Engineering.dll", "Installed scan"),
            new TiaPortalRuntimeInfo("V18", "TIA Portal V18", @"C:\V18\Siemens.Engineering.dll", "Installed scan")));

        var preferredResolution = resolver.ResolveDetectedVersion("V15");
        var fallbackResolver = new TiaPortalRuntimeResolver(
            new ArchiveOptions { TiaVersionSelectionMode = TiaPortalVersionSelectionMode.Auto, PreferredTiaVersion = "V15" },
            new FakeRuntimeCatalog(
                new TiaPortalRuntimeInfo("V20", "TIA Portal V20", @"C:\V20\Siemens.Engineering.dll", "Installed scan"),
                new TiaPortalRuntimeInfo("V18", "TIA Portal V18", @"C:\V18\Siemens.Engineering.dll", "Installed scan")));
        var highestResolution = fallbackResolver.ResolveDetectedVersion(null);

        Assert.True(preferredResolution.IsSuccess);
        Assert.Equal("V18", preferredResolution.SelectedRuntime?.Version);
        Assert.True(highestResolution.IsSuccess);
        Assert.Equal("V20", highestResolution.SelectedRuntime?.Version);
    }

    [Fact]
    public void ManualModeReturnsDiagnosticWhenPreferredVersionMissing()
    {
        var options = new ArchiveOptions
        {
            TiaVersionSelectionMode = TiaPortalVersionSelectionMode.Manual,
            PreferredTiaVersion = "V15"
        };

        var resolver = new TiaPortalRuntimeResolver(options, new FakeRuntimeCatalog(
            new TiaPortalRuntimeInfo("V19", "TIA Portal V19", @"C:\V19\Siemens.Engineering.dll", "Installed scan")));

        var resolution = resolver.ResolveDetectedVersion("V19");

        Assert.False(resolution.IsSuccess);
        Assert.Equal("ManualRuntimeUnavailable", resolution.DiagnosticCode);
    }

    private sealed class FakeRuntimeCatalog : ITiaPortalRuntimeCatalog
    {
        private readonly IReadOnlyList<TiaPortalRuntimeInfo> _runtimes;

        public FakeRuntimeCatalog(params TiaPortalRuntimeInfo[] runtimes)
        {
            _runtimes = runtimes;
        }

        public IReadOnlyList<TiaPortalRuntimeInfo> GetAvailableRuntimes()
        {
            return _runtimes;
        }
    }
}