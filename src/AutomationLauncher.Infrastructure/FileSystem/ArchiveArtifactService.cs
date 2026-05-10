using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AutomationLauncher.Domain.Contracts;
using AutomationLauncher.Domain.Models;
using Serilog;

namespace AutomationLauncher.Infrastructure.FileSystem;

public sealed class ArchiveArtifactService : IArchiveArtifactService
{
    private readonly ILogger _logger;

    public ArchiveArtifactService(ILogger logger)
    {
        _logger = logger;
    }

    public long? TryGetPathSizeBytes(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return new FileInfo(path).Length;
            }

            if (!Directory.Exists(path))
            {
                return null;
            }

            return Directory
                .EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(TryGetFileLength);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Unable to measure path size. Path={Path}", path);
            return null;
        }
    }

    public void PrepareStableBackupTarget(string archivePath, string oldArchivePath)
    {
        if (File.Exists(oldArchivePath))
        {
            File.Delete(oldArchivePath);
        }

        if (File.Exists(archivePath))
        {
            File.Move(archivePath, oldArchivePath);
        }
    }

    public void FinalizeSuccessfulBackup(ArchiveOptions options, string archivePath, string? oldArchivePath, string archiveIdentity)
    {
        if (options.BackupFlow == ArchiveBackupFlow.StableFileWithOld)
        {
            if (!string.IsNullOrWhiteSpace(oldArchivePath) && File.Exists(oldArchivePath))
            {
                try
                {
                    File.Delete(oldArchivePath);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Unable to delete old stable backup. Path={Path}", oldArchivePath);
                }
            }

            return;
        }

        CleanupOldSuccessfulBackups(archivePath, archiveIdentity, options.SuccessfulBackupRetentionCount);
    }

    public void WriteMetricsLog(ArchiveMetricsLogEntry entry)
    {
        try
        {
            var outputDirectory = Path.GetDirectoryName(entry.ArchivePath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                return;
            }

            Directory.CreateDirectory(outputDirectory);
            var archiveBaseName = Path.GetFileNameWithoutExtension(entry.ArchivePath);
            var metricsLogPath = Path.Combine(outputDirectory, $"{archiveBaseName}.archive.log");
            var content = new StringBuilder()
                .AppendLine("Archive Metrics")
                .AppendLine($"CorrelationId={entry.CorrelationId}")
                .AppendLine($"StartedAt={entry.StartedAt:O}")
                .AppendLine($"ProjectPath={entry.ProjectPath}")
                .AppendLine($"ProjectSizeBytes={FormatBytesValue(entry.ProjectSizeBytes)}")
                .AppendLine($"ProjectSizeMB={FormatMegabytesValue(entry.ProjectSizeBytes)}");

            if (entry.PreSaveAttempted)
            {
                content.AppendLine("PreSaveAttempted=true")
                    .AppendLine($"PreSaveTriggerSource={entry.PreSaveTriggerSource ?? "Unknown"}")
                    .AppendLine($"PreSaveSucceeded={entry.PreSaveSucceeded?.ToString() ?? "N/A"}");
            }
            else
            {
                content.AppendLine("PreSaveAttempted=false");
            }

            content.AppendLine($"FinishedAt={entry.FinishedAt:O}")
                .AppendLine($"ArchivePath={entry.ArchivePath}")
                .AppendLine($"ArchiveSizeBytes={FormatBytesValue(entry.ArchiveSizeBytes)}")
                .AppendLine($"ArchiveSizeMB={FormatMegabytesValue(entry.ArchiveSizeBytes)}")
                .AppendLine($"DurationMs={entry.Duration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)}");

            File.WriteAllText(metricsLogPath, content.ToString());
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Unable to write archive metrics log. ArchivePath={ArchivePath}", entry.ArchivePath);
        }
    }

    private long TryGetFileLength(string filePath)
    {
        try
        {
            return new FileInfo(filePath).Length;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Unable to read file length during archive sizing. Path={Path}", filePath);
            return 0L;
        }
    }

    private void CleanupOldSuccessfulBackups(string currentArchivePath, string archiveIdentity, int retentionCount)
    {
        if (retentionCount <= 0)
        {
            return;
        }

        var directoryPath = Path.GetDirectoryName(currentArchivePath);
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return;
        }

        var matchingFiles = Directory
            .EnumerateFiles(directoryPath, archiveIdentity + "_*.zap*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var obsoleteFile in matchingFiles.Skip(retentionCount))
        {
            try
            {
                File.Delete(obsoleteFile);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Unable to delete obsolete backup during retention cleanup. Path={Path}", obsoleteFile);
            }
        }
    }

    private static string FormatBytesValue(long? bytes)
    {
        return bytes.HasValue
            ? bytes.Value.ToString(CultureInfo.InvariantCulture)
            : "N/A";
    }

    private static string FormatMegabytesValue(long? bytes)
    {
        if (!bytes.HasValue)
        {
            return "N/A";
        }

        var megabytes = bytes.Value / (1024d * 1024d);
        return megabytes.ToString("F2", CultureInfo.InvariantCulture);
    }
}
