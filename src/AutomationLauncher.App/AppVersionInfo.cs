using System.Reflection;

namespace AutomationLauncher.App;

public static class AppVersionInfo
{
    public const string ProductVersion = "10.0.0";

    public static string DisplayVersion
    {
        get
        {
            var assembly = Assembly.GetExecutingAssembly();
            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                var versionParts = informationalVersion!.Split(new[] { '+' }, 2, StringSplitOptions.None);
                var sanitizedVersion = versionParts.Length > 0 ? versionParts[0] : null;
                if (!string.IsNullOrWhiteSpace(sanitizedVersion))
                {
                    return sanitizedVersion!;
                }
            }

            return assembly.GetName().Version?.ToString() ?? ProductVersion;
        }
    }
}