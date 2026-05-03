using AutomationLauncher.Domain.Models;
using AutomationLauncher.Infrastructure.Tia;
using Siemens.Engineering;
using Xunit;

namespace AutomationLauncher.Infrastructure.Tests;

public sealed class OpennessVersionProviderContractTests : IDisposable
{
    private readonly string _tempRoot;

    public OpennessVersionProviderContractTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "AutomationLauncherProviderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void V15Provider_ReadsModifiedAndFilePath_AndArchivesWithLegacyEnum()
    {
        var project = new LegacyProject
        {
            Name = "LegacyProject",
            Modified = true,
            FilePath = @"C:\Projects\Legacy.ap15"
        };

        FakeOpennessRuntimeState.Reset(new TiaPortalProcess
        {
            Id = 101,
            AttachedPortal = new AttachedPortalProjects { Projects = new object[] { project } }
        });

        var provider = new V15OpennessVersionProvider();
        var runtime = new TiaPortalRuntimeInfo("V15", "TIA Portal V15", @"C:\Portal V15\Siemens.Engineering.dll", "Test");
        var assembly = typeof(TiaPortal).Assembly;

        var context = provider.TryReadOpenProject(assembly, 101, runtime);
        var archivePath = Path.Combine(_tempRoot, "legacy.zap15");
        var archiveResult = provider.TryArchiveProject(assembly, "101", archivePath, runtime);

        Assert.True(context.UnsavedStateDetectedReliably);
        Assert.True(context.HasUnsavedChanges);
        Assert.Equal(@"C:\Projects\Legacy.ap15", context.OpenProjectPath);
        Assert.Equal("LegacyProject", context.ProjectName);
        Assert.True(archiveResult);
        Assert.Equal(Path.Combine(_tempRoot, "legacy"), FakeOpennessRuntimeState.LastArchiveArtifactPath);
    }

    [Fact]
    public void V16Provider_ReadsModifiedAndFilePath_FromSingleProjectPortal()
    {
        var project = new LegacyProject
        {
            Name = "LegacyV16",
            Modified = false,
            FilePath = @"C:\Projects\LegacyV16.ap16"
        };

        FakeOpennessRuntimeState.Reset(new TiaPortalProcess
        {
            Id = 102,
            AttachedPortal = new AttachedPortalSingleProject { Project = project }
        });

        var provider = new V16OpennessVersionProvider();
        var runtime = new TiaPortalRuntimeInfo("V16", "TIA Portal V16", @"C:\Portal V16\Siemens.Engineering.dll", "Test");

        var context = provider.TryReadOpenProject(typeof(TiaPortal).Assembly, 102, runtime);

        Assert.True(context.UnsavedStateDetectedReliably);
        Assert.False(context.HasUnsavedChanges);
        Assert.Equal(@"C:\Projects\LegacyV16.ap16", context.OpenProjectPath);
    }

    [Theory]
    [InlineData("V17")]
    [InlineData("V18")]
    [InlineData("V19")]
    public void StandardProviders_ReadIsModifiedAndPath(string version)
    {
        var project = new StandardProject
        {
            Name = $"Project{version}",
            IsModified = true,
            Path = $@"C:\Projects\{version}.apx"
        };

        FakeOpennessRuntimeState.Reset(new TiaPortalProcess
        {
            Id = 200,
            AttachedPortal = new AttachedPortalProjects { Projects = new object[] { project } }
        });

        var provider = CreateStandardProvider(version);
        var runtime = new TiaPortalRuntimeInfo(version, $"TIA Portal {version}", $@"C:\Portal {version}\Siemens.Engineering.dll", "Test");

        var context = provider.TryReadOpenProject(typeof(TiaPortal).Assembly, 200, runtime);
        var saveResult = provider.TrySaveProject(typeof(TiaPortal).Assembly, "200", runtime);

        Assert.True(context.UnsavedStateDetectedReliably);
        Assert.True(context.HasUnsavedChanges);
        Assert.Equal($@"C:\Projects\{version}.apx", context.OpenProjectPath);
        Assert.True(saveResult);
        Assert.True(FakeOpennessRuntimeState.SaveCalls > 0);
    }

    [Fact]
    public void LatestProvider_ReadsLocationAndArchives()
    {
        var project = new LatestProject
        {
            Name = "LatestProject",
            IsModified = false,
            Location = @"C:\Projects\Latest.apx"
        };

        FakeOpennessRuntimeState.Reset(new TiaPortalProcess
        {
            Id = 300,
            AttachedPortal = new AttachedPortalProjects { Projects = new object[] { project } }
        });

        var provider = new V20OpennessVersionProvider();
        var runtime = new TiaPortalRuntimeInfo("V20", "TIA Portal V20", @"C:\Portal V20\Siemens.Engineering.dll", "Test");
        var archivePath = Path.Combine(_tempRoot, "latest.zap20");

        var context = provider.TryReadOpenProject(typeof(TiaPortal).Assembly, 300, runtime);
        var archiveResult = provider.TryArchiveProject(typeof(TiaPortal).Assembly, "300", archivePath, runtime);

        Assert.True(context.UnsavedStateDetectedReliably);
        Assert.False(context.HasUnsavedChanges);
        Assert.Equal(@"C:\Projects\Latest.apx", context.OpenProjectPath);
        Assert.True(archiveResult);
    }

    [Fact]
    public void V21Provider_ReadsLocationAndArchives()
    {
        var project = new LatestProject
        {
            Name = "V21Project",
            IsModified = true,
            Location = @"C:\Projects\V21.apx"
        };

        FakeOpennessRuntimeState.Reset(new TiaPortalProcess
        {
            Id = 310,
            AttachedPortal = new AttachedPortalProjects { Projects = new object[] { project } }
        });

        var provider = new V21OpennessVersionProvider();
        var runtime = new TiaPortalRuntimeInfo("V21", "TIA Portal V21", @"C:\Portal V21\PublicAPI\V21\net48\Siemens.Engineering.Base.dll", "Test");
        var archivePath = Path.Combine(_tempRoot, "v21.zap21");

        var context = provider.TryReadOpenProject(typeof(TiaPortal).Assembly, 310, runtime);
        var archiveResult = provider.TryArchiveProject(typeof(TiaPortal).Assembly, "310", archivePath, runtime);

        Assert.True(context.UnsavedStateDetectedReliably);
        Assert.True(context.HasUnsavedChanges);
        Assert.Equal(@"C:\Projects\V21.apx", context.OpenProjectPath);
        Assert.Equal("V21Project", context.ProjectName);
        Assert.True(archiveResult);
    }

    [Theory]
    [InlineData("V15", true,  false, false, false, false, false, false, false)]
    [InlineData("V16", false, true,  false, false, false, false, false, false)]
    [InlineData("V17", false, false, true,  false, false, false, false, false)]
    [InlineData("V18", false, false, false, true,  false, false, false, false)]
    [InlineData("V19", false, false, false, false, true,  false, false, false)]
    [InlineData("V20", false, false, false, false, false, true,  false, false)]
    [InlineData("V21", false, false, false, false, false, false, true,  false)]
    [InlineData("V22", false, false, false, false, false, false, false, true)]
    public void Providers_HandleExpectedVersionBands(string version, bool v15, bool v16, bool v17, bool v18, bool v19, bool v20, bool v21, bool latest)
    {
        var runtime = new TiaPortalRuntimeInfo(version, $"TIA Portal {version}", $@"C:\Portal {version}\Siemens.Engineering.dll", "Test");

        Assert.Equal(v15,     new V15OpennessVersionProvider().CanHandle(runtime));
        Assert.Equal(v16,     new V16OpennessVersionProvider().CanHandle(runtime));
        Assert.Equal(v17,     new V17OpennessVersionProvider().CanHandle(runtime));
        Assert.Equal(v18,     new V18OpennessVersionProvider().CanHandle(runtime));
        Assert.Equal(v19,     new V19OpennessVersionProvider().CanHandle(runtime));
        Assert.Equal(v20,     new V20OpennessVersionProvider().CanHandle(runtime));
        Assert.Equal(v21,     new V21OpennessVersionProvider().CanHandle(runtime));
        Assert.Equal(latest,  new LatestOpennessVersionProvider().CanHandle(runtime));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private static IOpennessVersionProvider CreateStandardProvider(string version)
    {
        return version switch
        {
            "V17" => new V17OpennessVersionProvider(),
            "V18" => new V18OpennessVersionProvider(),
            "V19" => new V19OpennessVersionProvider(),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, null)
        };
    }
}