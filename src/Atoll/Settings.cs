using System.Text.Json;

namespace Atoll;

enum Pop { Music, Timers, Downloads, Devices, Volume, Clipboard }

// User preferences, read once at startup from %LOCALAPPDATA%\Atoll\settings.json.
// ponytail: no settings window yet, the file is edited by hand (tray menu) and applies on restart.
sealed class Settings
{
    // Off by default: unlike a Mac notch, a resting pill covers real content (browser tabs).
    public bool ShowCompactPill { get; set; }

    // What may pop up on its own. Volume is opt-in: Windows shows its own flyout too.
    public bool PopMusic { get; set; } = true;
    public bool PopTimers { get; set; } = true;
    public bool PopDownloads { get; set; } = true;
    public bool PopDevices { get; set; } = true;
    public bool PopVolume { get; set; }
    public bool PopClipboard { get; set; } = true;

    public bool Allows(Pop kind) => kind switch
    {
        Pop.Music => PopMusic,
        Pop.Timers => PopTimers,
        Pop.Downloads => PopDownloads,
        Pop.Devices => PopDevices,
        Pop.Volume => PopVolume,
        _ => PopClipboard,
    };

    public static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Atoll", "settings.json");

    // Writes the defaults out so there is something to edit by hand.
    public static void EnsureFile()
    {
        if (File.Exists(FilePath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(new Settings(), new JsonSerializerOptions { WriteIndented = true }));
    }

    public static Settings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return new();
        }
    }
}
