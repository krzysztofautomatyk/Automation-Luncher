using Media = System.Windows.Media;

namespace AutomationLauncher.App;

public sealed class LogLineEntry
{
    private static readonly Media.Brush SearchMatchBackground = CreateFrozenBrush(255, 247, 204);
    private static readonly Media.Brush ErrorBackground = CreateFrozenBrush(255, 232, 232);
    private static readonly Media.Brush WarningBackground = CreateFrozenBrush(255, 242, 224);
    private static readonly Media.Brush InfoBackground = CreateFrozenBrush(236, 247, 255);
    private static readonly Media.Brush VerboseBackground = CreateFrozenBrush(245, 245, 245);
    private static readonly Media.Brush DefaultBackground = CreateFrozenBrush(255, 255, 255);
    private static readonly Media.Brush ErrorForeground = CreateFrozenBrush(137, 27, 27);
    private static readonly Media.Brush WarningForeground = CreateFrozenBrush(166, 101, 0);
    private static readonly Media.Brush DebugForeground = CreateFrozenBrush(48, 84, 120);
    private static readonly Media.Brush VerboseForeground = CreateFrozenBrush(88, 88, 88);
    private static readonly Media.Brush InfoForeground = CreateFrozenBrush(26, 72, 116);
    private static readonly Media.Brush DefaultForeground = CreateFrozenBrush(34, 34, 34);

    public LogLineEntry(string message, string level, bool isSearchMatch)
    {
        Message = message;
        Level = level;
        Foreground = GetForeground(level);
        Background = isSearchMatch ? SearchMatchBackground : GetBackground(level);
    }

    public string Message { get; }

    public string Level { get; }

    public Media.Brush Foreground { get; }

    public Media.Brush Background { get; }

    private static Media.Brush GetForeground(string level)
    {
        return level switch
        {
            "ERR" or "FTL" => ErrorForeground,
            "WRN" => WarningForeground,
            "DBG" => DebugForeground,
            "VRB" => VerboseForeground,
            "INF" => InfoForeground,
            _ => DefaultForeground
        };
    }

    private static Media.Brush GetBackground(string level)
    {
        return level switch
        {
            "ERR" or "FTL" => ErrorBackground,
            "WRN" => WarningBackground,
            "INF" => InfoBackground,
            "DBG" or "VRB" => VerboseBackground,
            _ => DefaultBackground
        };
    }

    private static Media.Brush CreateFrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new Media.SolidColorBrush(Media.Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
