using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HardwareMonitor;

public sealed record HotkeySettings(uint Modifiers, uint VirtualKey, string Text);

public sealed class AppSettings
{
    public HashSet<string> SelectedSensors { get; set; } = new(StringComparer.Ordinal);
    public bool DarkTheme { get; set; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OverlayTheme { get; set; }
    public string? OverlayTextColor { get; set; }
    public bool OverlayOutline { get; set; } = true;
    public double OverlayOpacity { get; set; } = 1;
    public double OverlayFontSize { get; set; } = 14;

    public HotkeySettings? OverlayHotkey { get; set; }
    public HotkeySettings? LockHotkey { get; set; }
    public double? OverlayLeft { get; set; }
    public double? OverlayTop { get; set; }
    public bool OverlayLocked { get; set; }
}

public sealed class SettingsStore
{
    public string Path { get; }
    public SettingsStore(string? path = null) => Path = System.IO.Path.GetFullPath(path ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HardwareMonitor", "settings.json"));

    public AppSettings Load(out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(Path)) return Normalize(new AppSettings());
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path)) ?? throw new JsonException("Пустой файл настроек.");
            settings.SelectedSensors ??= new(StringComparer.Ordinal);
            if (settings.OverlayLeft is double left && !double.IsFinite(left)) settings.OverlayLeft = null;
            if (settings.OverlayTop is double top && !double.IsFinite(top)) settings.OverlayTop = null;
            return Normalize(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = "Не удалось прочитать настройки: " + ex.Message;
            return Normalize(new AppSettings());
        }
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        string legacyColor = settings.OverlayTheme switch
        {
            "Зеленый / Лайм" => "#AAFF00", "Голубой / Светло-голубой" => "#ADD8E6",
            "Оранжевый / Золотой" => "#FFD700", "Серый / Минимализм" => "#E0E0E0",
            "Красная тревога" => "#FF8C00", _ => "#F4F6FA"
        };
        string color = settings.OverlayTextColor ?? legacyColor;
        settings.OverlayTextColor = color.Length == 7 && color[0] == '#' &&
            color.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0 ? color.ToUpperInvariant() : "#F4F6FA";
        settings.OverlayTheme = null;
        settings.OverlayOpacity = double.IsFinite(settings.OverlayOpacity) ? Math.Clamp(settings.OverlayOpacity, .1, 1) : 1;
        settings.OverlayFontSize = double.IsFinite(settings.OverlayFontSize) ? Math.Clamp(settings.OverlayFontSize, 12, 20) : 14;
        return settings;
    }

    public string? Save(AppSettings settings)
    {
        string temporary = Path + ".tmp";
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, Path, true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return "Не удалось сохранить настройки: " + ex.Message;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
