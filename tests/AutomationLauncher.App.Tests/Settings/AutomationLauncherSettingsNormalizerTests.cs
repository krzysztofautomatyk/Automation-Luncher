using Xunit;

namespace AutomationLauncher.App.Tests.Settings;

public sealed class AutomationLauncherSettingsNormalizerTests
{
    [Fact]
    public void Normalize_InfersLegacyActions_AndPreservesCustomVariants()
    {
        var settings = new AutomationLauncherSettings
        {
            ControlFiles = new ControlFilesSettings
            {
                Bindings = new List<ControlFileScriptBinding>
                {
                    new() { ControlFileType = ".start" },
                    new() { ControlFileType = "stop" },
                    new() { ControlFileType = "march" },
                    new() { ControlFileType = "archive-fast", Action = HostControlCommandAction.Archive }
                }
            }
        };

        AutomationLauncherSettingsNormalizer.Normalize(settings);

        Assert.Collection(
            settings.ControlFiles.Bindings,
            binding =>
            {
                Assert.Equal("start", binding.ControlFileType);
                Assert.Equal(HostControlCommandAction.Start, binding.Action);
                Assert.Equal(ControlFileScriptBinding.BuildDefaultSplashCountdownSeconds(HostControlCommandAction.Start), binding.SplashCountdownSeconds);
            },
            binding =>
            {
                Assert.Equal("stop", binding.ControlFileType);
                Assert.Equal(HostControlCommandAction.Stop, binding.Action);
                Assert.Equal(ControlFileScriptBinding.BuildDefaultSplashCountdownSeconds(HostControlCommandAction.Stop), binding.SplashCountdownSeconds);
            },
            binding =>
            {
                Assert.Equal("march", binding.ControlFileType);
                Assert.Equal(HostControlCommandAction.Archive, binding.Action);
                Assert.Equal(ControlFileScriptBinding.BuildDefaultSplashCountdownSeconds(HostControlCommandAction.Archive), binding.SplashCountdownSeconds);
            },
            binding =>
            {
                Assert.Equal("archive-fast", binding.ControlFileType);
                Assert.Equal(HostControlCommandAction.Archive, binding.Action);
                Assert.Equal(ControlFileScriptBinding.BuildDefaultSplashCountdownSeconds(HostControlCommandAction.Archive), binding.SplashCountdownSeconds);
            });
    }

    [Fact]
    public void Normalize_RemovesInvalidOrReservedBindings_WithoutReaddingDefaults()
    {
        var settings = new AutomationLauncherSettings
        {
            ControlFiles = new ControlFilesSettings
            {
                Bindings = new List<ControlFileScriptBinding>
                {
                    new() { ControlFileType = "archive-only", Action = HostControlCommandAction.Archive },
                    new() { ControlFileType = "ready", Action = HostControlCommandAction.Stop },
                    new() { ControlFileType = "bad/value", Action = HostControlCommandAction.Start },
                    new() { ControlFileType = "ARCHIVE-ONLY", Action = HostControlCommandAction.Archive }
                }
            }
        };

        AutomationLauncherSettingsNormalizer.Normalize(settings);

        var binding = Assert.Single(settings.ControlFiles.Bindings);
        Assert.Equal("archive-only", binding.ControlFileType);
        Assert.Equal(HostControlCommandAction.Archive, binding.Action);
    }

    [Fact]
    public void Normalize_PreservesCustomSplashSettings()
    {
        var settings = new AutomationLauncherSettings
        {
            ControlFiles = new ControlFilesSettings
            {
                Bindings = new List<ControlFileScriptBinding>
                {
                    new()
                    {
                        ControlFileType = "archive-only",
                        Action = HostControlCommandAction.Archive,
                        SplashTitle = "My Archive Splash",
                        SplashBackgroundImagePath = @"C:\Images\archive.png",
                        SplashCountdownSeconds = 7
                    }
                }
            }
        };

        AutomationLauncherSettingsNormalizer.Normalize(settings);

        var binding = Assert.Single(settings.ControlFiles.Bindings);
        Assert.Equal("My Archive Splash", binding.SplashTitle);
        Assert.Equal(@"C:\Images\archive.png", binding.SplashBackgroundImagePath);
        Assert.Equal(7, binding.SplashCountdownSeconds);
    }
}
