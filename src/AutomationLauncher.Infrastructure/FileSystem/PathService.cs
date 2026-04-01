using System.IO;
using AutomationLauncher.Domain.Contracts;

namespace AutomationLauncher.Infrastructure.FileSystem;

public sealed class PathService : IPathService
{
    public string NormalizePath(string path)
    {
        var cleaned = path.Trim().Trim('"', '\'');
        var full = Path.GetFullPath(cleaned);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public string BuildArchiveFilePath(string projectPath, string outputDirectory, DateTimeOffset timestamp)
    {
        var fileNameWithoutExtension = $"{Environment.MachineName}_automaticBackup_{timestamp:yyyyMMdd_HHmmss}";
        return BuildArchiveFilePath(projectPath, outputDirectory, fileNameWithoutExtension);
    }

    public string BuildArchiveFilePath(string projectPath, string outputDirectory, string fileNameWithoutExtension)
    {
        var normalizedDirectory = NormalizePath(outputDirectory);
        var archiveExtension = ResolveArchiveExtension(projectPath);
        return Path.Combine(normalizedDirectory, fileNameWithoutExtension + archiveExtension);
    }

    public void EnsureDirectoryExists(string path)
    {
        Directory.CreateDirectory(path);
    }

    private static string ResolveArchiveExtension(string projectPath)
    {
        var projectExtension = (Path.GetExtension(projectPath) ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(projectExtension))
        {
            return ".zap19";
        }

        if (projectExtension.StartsWith(".zap", StringComparison.OrdinalIgnoreCase))
        {
            return projectExtension;
        }

        if (projectExtension.Length > 3 && projectExtension.StartsWith(".ap", StringComparison.OrdinalIgnoreCase))
        {
            return ".zap" + projectExtension.Substring(3);
        }

        return ".zap19";
    }
}
