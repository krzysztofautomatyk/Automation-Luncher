using AutomationLauncher.Domain.Models;

namespace AutomationLauncher.Infrastructure.Tia;

internal sealed class V17OpennessVersionProvider : OpennessVersionProviderBase
{
    public override bool CanHandle(TiaPortalRuntimeInfo runtime)
    {
        return TryParseExactVersion(runtime.Version, 17);
    }

    private static bool TryParseExactVersion(string version, int expected)
    {
        return int.TryParse(version.TrimStart('V', 'v'), out var parsed) && parsed == expected;
    }
}

internal sealed class V18OpennessVersionProvider : OpennessVersionProviderBase
{
    public override bool CanHandle(TiaPortalRuntimeInfo runtime)
    {
        return TryParseExactVersion(runtime.Version, 18);
    }

    private static bool TryParseExactVersion(string version, int expected)
    {
        return int.TryParse(version.TrimStart('V', 'v'), out var parsed) && parsed == expected;
    }
}

internal sealed class V19OpennessVersionProvider : OpennessVersionProviderBase
{
    public override bool CanHandle(TiaPortalRuntimeInfo runtime)
    {
        return TryParseExactVersion(runtime.Version, 19);
    }

    private static bool TryParseExactVersion(string version, int expected)
    {
        return int.TryParse(version.TrimStart('V', 'v'), out var parsed) && parsed == expected;
    }
}

internal sealed class LatestOpennessVersionProvider : OpennessVersionProviderBase
{
    protected override string[] ProjectPathPropertyNames => new[] { "Path", "ProjectPath", "FilePath", "Location" };

    protected override string[] ArchiveModeNames => new[] { "Compressed", "Default" };

    public override bool CanHandle(TiaPortalRuntimeInfo runtime)
    {
        return int.TryParse(runtime.Version.TrimStart('V', 'v'), out var version) && version >= 20;
    }
}
