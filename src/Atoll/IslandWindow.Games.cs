using System.Diagnostics;
using Microsoft.UI.Dispatching;
using static Atoll.Native;

namespace Atoll;

// Fullscreen games (and any fullscreen app, a movie included): nothing pops up, alerts are held
// and summarised once on exit. The user picks what Alt+Q does there: nothing, or a small music card.
public sealed partial class IslandWindow
{
    readonly List<Alert> _held = [];
    DispatcherQueueTimer? _gameWatch;
    string? _game; // executable of the fullscreen app in front
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
        UpdateHotkey();
        SetMode(Mode.Hidden);
        if (_settings.Games == GameMode.Ask && GamePolicy is null)
            Notify(new Alert(Pop.Music, "", $"First time in {exe}", "Atoll remembers your choice", Seconds: 12,
                Action2Label: "Stay out", Action2: () => Remember(exe, GameMode.Quiet),
                ActionLabel: "Music on Alt+Q", Action: () => Remember(exe, GameMode.Music)), force: true);
    }

    void Remember(string exe, GameMode mode)
    {
        _settings.PerGame[exe] = mode;
        _settings.Save();
        UpdateHotkey();
    }

    void LeaveGame()
    {
        _game = null;
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

    // Quiet games get Alt+Q back: Atoll lets go of the shortcut instead of swallowing it.
    void UpdateHotkey()
    {
        bool want = _game is null || GamePolicy == GameMode.Music;
        if (want == _hotkeyOn) return;
        _hotkeyOn = want;
        if (want) RegisterHotKey(_hwnd, HotkeyId, MOD_ALT | MOD_NOREPEAT, 'Q');
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
        try { return Process.GetProcessById((int)pid).ProcessName; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return null; } // exited meanwhile
    }
}
