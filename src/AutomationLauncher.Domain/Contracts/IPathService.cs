namespace AutomationLauncher.Domain.Contracts;

public interface IPathService
{
    string NormalizePath(string path);
    string BuildArchiveFilePath(string projectPath, string outputDirectory, DateTimeOffset timestamp);
    string BuildArchiveFilePath(string projectPath, string outputDirectory, string fileNameWithoutExtension);
    void EnsureDirectoryExists(string path);
}
