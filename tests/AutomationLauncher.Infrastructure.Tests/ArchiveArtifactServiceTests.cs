using System.IO;
using AutomationLauncher.Domain.Models;
using AutomationLauncher.Infrastructure.FileSystem;
using Serilog;
using Xunit;

namespace AutomationLauncher.Infrastructure.Tests;

public sealed class ArchiveArtifactServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly ArchiveArtifactService _service;

    public ArchiveArtifactServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "AutomationLauncherInfraTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _service = new ArchiveArtifactService(new LoggerConfiguration().CreateLogger());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void TryGetPathSizeBytes_ReturnsCombinedFileLength_ForDirectory()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, "a.txt"), "1234");
        File.WriteAllText(Path.Combine(_tempDirectory, "b.txt"), "12");

        var bytes = _service.TryGetPathSizeBytes(_tempDirectory);

        Assert.Equal(6, bytes);
    }

    [Fact]
    public void PrepareStableBackupTarget_MovesCurrentArchive_ToOldSlot()
    {
        var archivePath = Path.Combine(_tempDirectory, "current.zap19");
        var oldArchivePath = Path.Combine(_tempDirectory, "current_old.zap19");
        File.WriteAllText(archivePath, "current");

        _service.PrepareStableBackupTarget(archivePath, oldArchivePath);

        Assert.False(File.Exists(archivePath));
        Assert.True(File.Exists(oldArchivePath));
    }

    [Fact]
    public void FinalizeSuccessfulBackup_DeletesObsoleteTimestampedBackups()
    {
        var archiveIdentity = Environment.MachineName + "_automaticBackup";
        File.WriteAllText(Path.Combine(_tempDirectory, $"{archiveIdentity}_20260101_010101.zap19"), "1");
        File.WriteAllText(Path.Combine(_tempDirectory, $"{archiveIdentity}_20260102_010101.zap19"), "2");
        var currentArchivePath = Path.Combine(_tempDirectory, $"{archiveIdentity}_20260103_010101.zap19");
        File.WriteAllText(currentArchivePath, "3");

        _service.FinalizeSuccessfulBackup(
            new ArchiveOptions
            {
                BackupFlow = ArchiveBackupFlow.TimestampedRetention,
                SuccessfulBackupRetentionCount = 2
            },
            currentArchivePath,
            null,
            archiveIdentity);

        var remainingFiles = Directory.GetFiles(_tempDirectory, $"{archiveIdentity}_*.zap19");
        Assert.Equal(2, remainingFiles.Length);
    }

    [Fact]
    public void WriteMetricsLog_CreatesArchiveLogBesideArchive()
    {
        var archivePath = Path.Combine(_tempDirectory, "backup.zap19");

        _service.WriteMetricsLog(new ArchiveMetricsLogEntry(
            archivePath,
            "corr-1",
            DateTimeOffset.UtcNow.AddSeconds(-10),
            DateTimeOffset.UtcNow,
            @"C:\Projects\Target.ap19",
            1024,
            256,
            TimeSpan.FromSeconds(10),
            true,
            true,
            "UserSaveNow"));

        var metricsPath = Path.Combine(_tempDirectory, "backup.archive.log");
        Assert.True(File.Exists(metricsPath));
        Assert.Contains("CorrelationId=corr-1", File.ReadAllText(metricsPath));
    }
}
