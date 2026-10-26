using System.Diagnostics;
using Microsoft.UI.Dispatching;
using static Atoll.Native;

namespace Atoll;

// Fullscreen games (and any fullscreen app, a movie included): nothing pops up, alerts are held
// and summarised once on exit. The user picks what Alt+Q does there: nothing, or a small music card.
public sealed partial class IslandWindow
{
    readonly List<Alert> _held = [];
    readonly HashSet<string> _asked = []; // asked once per game per run, even if left unanswered
    DispatcherQueueTimer? _gameWatch;
    string? _game;   // executable of the fullscreen app in front
    string? _asking; // game whose "first time" question is on screen
    int _cardToken;
    bool _hotkeyOn = true;

    // null while a game set to "Decide per game" has not been decided yet: treated as Quiet.
    GameMode? GamePolicy => _game is null ? null
        : _settings.Games != GameMode.Ask ? _settings.Games
        : _settings.PerGame.TryGetValue(_game, out var m) ? m : null;

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
        if (_settings.Games == GameMode.Ask && GamePolicy is null && _asked.Add(exe)) _asking = exe;
        UpdateHotkey();
        SetMode(Mode.Hidden);
        // Answered with the keyboard: games usually capture the mouse, so a click may never reach the island.
        if (_asking != null)
            Notify(new Alert(Pop.Music, "", $"First time in {exe}", "Press Alt+Q for music controls here, or Atoll stays out",
                Seconds: 8, Wide: true, Expired: () => Remember(exe, GameMode.Quiet)), force: true);
    }

    void Remember(string exe, GameMode mode)
    {
        _asking = null;
        _settings.PerGame[exe] = mode;
        _settings.Save();
        UpdateHotkey();
    }

    // Alt+Q while the question shows: music it is, and the card opens right away as proof.
    void AnswerMusic()
    {
        var exe = _asking!;
        _alertToken++;
        _alert = null;
        Remember(exe, GameMode.Music);
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

    // Outside games, Alt+Q is a regular hotkey. In a game where it belongs to Atoll (music card, or the
    // first-time question), a keyboard hook reads it, since games often disable hotkeys. Quiet games
    // get Alt+Q back entirely.
    void UpdateHotkey()
    {
        bool hotkey = _game is null;
        bool hook = _game != null && (GamePolicy == GameMode.Music || _asking == _game);
        if (hook) GameKeys.Start(() => DispatcherQueue.TryEnqueue(Toggle));
        else GameKeys.Stop();
        if (hotkey == _hotkeyOn) return;
        _hotkeyOn = hotkey;
        if (hotkey) RegisterHotKey(_hwnd, HotkeyId, MOD_ALT | MOD_NOREPEAT, 'Q');
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

    void FillGameCard()
    {
        GameTitle.Text = _media.HasSession ? _media.Title : "Nothing playing";
        GameArtist.Text = _media.Artist;
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
