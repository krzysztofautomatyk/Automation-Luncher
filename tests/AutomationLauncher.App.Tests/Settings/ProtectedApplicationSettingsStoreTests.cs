using System.IO;
using AutomationLauncher.App;
using Xunit;

namespace AutomationLauncher.App.Tests.Settings;

public sealed class ProtectedApplicationSettingsStoreTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _settingsPath;
    private readonly ProtectedApplicationSettingsStore _store;

    private const string ValidPassword = "TestP@ssw0rd!";

    public ProtectedApplicationSettingsStoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "ALTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _settingsPath = Path.Combine(_testDir, "protected-settings.json");
        _store = new ProtectedApplicationSettingsStore(_settingsPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); } catch { }
    }

    // ─── HasProtectedSettings ────────────────────────────────────────────────

    [Fact]
    public void HasProtectedSettings_WhenFileDoesNotExist_ReturnsFalse()
    {
        Assert.False(_store.HasProtectedSettings());
    }

    [Fact]
    public void HasProtectedSettings_AfterCreate_ReturnsTrue()
    {
        _store.Create(new AutomationLauncherSettings(), ValidPassword);
        Assert.True(_store.HasProtectedSettings());
    }

    // ─── ValidatePasswordRequirements ────────────────────────────────────────

    [Theory]
    [InlineData("short1A!",          false, "12 characters")]
    [InlineData("alllowercase1!",    false, "uppercase")]
    [InlineData("ALLUPPERCASE1!",    false, "lowercase")]
    [InlineData("NoDigitHere!AbCd",  false, "digit")]
    [InlineData("NoSpecialChar12Ab", false, "special")]
    [InlineData("ValidP@ssw0rd123",  true,  "")]
    public void ValidatePasswordRequirements_ReturnsExpectedResult(
        string password, bool expectedValid, string expectedMessageFragment)
    {
        var isValid = _store.ValidatePasswordRequirements(password, out var msg);

        Assert.Equal(expectedValid, isValid);
        if (!expectedValid)
        {
            Assert.Contains(expectedMessageFragment, msg, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Empty(msg);
        }
    }

    // ─── Create + TryLoad round-trip ─────────────────────────────────────────

    [Fact]
    public void CreateAndTryLoad_WithCorrectPassword_ReturnsSameSettings()
    {
        var original = new AutomationLauncherSettings();
        original.Logging.DirectoryPath = @"C:\Logs\MyCustomPath";

        _store.Create(original, ValidPassword);
        var loaded = _store.TryLoad(ValidPassword, out var settings, out var error);

        Assert.True(loaded, $"TryLoad failed: {error}");
        Assert.NotNull(settings);
        Assert.Equal(original.Logging.DirectoryPath, settings!.Logging.DirectoryPath);
    }

    [Fact]
    public void TryLoad_WithWrongPassword_ReturnsFalse()
    {
        _store.Create(new AutomationLauncherSettings(), ValidPassword);
        var loaded = _store.TryLoad("WrongP@ssw0rd99!", out _, out var error);

        Assert.False(loaded);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void TryLoad_WhenNoFileExists_ReturnsFalse()
    {
        var loaded = _store.TryLoad(ValidPassword, out _, out var error);

        Assert.False(loaded);
        Assert.NotEmpty(error);
    }

    // ─── TryLoadCachedSettings ───────────────────────────────────────────────

    [Fact]
    public void TryLoadCachedSettings_WhenNoCacheFile_ReturnsFalse()
    {
        var loaded = _store.TryLoadCachedSettings(out _, out var error);

        Assert.False(loaded);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Save_ThenTryLoadCached_ReturnsSettings()
    {
        var original = new AutomationLauncherSettings();
        original.Logging.DirectoryPath = @"C:\Logs\CachedPath";

        _store.Save(original, ValidPassword);
        var loaded = _store.TryLoadCachedSettings(out var settings, out var error);

        Assert.True(loaded, $"TryLoadCachedSettings failed: {error}");
        Assert.NotNull(settings);
        Assert.Equal(original.Logging.DirectoryPath, settings!.Logging.DirectoryPath);
    }
}
