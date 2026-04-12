using AutomationLauncher.Domain.Models;

namespace AutomationLauncher.Infrastructure.Tia;

internal sealed class V15OpennessVersionProvider : OpennessVersionProviderBase
{
    protected override string[] ProjectDirtyPropertyNames => new[] { "Modified", "IsModified" };

    protected override string[] ProjectPathPropertyNames => new[] { "FilePath", "Path", "ProjectPath" };

    protected override string[] ArchiveEnumTypeNames => new[] { "Siemens.Engineering.ProjectArchivationMode", "Siemens.Engineering.ProjectArchiveMode" };

    protected override string[] ArchiveModeNames => new[] { "Compressed", "Default" };

    public override bool CanHandle(TiaPortalRuntimeInfo runtime)
    {
        return TryParseExactVersion(runtime.Version, 15);
    }

    private static bool TryParseExactVersion(string version, int expected)
    {
        return int.TryParse(version.TrimStart('V', 'v'), out var parsed) && parsed == expected;
    }
}

internal sealed class V16OpennessVersionProvider : OpennessVersionProviderBase
{
    protected override string[] ProjectDirtyPropertyNames => new[] { "Modified", "IsModified" };

    protected override string[] ProjectPathPropertyNames => new[] { "FilePath", "Path", "ProjectPath" };

    public override bool CanHandle(TiaPortalRuntimeInfo runtime)
    {
        return TryParseExactVersion(runtime.Version, 16);
    }

    private static bool TryParseExactVersion(string version, int expected)
    {
        return int.TryParse(version.TrimStart('V', 'v'), out var parsed) && parsed == expected;
    }
}
