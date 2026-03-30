using System.IO;
using System.Text.Json;
using AutomationLauncher.Domain.Models;

namespace AutomationLauncher.App;

public sealed class JsonRuntimeSelectionSettingsStore : IRuntimeSelectionSettingsStore
{
    private readonly string _settingsFilePath;

    public JsonRuntimeSelectionSettingsStore(string settingsFilePath)
    {
        _settingsFilePath = settingsFilePath;
    }

    public void SaveRuntimeSelection(ArchiveOptions options)
    {
        var directoryPath = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var payload = new
        {
            Archive = new
            {
                TiaVersionSelectionMode = options.TiaVersionSelectionMode.ToString(),
                PreferredTiaVersion = options.PreferredTiaVersion
            }
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(_settingsFilePath, json);
    }
}