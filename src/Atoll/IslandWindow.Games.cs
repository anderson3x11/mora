using System.Diagnostics;
using Microsoft.UI.Dispatching;
using static Atoll.Native;

namespace Atoll;

// Fullscreen games (and any fullscreen app, a movie included): nothing pops up, alerts are held
// and summarised once on exit. The user picks what the shortcut does there: nothing, or a small music card.
public sealed partial class IslandWindow
{
    readonly List<Alert> _held = [];
    readonly HashSet<string> _asked = []; // asked once per game per run, even if left unanswered
    DispatcherQueueTimer? _gameWatch;
    string? _game;   // executable of the fullscreen app in front
    string? _asking; // game whose "first time" question is on screen
    int _cardToken;
    bool _hotkeyOn = true;

    // The rules for the game in front: its own profile, else the one for any game.
    GameProfile? Profile => _game is null ? null : _settings.GameProfiles.GetValueOrDefault(_game) ?? _settings.AnyGame;

    void WatchGames()
    {
        _gameWatch = DispatcherQueue.CreateTimer();
        _gameWatch.Interval = TimeSpan.FromSeconds(1.5); // ponytail: polling; a foreground WinEvent hook would react faster
        _gameWatch.Tick += (_, _) =>
        {
            var exe = FullscreenApp();
            if (exe == _game) return;
            if (_game != null) LeaveGame();
            if (exe != null) EnterGame(exe);
        };
        _gameWatch.Start();
    }

    void EnterGame(string exe)
    {
        _game = exe;
        _alerts.Clear();
        _alertToken++;
        _alert = null;
        if (!_settings.SeenGames.Contains(exe))
        {
            _settings.SeenGames.Add(exe); // listed in Settings from now on, ready to customize
            _settings.Save();
        }
        if (_settings.AskNewGames && !_settings.GameProfiles.ContainsKey(exe) && _asked.Add(exe)) _asking = exe;
        UpdateHotkey();
        SetMode(Mode.Hidden);
        // Answered with the keyboard: games usually capture the mouse, so a click may never reach the island.
        if (_asking != null)
            Notify(new Alert(Pop.Music, "", $"First time in {exe}", $"Press {_settings.HotkeyName} for music controls here, or Atoll stays out",
                Seconds: 8, Wide: true, Expired: () => Remember(exe, musicCard: false)), force: true);
    }

    // The answer to the first-time question becomes the game's own profile, editable in Settings.
    void Remember(string exe, bool musicCard)
    {
        _asking = null;
        _settings.GameProfiles[exe] = new() { MusicCard = musicCard };
        _settings.Save();
        UpdateHotkey();
    }

    // The shortcut while the question shows: music it is, and the card opens right away as proof.
    void AnswerMusic()
    {
        var exe = _asking!;
        _alertToken++;
        _alert = null;
        Remember(exe, musicCard: true);
        ToggleGameCard();
    }

    void LeaveGame()
    {
        _game = null;
        _asking = null; // unanswered: stays out for this run, asked again next time Atoll starts
        UpdateHotkey();
        if (_held.Count == 0)
        {
            if (_mode != Mode.Hidden) Settle();
            return;
        }
        var held = _held.ToList();
        _held.Clear();
        Notify(new Alert(Pop.Music, "", $"{held.Count} update{(held.Count > 1 ? "s" : "")} while you played",
            string.Join(" · ", held.Select(a => a.Title)), Seconds: 5), force: true);
    }

    // Outside games, the shortcut is a regular hotkey. In a game where it belongs to Atoll (music card, or the
    // first-time question), a keyboard hook reads it, since games often disable hotkeys. Quiet games
    // get the shortcut back entirely.
    void UpdateHotkey()
    {
        bool hotkey = _game is null;
        bool hook = _game != null && (Profile!.MusicCard || _asking == _game);
        if (hook) GameKeys.Start(() => DispatcherQueue.TryEnqueue(Toggle), vk => DispatcherQueue.TryEnqueue(() => OnCardArrow(vk)),
            _settings.HotkeyModifiers, _settings.HotkeyKey);
        else GameKeys.Stop();
        if (hotkey == _hotkeyOn) return;
        _hotkeyOn = hotkey;
        if (hotkey) RegisterHotKey(_hwnd, HotkeyId, _settings.HotkeyModifiers | MOD_NOREPEAT, _settings.HotkeyKey);
        else UnregisterHotKey(_hwnd, HotkeyId);
    }

    void ToggleGameCard()
    {
        if (_mode == Mode.Game)
        {
            Settle();
            return;
        }
        SetMode(Mode.Game);
        ArmGameCard();
    }

    // Closes by itself 4 s after the last interaction; never while the pointer is on it.
    async void ArmGameCard()
    {
        var t = ++_cardToken;
        await Task.Delay(4000);
        while (t == _cardToken && _mode == Mode.Game && GetCursorPos(out var pt) != 0 && OnPill(pt))
            await Task.Delay(700);
        if (t == _cardToken && _mode == Mode.Game) Settle();
    }

    // Arrow keys while the card shows: right next track, left back to the start (previous track in
    // the first 3 seconds), up and down the Windows volume. Each one keeps the card up a bit longer.
    async void OnCardArrow(uint vk)
    {
        if (_mode != Mode.Game) return;
        ArmGameCard();
        switch (vk)
        {
            case 0x27: _media.Next(); return;
            case 0x25:
                if (_media.Duration > TimeSpan.Zero && _media.Position > TimeSpan.FromSeconds(3)) _media.Seek(0);
                else _media.Previous();
                return;
        }
        if (_audio is null) return;
        _audio.Level = Math.Clamp(MathF.Round(_audio.Level * 20) / 20 + (vk == 0x26 ? 0.05f : -0.05f), 0, 1);
        var t = ++_cardNoteToken;
        _cardNote = $"Volume {Math.Round(_audio.Level * 100)}";
        FillGameCard();
        await Task.Delay(1500);
        if (t != _cardNoteToken) return;
        _cardNote = null;
        FillGameCard();
    }

    string? _cardNote; // shown instead of the artist for a moment (volume level)
    int _cardNoteToken;

    void FillGameCard()
    {
        GameTitle.Text = _media.HasSession ? _media.Title : "Nothing playing";
        GameArtist.Text = _cardNote ?? _media.Artist;
        GamePlayIcon.Glyph = PlayIcon.Glyph;
        SetArt(GameArt, GameArtIcon);
        ShapeArt(GameArt, GameArtColumn, 45);
    }

    // The foreground window fills its whole monitor without being a maximized window:
    // exclusive or borderless fullscreen games, fullscreen video, presentations.
    static string? FullscreenApp()
    {
        var fg = GetForegroundWindow();
        if (fg == 0 || IsZoomed(fg) != 0 || GetWindowRect(fg, out var r) == 0) return null;
        string name;
        unsafe
        {
            char* cls = stackalloc char[32];
            name = new string(cls, 0, GetClassName(fg, cls, 32));
        }
        if (name is "Progman" or "WorkerW" or "Shell_TrayWnd") return null; // the desktop is "fullscreen" too
        var info = new MONITORINFO { Size = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromWindow(fg, 2), ref info); // MONITOR_DEFAULTTONEAREST
        var m = info.Monitor;
        if (r.Left > m.Left || r.Top > m.Top || r.Right < m.Right || r.Bottom < m.Bottom) return null;
        GetWindowThreadProcessId(fg, out var pid);
        if (pid == Environment.ProcessId) return null;
        try
        {
            var exe = Process.GetProcessById((int)pid).ProcessName;
            return exe == "LockApp" ? null : exe; // the lock screen fills the monitor too
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return null; } // exited meanwhile
    }
}
