using AutomationLauncher.Domain.Models;
using AutomationLauncher.Infrastructure.Tia;
using Xunit;

namespace AutomationLauncher.Infrastructure.Tests;

public sealed class TiaPortalRuntimeCatalogTests
{
    [Fact]
    public void ReturnsConfiguredOverrideAndInstalledRuntimes_SortedByVersionDescending()
    {
        var options = new ArchiveOptions
        {
            OpennessAssemblyPath = @"C:\Configured\Portal V19\PublicAPI\V19\Siemens.Engineering.dll",
            KnownVersions =
            {
                new TiaPortalRuntimeConfiguration
                {
                    Version = "V18",
                    OpennessAssemblyPath = @"D:\Overrides\Portal V18\PublicAPI\V18\Siemens.Engineering.dll"
                }
            }
        };

        var existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Configured\Portal V19\PublicAPI\V19\Siemens.Engineering.dll",
            @"D:\Overrides\Portal V18\PublicAPI\V18\Siemens.Engineering.dll",
            @"C:\Program Files\Siemens\Automation\Portal V15\PublicAPI\V15\Siemens.Engineering.dll",
            @"C:\Program Files\Siemens\Automation\Portal V17\PublicAPI\V17\Siemens.Engineering.dll"
        };

        var existingDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Program Files\Siemens\Automation"
        };

        var catalog = new TiaPortalRuntimeCatalog(
            options,
            existingFiles.Contains,
            existingDirectories.Contains,
            (path, pattern) => new[]
            {
                @"C:\Program Files\Siemens\Automation\Portal V15",
                @"C:\Program Files\Siemens\Automation\Portal V17"
            },
            _ => null,
            folder => folder == Environment.SpecialFolder.ProgramFiles ? @"C:\Program Files" : @"D:\Unused");

        var runtimes = catalog.GetAvailableRuntimes();

        Assert.Collection(
            runtimes,
            runtime =>
            {
                Assert.Equal("V19", runtime.Version);
                Assert.Equal("Configured path", runtime.Source);
            },
            runtime =>
            {
                Assert.Equal("V18", runtime.Version);
                Assert.Equal("Version override", runtime.Source);
            },
            runtime =>
            {
                Assert.Equal("V17", runtime.Version);
                Assert.Equal("Installed scan", runtime.Source);
            },
            runtime =>
            {
                Assert.Equal("V15", runtime.Version);
                Assert.Equal("Installed scan", runtime.Source);
            });
    }

    [Fact]
    public void OverrideWinsOverConfiguredRuntime_WhenVersionMatches()
    {
        var options = new ArchiveOptions
        {
            OpennessAssemblyPath = @"C:\Configured\Portal V19\PublicAPI\V19\Siemens.Engineering.dll",
            KnownVersions =
            {
                new TiaPortalRuntimeConfiguration
                {
                    Version = "V19",
                    OpennessAssemblyPath = @"D:\Overrides\Portal V19\PublicAPI\V19\Siemens.Engineering.dll"
                }
            }
        };

        var existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Configured\Portal V19\PublicAPI\V19\Siemens.Engineering.dll",
            @"D:\Overrides\Portal V19\PublicAPI\V19\Siemens.Engineering.dll"
        };

        var catalog = new TiaPortalRuntimeCatalog(
            options,
            existingFiles.Contains,
            _ => false,
            (_, _) => Array.Empty<string>(),
            _ => null,
            _ => string.Empty);

        var runtime = Assert.Single(catalog.GetAvailableRuntimes());

        Assert.Equal("V19", runtime.Version);
        Assert.Equal(@"D:\Overrides\Portal V19\PublicAPI\V19\Siemens.Engineering.dll", runtime.OpennessAssemblyPath);
        Assert.Equal("Version override", runtime.Source);
    }

    [Fact]
    public void UsesAssemblyVersionWhenPathDoesNotContainPortalVersion()
    {
        var options = new ArchiveOptions
        {
            OpennessAssemblyPath = @"C:\Custom\Siemens.Engineering.dll"
        };

        var catalog = new TiaPortalRuntimeCatalog(
            options,
            path => string.Equals(path, @"C:\Custom\Siemens.Engineering.dll", StringComparison.OrdinalIgnoreCase),
            _ => false,
            (_, _) => Array.Empty<string>(),
            _ => new Version(20, 0, 0, 0),
            _ => string.Empty);

        var runtime = Assert.Single(catalog.GetAvailableRuntimes());

        Assert.Equal("V20", runtime.Version);
        Assert.Equal("Configured path", runtime.Source);
    }

    [Fact]
    public void ScansV21UsingNet48SplitAssemblyPath()
    {
        var options = new ArchiveOptions();

        var existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48\Siemens.Engineering.Base.dll"
        };

        var existingDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Program Files\Siemens\Automation"
        };

        var catalog = new TiaPortalRuntimeCatalog(
            options,
            existingFiles.Contains,
            existingDirectories.Contains,
            (path, pattern) => new[] { @"C:\Program Files\Siemens\Automation\Portal V21" },
            _ => null,
            folder => folder == Environment.SpecialFolder.ProgramFiles ? @"C:\Program Files" : @"D:\Unused");

        var runtime = Assert.Single(catalog.GetAvailableRuntimes());

        Assert.Equal("V21", runtime.Version);
        Assert.Equal(@"C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48\Siemens.Engineering.Base.dll", runtime.OpennessAssemblyPath);
        Assert.Equal("Installed scan", runtime.Source);
    }

    [Fact]
    public void ScansV19UsingClassicAssemblyPath_WhenNet48NotPresent()
    {
        var options = new ArchiveOptions();

        var existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Program Files\Siemens\Automation\Portal V19\PublicAPI\V19\Siemens.Engineering.dll"
        };

        var existingDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Program Files\Siemens\Automation"
        };

        var catalog = new TiaPortalRuntimeCatalog(
            options,
            existingFiles.Contains,
            existingDirectories.Contains,
            (path, pattern) => new[] { @"C:\Program Files\Siemens\Automation\Portal V19" },
            _ => null,
            folder => folder == Environment.SpecialFolder.ProgramFiles ? @"C:\Program Files" : @"D:\Unused");

        var runtime = Assert.Single(catalog.GetAvailableRuntimes());

        Assert.Equal("V19", runtime.Version);
        Assert.Equal(@"C:\Program Files\Siemens\Automation\Portal V19\PublicAPI\V19\Siemens.Engineering.dll", runtime.OpennessAssemblyPath);
        Assert.Equal("Installed scan", runtime.Source);
    }
}