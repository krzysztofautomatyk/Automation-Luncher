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

internal sealed class V20OpennessVersionProvider : OpennessVersionProviderBase
{
    protected override string[] ProjectPathPropertyNames => new[] { "Path", "ProjectPath", "FilePath", "Location" };

    protected override string[] ArchiveModeNames => new[] { "Compressed", "Default" };

    public override bool CanHandle(TiaPortalRuntimeInfo runtime)
    {
        return int.TryParse(runtime.Version.TrimStart('V', 'v'), out var version) && version == 20;
    }
}

/// <summary>
/// Provider for TIA Portal V21.
/// In V21 the monolithic Siemens.Engineering.dll was split into separate assemblies
/// (Siemens.Engineering.Base.dll, Siemens.Engineering.Step7.dll, …).
/// The core types used by this provider (TiaPortal, TiaPortalProcess, OnlineProvider,
/// SoftwareContainer, ProjectArchivationMode) all reside in Siemens.Engineering.Base.dll
/// which is what the catalog sets as OpennessAssemblyPath for V21 installations.
/// CompareToOnline() is accessed via reflection on the live PlcSoftware COM object,
/// so the split assembly layout is transparent to the provider logic.
/// </summary>
internal sealed class V21OpennessVersionProvider : OpennessVersionProviderBase
{
    protected override string[] ProjectPathPropertyNames => new[] { "Path", "ProjectPath", "FilePath", "Location" };

    protected override string[] ArchiveModeNames => new[] { "Compressed", "Default" };

    public override bool CanHandle(TiaPortalRuntimeInfo runtime)
    {
        return int.TryParse(runtime.Version.TrimStart('V', 'v'), out var version) && version == 21;
    }
}

internal sealed class LatestOpennessVersionProvider : OpennessVersionProviderBase
{
    protected override string[] ProjectPathPropertyNames => new[] { "Path", "ProjectPath", "FilePath", "Location" };

    protected override string[] ArchiveModeNames => new[] { "Compressed", "Default" };

    public override bool CanHandle(TiaPortalRuntimeInfo runtime)
    {
        return int.TryParse(runtime.Version.TrimStart('V', 'v'), out var version) && version >= 22;
    }
}
