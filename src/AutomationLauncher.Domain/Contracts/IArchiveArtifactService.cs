using AutomationLauncher.Domain.Models;

namespace AutomationLauncher.Domain.Contracts;

public interface IArchiveArtifactService
{
    long? TryGetPathSizeBytes(string path);

    void PrepareStableBackupTarget(string archivePath, string oldArchivePath);

    void FinalizeSuccessfulBackup(ArchiveOptions options, string archivePath, string? oldArchivePath, string archiveIdentity);

    void WriteMetricsLog(ArchiveMetricsLogEntry entry);
}
