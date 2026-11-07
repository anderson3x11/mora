using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mora;

enum Pop { Music, Timers, Downloads, Devices, Volume, Clipboard, Privacy, Notifications, Agents }

// The game setting before per-game profiles; only read to convert old settings files.
enum GameMode { Quiet, Music, Ask }

// What Mora does during one fullscreen game (or any game without its own profile).
sealed class GameProfile
{
    public bool MusicCard { get; set; }        // the shortcut opens music controls; otherwise the game keeps it
    public List<Pop> Pops { get; set; } = []; // what may pop up over the game; the rest waits for the summary

    public bool Shows(Pop kind) => Pops.Contains(kind);
    public GameProfile Clone() => new() { MusicCard = MusicCard, Pops = [.. Pops] };
}

public enum ClockStyle { TimeAndDate, Time, TimeWithSeconds, FullDate }

// User preferences in %LOCALAPPDATA%\Mora\settings.json, written by the setup and settings window.
sealed class Settings
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public bool StartWithWindows { get; set; } = true;

    // The shortcut that opens the island (RegisterHotKey modifiers and virtual key).
    public uint HotkeyModifiers { get; set; } = Hotkey.Alt;
    public uint HotkeyKey { get; set; } = Hotkey.KeyAboveTab;
    [System.Text.Json.Serialization.JsonIgnore] public string HotkeyName => Hotkey.Name(HotkeyModifiers, HotkeyKey);

    // Off by default: unlike a Mac notch, a resting pill covers real content (browser tabs).
    public bool ShowCompactPill { get; set; }
    public bool ShowTimerPill { get; set; } // a small countdown while a timer runs, the bubble beside music

    // What may pop up on its own. Volume is opt-in: Windows shows its own flyout too.
    public bool PopMusic { get; set; } = true;
    public bool PopTimers { get; set; } = true;
    public bool PopDownloads { get; set; } = true;
    public bool PopDevices { get; set; } = true;
    public bool PopVolume { get; set; }
    public bool PopClipboard { get; set; } = true;
    public bool PopPrivacy { get; set; } = true; // an app starts using the microphone or camera
    public bool PopAgents { get; set; } = true;  // a coding agent needs you, or finished a long task
    public bool ConnectClaudeCode { get; set; }
    public bool MascotRoams { get; set; } = true; // the crab walks along the open panel's bottom edge  // Mora's hooks are in Claude Code's settings
    public bool PlaySounds { get; set; } = true; // a ring for timers, a note for app notifications
    public int SoundVolume { get; set; } = 90;   // 0 to 100, of each sound's loudest without clipping
    public Dictionary<string, string> NotifyApps { get; set; } = []; // app id → name: their notifications show in the island

    // Games: one profile for any game, own profiles by executable name, and every game seen so far
    // (listed in Settings so it can be customized later).
    public GameProfile AnyGame { get; set; } = new();
    public bool AskNewGames { get; set; }
    public Dictionary<string, GameProfile> GameProfiles { get; set; } = [];
    public string Theme { get; set; } = "Island";
    public bool ReopenLastTab { get; set; } = true; // off: the panel always opens on Media
    public bool DoNotDisturb { get; set; }      // the moon in the panel: pop-ups wait until it is off
    public bool ShowLyrics { get; set; }        // the Media tab's lyrics button, remembered
    public bool ShowWeather { get; set; } = true; // the Home tab's weather card (asks Open-Meteo, with a rounded location)
    public string CalendarLink { get; set; } = ""; // an .ics address for the Home tab's agenda, besides Windows' calendars
    public bool PrimaryScreenOnly { get; set; } // off: the island follows the screen of the active window
    // The clock: where it shows, and how.
    public bool ShowClockPill { get; set; }         // a resting pill with the time, off like the music pill
    public bool ClockBesidePanel { get; set; } = true;
    public bool ClockInPanel { get; set; } = true;
    public bool ClockInGameCard { get; set; } = true;
    public ClockStyle ClockFormat { get; set; } = ClockStyle.TimeAndDate;
    public List<string> SeenGames { get; set; } = [];
    public int GameListVersion { get; set; }

    // Before per-game profiles. Converted on load, then no longer written.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public GameMode? Games { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, GameMode>? PerGame { get; set; }

    void Upgrade()
    {
        if (Games is { } mode)
        {
            AnyGame.MusicCard = mode == GameMode.Music;
            AskNewGames = mode == GameMode.Ask;
            Games = null;
        }
        if (PerGame != null)
        {
            foreach (var (exe, m) in PerGame) GameProfiles.TryAdd(exe, new() { MusicCard = m == GameMode.Music });
            PerGame = null;
        }
        foreach (var exe in GameProfiles.Keys)
            if (!SeenGames.Contains(exe)) SeenGames.Add(exe);
        if (GameListVersion < 1) // once: any fullscreen app used to count as a game (Snipping Tool, Explorer)
        {
            var known = KnownGames.ByWindows().Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
            SeenGames.RemoveAll(exe => !GameProfiles.ContainsKey(exe) && !known.Contains(exe));
            GameListVersion = 1;
        }
    }

    public bool Allows(Pop kind) => kind switch
    {
        Pop.Music => PopMusic,
        Pop.Timers => PopTimers,
        Pop.Downloads => PopDownloads,
        Pop.Devices => PopDevices,
        Pop.Volume => PopVolume,
        Pop.Privacy => PopPrivacy,
        Pop.Notifications => true, // chosen per app
        Pop.Agents => PopAgents,

        _ => PopClipboard,
    };

    // MORA_DATA points tests and development runs at a folder of their own, so they never touch the
    // user's settings, shelf or error log.
    public static readonly string FilePath = Path.Combine(
        Environment.GetEnvironmentVariable("MORA_DATA") is { Length: > 0 } dir ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mora"),
        "settings.json");

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }

    public Settings Clone() => JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(this, Json), Json)!;

    public static bool FirstRun => !File.Exists(FilePath);

    // The app was called Atoll: its folder (settings, shelf, error log) moves over once, before anything reads it.
    public static void MoveFromAtoll()
    {
        if (Environment.GetEnvironmentVariable("MORA_DATA") is { Length: > 0 }) return;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string atoll = Path.Combine(local, "Atoll"), mora = Path.GetDirectoryName(FilePath)!;
        if (!Directory.Exists(atoll) || Directory.Exists(mora)) return;
        try { Directory.Move(atoll, mora); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // Atoll still running: next launch
    }

    public static Settings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new();
            settings.Upgrade();
            return settings;
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return new();
        }
    }
}
