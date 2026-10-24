using System.Text.Json;

namespace Atoll;

// User preferences, read once at startup from %LOCALAPPDATA%\Atoll\settings.json.
// ponytail: no settings window yet, the file is edited by hand until it exists.
sealed class Settings
{
    // Off by default: unlike a Mac notch, a resting pill covers real content (browser tabs).
    public bool ShowCompactPill { get; set; }

    public static Settings Load()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Atoll", "settings.json");
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return new();
        }
    }
}
