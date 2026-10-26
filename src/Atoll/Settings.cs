using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atoll;

enum Pop { Music, Timers, Downloads, Devices, Volume, Clipboard }

// During fullscreen games: hidden with the shortcut off, music card on the shortcut, or ask once per game.
enum GameMode { Quiet, Music, Ask }

// User preferences in %LOCALAPPDATA%\Atoll\settings.json, written by the setup and settings window.
sealed class Settings
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public bool StartWithWindows { get; set; } = true;

    // The shortcut that opens the island (RegisterHotKey modifiers and virtual key).
    public uint HotkeyModifiers { get; set; } = Hotkey.Alt;
    public uint HotkeyKey { get; set; } = 'Q';
    [System.Text.Json.Serialization.JsonIgnore] public string HotkeyName => Hotkey.Name(HotkeyModifiers, HotkeyKey);

    // Off by default: unlike a Mac notch, a resting pill covers real content (browser tabs).
    public bool ShowCompactPill { get; set; }

    // What may pop up on its own. Volume is opt-in: Windows shows its own flyout too.
    public bool PopMusic { get; set; } = true;
    public bool PopTimers { get; set; } = true;
    public bool PopDownloads { get; set; } = true;
    public bool PopDevices { get; set; } = true;
    public bool PopVolume { get; set; }
    public bool PopClipboard { get; set; } = true;

    public GameMode Games { get; set; } = GameMode.Quiet;
    public Dictionary<string, GameMode> PerGame { get; set; } = []; // executable name -> Quiet or Music, for Ask

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

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }

    public Settings Clone() => JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(this, Json), Json)!;

    public static bool FirstRun => !File.Exists(FilePath);

    public static Settings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return new();
        }
    }
}
