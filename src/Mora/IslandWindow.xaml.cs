using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using static Mora.Native;

namespace Mora;

// The island never activates: WS_EX_NOACTIVATE + MA_NOACTIVATE, shown with SW_SHOWNOACTIVATE,
// and the window region follows the pill so everything around it is click-through.
public sealed partial class IslandWindow : Window
{
    // Fixed window canvas in DIPs: room for the largest state plus spring overshoot.
    const double CanvasW = 560, CanvasH = 300;
    const int HotkeyId = 1;
    static readonly (double K, double C) Reduced = (900, 60);
    (double K, double C) OpenSpring => _theme.Open;
    (double K, double C) CloseSpring => _theme.Close;

    enum Mode { Hidden, Compact, Alert, Game, Expanded }
    enum Via { None, Hotkey, Click, Hover, Drag }
    record struct Geo(double W, double H, double R, double Y);

    enum Tab { Media, Timers, Shelf, Clipboard }

    // Sizes are the same in every theme; the theme sets the corners and where the island sits.
    Geo GeoOf(Mode m)
    {
        var (w, h) = m switch
        {
            Mode.Compact => (MediaPill ? 214 : 132, 32),
            Mode.Alert => (_alert is { Wide: true } || _alert?.Action2Label != null ? 384 : 304, 50), // longer text or two answers
            Mode.Game => (288, 66),
            Mode.Expanded => (430, _tab switch
            {
                Tab.Media => 170,
                Tab.Timers => 124,
                Tab.Shelf => 54 + 31 * Math.Clamp(_shelf.Count, 2, 4),           // the empty drop zone takes two rows
                _ => 54 + 31 * Math.Clamp(_clipboard?.Items.Count ?? 0, 1, 4), // one 26 px row + 5 px gap per copy
            }),
            _ => (96, 24),
        };
        double r = _theme.Radius(Math.Min(h / 2.0, 26));
        if (m != Mode.Hidden) return new(w, h, r, _theme.Top);
        // Back into the edge (aiming past zero, so it is gone at once rather than creeping toward it), or up and out of sight.
        return _theme.Attached ? new(150, -12, r, 0) : new(w, h, r, -40);
    }

    static IslandWindow _self = null!; // one island; native callbacks route here
    readonly nint _hwnd;
    readonly Spring _w, _h, _r, _y;
    readonly bool _motion = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
    readonly Stopwatch _clock = new();
    readonly Media _media;
    Settings _settings = Settings.Load();
    SetupWindow? _setup; // first-run setup or the settings window, while open
    readonly Tray _tray;
    readonly IReadOnlyList<(string Label, Action Run)> _trayMenu;
    readonly List<Visual> _wave = [];
    readonly DispatcherQueueTimer _waveTimer;
    readonly DispatcherQueueTimer _progressTimer;
    Theme _theme = Theme.Island;
    (double K, double C) _spring = Theme.Island.Open;
    double _last, _scale = 1;
    bool _animating;
    Mode _mode;
    Via _via;
    Tab _tab;
    int _token, _hoverToken, _winX, _winY;
    nint _hook, _monitor;
    RECT _hit; // physical px, window-relative

    public IslandWindow()
    {
        InitializeComponent();
        BuildShape();
        _self = this;
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var g = GeoOf(Mode.Hidden);
        (_w, _h, _r, _y) = (new(g.W), new(g.H), new(g.R), new(g.Y));
        SystemBackdrop = new TransparentBackdrop();
        Title = "Mora";

        var p = OverlappedPresenter.Create();
        p.SetBorderAndTitleBar(false, false);
        p.IsResizable = p.IsMaximizable = p.IsMinimizable = false;
        p.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(p);
        AppWindow.IsShownInSwitchers = false;

        var ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST) & ~WS_EX_APPWINDOW);
        int v = 1; // DWMWCP_DONOTROUND
        DwmSetWindowAttribute(_hwnd, 33, ref v, 4);
        v = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        DwmSetWindowAttribute(_hwnd, 34, ref v, 4);
        var glass = new RECT { Left = -1, Top = -1, Right = -1, Bottom = -1 }; // MARGINS: whole window
        DwmExtendFrameIntoClientArea(_hwnd, ref glass);

        unsafe { SetWindowSubclass(_hwnd, (nint)(delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint>)&WndProc, 1, 0); }
        RegisterHotKey(_hwnd, HotkeyId, _settings.HotkeyModifiers | MOD_NOREPEAT, _settings.HotkeyKey);

        Place();
        SetWindowRgn(_hwnd, CreateRectRgn(0, 0, 0, 0), 1); // click-through from the start; Render only updates on change
        Render();
        ShowWindow(_hwnd, SW_SHOWNOACTIVATE);

        _progressTimer = DispatcherQueue.CreateTimer();
        _progressTimer.Interval = TimeSpan.FromSeconds(1);
        _progressTimer.Tick += (_, _) => UpdateProgress();
        _waveTimer = DispatcherQueue.CreateTimer();
        BuildWave();
        ApplyTheme(); // also shows the tab and fills the timer
        _clipboard = new ClipboardHistory(_hwnd);
        FillClipboard();
        LoadShelf();
        FillShelf();
        WatchDrags();
        WatchGames();
        _media = new Media(DispatcherQueue);
        _media.Changed += OnMediaChanged;
        _ = _media.StartAsync();
        WireAlerts();
        _tray = new Tray(_hwnd, $"Mora ({_settings.HotkeyName})");
        _trayMenu =
        [
            ("Open Mora", () => SetMode(Mode.Expanded, Via.Click)),
            ("Settings…", () => OpenSetup(firstRun: false)),
            ("Quit Mora", () =>
            {
                _tray.Remove();
                HookMouse(false);
                Application.Current.Exit();
            }),
        ];
        if (Settings.FirstRun) OpenSetup(firstRun: true);
        else Startup.Apply(_settings.StartWithWindows); // keeps the entry right if the app was moved or updated
    }

    // The island stays hidden and quiet while setup or settings are open.
    void OpenSetup(bool firstRun)
    {
        if (_setup != null)
        {
            _setup.Activate(); // the user asked for it from the tray
            return;
        }
        SetMode(Mode.Hidden);
        _setup = SetupWindow.Open(_settings, firstRun, chosen =>
        {
            var old = _settings;
            _settings = chosen;
            if ((old.HotkeyModifiers, old.HotkeyKey) != (chosen.HotkeyModifiers, chosen.HotkeyKey) && _hotkeyOn)
            {
                UnregisterHotKey(_hwnd, HotkeyId);
                if (RegisterHotKey(_hwnd, HotkeyId, chosen.HotkeyModifiers | MOD_NOREPEAT, chosen.HotkeyKey) == 0)
                {
                    // Taken since it was picked: keep the old shortcut rather than none.
                    (chosen.HotkeyModifiers, chosen.HotkeyKey) = (old.HotkeyModifiers, old.HotkeyKey);
                    RegisterHotKey(_hwnd, HotkeyId, old.HotkeyModifiers | MOD_NOREPEAT, old.HotkeyKey);
                    Notify(new Alert(Pop.Music, "", "Shortcut already in use", $"Mora keeps {old.HotkeyName}", Seconds: 4), force: true);
                }
            }
            _tray.SetTip($"Mora ({_settings.HotkeyName})");
            _settings.Save();
            if (old.Theme != chosen.Theme) ApplyTheme();
            Startup.Apply(_settings.StartWithWindows);
            UpdateHotkey();
            OnMediaChanged(); // the resting pill may have been switched on or off
            if (firstRun)
                Notify(new Alert(Pop.Music, "", $"Press {_settings.HotkeyName} any time", "Mora never takes your keyboard", Seconds: 5), force: true);
        });
        _setup.Closed += (_, _) => _setup = null;
    }

    // Top center of the monitor holding the active window.
    // ponytail: only re-checked when the island appears or opens; a resting pill does not follow focus yet.
    void Place()
    {
        var mon = MonitorFromWindow(GetForegroundWindow(), 1); // MONITOR_DEFAULTTOPRIMARY
        if (mon == _monitor) return;
        _monitor = mon;
        var info = new MONITORINFO { Size = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(mon, ref info);
        GetDpiForMonitor(mon, 0, out var dpi, out _);
        _scale = dpi / 96.0;
        int w = (int)Math.Ceiling(CanvasW * _scale), h = (int)Math.Ceiling(CanvasH * _scale);
        _winX = (info.Monitor.Left + info.Monitor.Right - w) / 2;
        _winY = info.Monitor.Top;
        SetWindowPos(_hwnd, HWND_TOPMOST, _winX, _winY, w, h, SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    void Toggle()
    {
        if (_setup != null) return;
        if (_game != null)
        {
            if (_asking == _game) AnswerMusic();
            else if (Profile!.MusicCard) ToggleGameCard();
            return;
        }
        if (_mode == Mode.Expanded)
        {
            Settle();
            return;
        }
        if (GetAsyncKeyState(0x01) < 0) // pressed mid-drag: open where the file can be dropped
        {
            _tab = Tab.Shelf;
            ShowTab();
        }
        SetMode(Mode.Expanded, Via.Hotkey);
    }

    // The resting pill, each part opt-in: music while media exists (paused included), a countdown while a
    // timer runs. Both at once split into the pill and a round bubble beside it. Never over a game.
    bool MediaPill => _settings.ShowCompactPill && _media is { HasSession: true };
    bool TimerPill => _settings.ShowTimerPill && _timer != null;
    bool RestingPill => (MediaPill || TimerPill) && _game is null;

    // After media or a timer changed: show, hide or refill the resting pill.
    void EvaluateRest()
    {
        if (_mode == Mode.Hidden && RestingPill) SetMode(Mode.Compact);
        else if (_mode == Mode.Compact && !RestingPill) SetMode(Mode.Hidden);
        else if (_mode == Mode.Compact)
        {
            Retarget(GeoOf(Mode.Compact), OpenSpring); // a timer alone is a shorter pill than music
            FillCompact();
        }
        UpdateBubble();
    }

    void FillCompact()
    {
        CompactMedia.Visibility = MediaPill ? Visibility.Visible : Visibility.Collapsed;
        CompactTimer.Visibility = MediaPill ? Visibility.Collapsed : Visibility.Visible;
        CompactLayer.Width = GeoOf(Mode.Compact).W;
        UpdateWave();
    }

    void UpdateBubble()
    {
        _bubble.T = _mode == Mode.Compact && MediaPill && TimerPill ? 1 : 0;
        if (_bubble.T != _bubble.X) Animate();
    }

    // Back to rest, unless alerts waited while the panel was open.
    void Settle()
    {
        if (_alerts.Count > 0) NextAlert();
        else SetMode(RestingPill ? Mode.Compact : Mode.Hidden);
    }

    // Growing: shape first, content fades in 140 ms later. Shrinking: content fades out first,
    // the shape follows 90 ms later, and the new content appears once it has mostly settled.
    async void SetMode(Mode mode, Via via = Via.None)
    {
        if (mode == Mode.Expanded && _game != null) return; // never a panel over a game, whatever asked for it
        var prev = _mode;
        var t = ++_token;
        (_mode, _via) = (mode, via);
        GameKeys.CardOpen = mode == Mode.Game;
        UpdateBubble();
        if (mode == Mode.Compact) FillCompact();
        if (prev == Mode.Hidden || mode == Mode.Expanded) Place();
        HookMouse(mode == Mode.Expanded);
        Fade(CompactLayer, false);
        Fade(AlertLayer, false);
        Fade(GameLayer, false);
        Fade(ExpandedLayer, false);
        UpdateWave();
        UpdateProgressTimer();

        Geo g = GeoOf(mode), pg = GeoOf(prev);
        if (g.W * g.H > pg.W * pg.H + 1)
        {
            Retarget(g, OpenSpring);
            await Task.Delay(140);
        }
        else
        {
            await Task.Delay(90);
            if (t != _token) return;
            Retarget(g, CloseSpring);
            await Task.Delay(170);
        }
        if (t != _token) return;
        if (mode == Mode.Compact) Fade(CompactLayer, true);
        if (mode == Mode.Expanded) Fade(ExpandedLayer, true);
        if (mode == Mode.Game)
        {
            FillGameCard();
            Fade(GameLayer, true);
        }
        if (mode == Mode.Alert)
        {
            FillAlert(); // swapped only now, so the previous alert fades out with its own text
            Fade(AlertLayer, true);
        }
    }

    void OnMediaChanged()
    {
        Fill();
        EvaluateRest();
    }

    void Fill()
    {
        bool has = _media.HasSession;
        NothingPlaying.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        MediaPane.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Text = _media.Title;
        ArtistText.Text = _media.Artist;
        PlayIcon.Glyph = _media.Playing ? "" : "";
        SetArt(CompactArt, CompactArtIcon);
        SetArt(ExpandedArt, ExpandedArtIcon);
        ShapeArt(ExpandedArt, ArtColumn, 58);
        SourceBadge.Text = _media.Source;
        FillGameCard();
        UpdateProgress();
        UpdateWave();
        UpdateProgressTimer();
    }

    void SetArt(Microsoft.UI.Xaml.Controls.Border frame, UIElement fallback)
    {
        frame.Background = _media.Art is { } art
            ? new ImageBrush { ImageSource = art, Stretch = Stretch.UniformToFill }
            : (Brush)((FrameworkElement)Content).Resources["Line"];
        fallback.Visibility = _media.Art is null ? Visibility.Visible : Visibility.Collapsed;
    }

    // The frame follows the artwork: wide for video thumbnails (YouTube), round for a Twitch
    // streamer's picture, square for album covers (Spotify) and anything else.
    void ShapeArt(Microsoft.UI.Xaml.Controls.Border frame, Microsoft.UI.Xaml.Controls.ColumnDefinition column, double height)
    {
        var art = _media.Art;
        bool wide = art is { PixelHeight: > 0 } && (double)art.PixelWidth / art.PixelHeight >= 1.3;
        bool round = !wide && art != null && _media.Site is "Twitch" or "Kick";
        double width = wide ? Math.Round(height * 16 / 9) : height;
        frame.Width = width;
        column.Width = new GridLength(width);
        frame.CornerRadius = new CornerRadius(round ? height / 2 : height * 0.19);
    }

    void UpdateProgress()
    {
        var d = _media.Duration;
        var p = _media.Position;
        ProgressRow.Visibility = d > TimeSpan.Zero ? Visibility.Visible : Visibility.Collapsed; // Firefox reports no timeline
        PositionText.Text = Format(p);
        DurationText.Text = Format(d);
        var f = d > TimeSpan.Zero ? p / d : 0;
        SeekDone.Width = new GridLength(f, GridUnitType.Star);
        SeekLeft.Width = new GridLength(1 - f, GridUnitType.Star);
    }

    static string Format(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    // Ticks only while someone can see the progress move.
    void UpdateProgressTimer()
    {
        if (_mode == Mode.Expanded && _media.Playing) _progressTimer.Start(); else _progressTimer.Stop();
    }

    // The bars follow what is actually playing: the output's peak level (a read of a shared value, no
    // audio capture), each bar showing it one tick after its left neighbour so beats travel across.
    // ponytail: stepped at 30 fps and only while visible bars move. Any per-vsync animation recomposes
    // the window every frame (~10% of a core at 240 Hz). The pill's springs keep full rate.
    const float BarRest = 0.22f;
    static readonly float[] BarGain = [0.85f, 1.15f, 1f, 0.75f];
    readonly float[] _barLevel = [BarRest, BarRest, BarRest, BarRest], _barHistory = new float[4];

    void BuildWave()
    {
        _wave.Clear();
        foreach (var panel in new[] { Wave, AlertWave, PanelWave })
            foreach (var bar in panel.Children)
            {
                var v = ElementCompositionPreview.GetElementVisual(bar);
                v.CenterPoint = new Vector3(1.5f, 8, 0);
                v.Scale = new Vector3(1, BarRest, 1);
                _wave.Add(v);
            }
        _waveTimer.Interval = TimeSpan.FromMilliseconds(33);
        _waveTimer.Tick += (_, _) => TickWave();
    }

    // The four bars of whichever waveform is on screen, if any.
    Span<Visual> VisibleBars()
    {
        int set = _mode switch
        {
            Mode.Compact when MediaPill => 0,
            Mode.Alert when _alert?.Kind == Pop.Music => 1,
            Mode.Expanded when _tab == Tab.Media => 2,
            _ => -1,
        };
        return set < 0 ? [] : System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_wave).Slice(set * 4, 4);
    }

    void TickWave()
    {
        var bars = VisibleBars();
        if (bars.IsEmpty) return;
        for (int i = 3; i > 0; i--) _barHistory[i] = _barHistory[i - 1];
        _barHistory[0] = MathF.Sqrt(_audio?.Peak ?? 0); // square root: closer to how loud it sounds
        for (int i = 0; i < 4; i++)
        {
            float target = BarRest + (1 - BarRest) * Math.Clamp(_barHistory[i] * BarGain[i], 0, 1);
            // Up at once on a beat, down smoothly after it.
            float level = target > _barLevel[i] ? target : _barLevel[i] * 0.8f + target * 0.2f;
            if (MathF.Abs(level - _barLevel[i]) < 0.005f) continue; // nothing to redraw: no composition frame
            _barLevel[i] = level;
            bars[i].Scale = new Vector3(1, level, 1);
        }
    }

    // The waveform runs only while one is on screen and music plays; paused bars sit low and still.
    void UpdateWave()
    {
        bool run = _motion && _media is { Playing: true } && !VisibleBars().IsEmpty; // _media is null during startup
        if (run == _waveTimer.IsRunning) return;
        if (run) _waveTimer.Start();
        else
        {
            _waveTimer.Stop();
            foreach (var v in _wave) v.Scale = new Vector3(1, BarRest, 1);
            Array.Fill(_barLevel, BarRest);
        }
    }

    async void Pill_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var t = ++_hoverToken;
        if (_mode != Mode.Compact || e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse) return;
        await Task.Delay(320);
        // XAML can report a stale "entered" right after the pill shrinks away from the cursor.
        if (t == _hoverToken && _mode == Mode.Compact && GetCursorPos(out var pt) != 0 && OnPill(pt))
            SetMode(Mode.Expanded, Via.Hover);
    }

    async void Pill_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var t = ++_hoverToken;
        if (_mode != Mode.Expanded || _via != Via.Hover) return;
        await Task.Delay(450);
        if (t == _hoverToken && _mode == Mode.Expanded && _via == Via.Hover) Settle();
    }

    void Pill_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (_mode == Mode.Game) ArmGameCard(); // any click on the card counts as interaction
        else if (_mode is Mode.Compact or Mode.Alert && _game is null) SetMode(Mode.Expanded, Via.Click);
    }

    void Tab_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        var before = GeoOf(Mode.Expanded);
        _tab = Enum.Parse<Tab>((string)((FrameworkElement)sender).Tag);
        ShowTab();
        var after = GeoOf(Mode.Expanded);
        Retarget(after, after.H > before.H ? OpenSpring : CloseSpring);
    }

    void ShowTab()
    {
        MediaTab.Visibility = _tab == Tab.Media ? Visibility.Visible : Visibility.Collapsed;
        TimersTab.Visibility = _tab == Tab.Timers ? Visibility.Visible : Visibility.Collapsed;
        ShelfTab.Visibility = _tab == Tab.Shelf ? Visibility.Visible : Visibility.Collapsed;
        ClipboardTab.Visibility = _tab == Tab.Clipboard ? Visibility.Visible : Visibility.Collapsed;
        SourceBadge.Visibility = _tab == Tab.Media ? Visibility.Visible : Visibility.Collapsed;
        UpdateWave(); // the panel's waveform only lives on the Media tab
        var res = ((FrameworkElement)Content).Resources;
        foreach (var t in Tabs.Children.Cast<Microsoft.UI.Xaml.Controls.Grid>())
        {
            bool on = (string)t.Tag == _tab.ToString();
            t.Background = on ? (Brush)res["Raised"] : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            var fg = (Brush)res[on ? "Fg" : "Muted"];
            var label = (Microsoft.UI.Xaml.Controls.StackPanel)t.Children[0];
            ((Microsoft.UI.Xaml.Controls.FontIcon)label.Children[0]).Foreground = fg;
            ((Microsoft.UI.Xaml.Controls.TextBlock)label.Children[1]).Foreground = fg;
        }
    }

    void Play_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => _media.TogglePlay();
    void Next_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => _media.Next();
    void Previous_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => _media.Previous();

    void Seek_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) =>
        _media.Seek(Math.Clamp(e.GetPosition(SeekTrack).X / SeekTrack.ActualWidth, 0, 1));

    void Button_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        ((Microsoft.UI.Xaml.Controls.Panel)sender).Background = (Brush)((FrameworkElement)Content).Resources["Raised"];

    void Button_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        ((Microsoft.UI.Xaml.Controls.Panel)sender).Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    // Retargets from the current position and velocity, never from zero.
    void Retarget(Geo g, (double K, double C) spring)
    {
        (_w.T, _h.T, _r.T, _y.T) = (g.W, g.H, g.R, g.Y);
        _spring = _motion ? spring : Reduced;
        Animate();
    }

    void Animate()
    {
        if (_animating) return;
        _animating = true;
        _last = 0;
        _clock.Restart();
        CompositionTarget.Rendering += OnRendering;
    }

    void OnRendering(object? sender, object e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = Math.Min(0.034, now - _last);
        _last = now;
        if (dt <= 0) return;
        int n = (int)Math.Ceiling(dt * 240);
        var (k, c) = _spring;
        bool moving = _w.Step(k, c, dt, n) | _h.Step(k, c, dt, n) | _r.Step(k, c, dt, n) | _y.Step(k, c, dt, n)
            | _bubble.Step(_motion ? 300 : 900, _motion ? 18 : 60, dt, n); // underdamped: the bubble pops in with a bounce
        Render();
        if (moving) return;
        CompositionTarget.Rendering -= OnRendering;
        _animating = false;
        Render(); // exact hit region again, now that everything rests
    }

    // The black shape is a compositor rounded rectangle: changing it touches no XAML layout, which at
    // 240 frames a second was most of an animation's cost. The same geometry clips the layers.
    // The split bubble is a second, round one beside it.
    // An attached theme adds two shoulders: small squares at the island's top corners, filled outside a
    // quarter circle, so the edge of the screen curves down into the island like a notch.
    CompositionRoundedRectangleGeometry? _shape, _bubbleShape;
    readonly Spring _bubble = new(0); // bubble scale, 0 hidden, 1 shown
    CompositionColorBrush? _surface, _border;
    readonly List<CompositionSpriteShape> _fills = [];
    readonly (ShapeVisual Visual, CompositionEllipseGeometry Arc, CompositionSpriteShape Ring)[] _shoulders = new (ShapeVisual, CompositionEllipseGeometry, CompositionSpriteShape)[2];
    CompositionGeometricClip? _layersClip;
    const float ShoulderMax = 10;

    void BuildShape()
    {
        var c = ElementCompositionPreview.GetElementVisual(Pill).Compositor;
        _surface = c.CreateColorBrush(Theme.Island.Surface);
        _border = c.CreateColorBrush(Microsoft.UI.Colors.Transparent);
        _shape = c.CreateRoundedRectangleGeometry();
        _bubbleShape = c.CreateRoundedRectangleGeometry();
        var root = c.CreateContainerVisual();
        var visual = c.CreateShapeVisual();
        visual.Size = new Vector2((float)CanvasW, (float)CanvasH);
        foreach (var geometry in new[] { _shape, _bubbleShape })
        {
            var fill = c.CreateSpriteShape(geometry);
            fill.FillBrush = _surface;
            fill.StrokeBrush = _border;
            visual.Shapes.Add(fill);
            _fills.Add(fill);
        }
        root.Children.InsertAtTop(visual);
        for (int i = 0; i < 2; i++)
        {
            var arc = c.CreateEllipseGeometry();
            var ring = c.CreateSpriteShape(arc);
            ring.StrokeBrush = _surface;
            var shoulder = c.CreateShapeVisual();
            shoulder.Shapes.Add(ring);
            shoulder.Clip = c.CreateInsetClip(); // only the square: the ring's other three quarters stay hidden
            root.Children.InsertAtTop(shoulder);
            _shoulders[i] = (shoulder, arc, ring);
        }
        ElementCompositionPreview.SetElementChildVisual(ShapeHost, root);
        _layersClip = c.CreateGeometricClip(_shape);
        ElementCompositionPreview.GetElementVisual(Layers).Clip = _layersClip;
        ElementCompositionPreview.SetIsTranslationEnabled(BubbleText, true);
    }

    // Colors, surface, corners, springs and the layout details that differ between themes.
    void ApplyTheme()
    {
        var t = _theme = Theme.Named(_settings.Theme);
        var res = ((FrameworkElement)Content).Resources;
        foreach (var (key, color) in new[] { ("Fg", t.Fg), ("Muted", t.Muted), ("Line", t.Line), ("Raised", t.Raised), ("Ink", t.Ink), ("InkText", t.InkText) })
            ((SolidColorBrush)res[key]).Color = color;
        _surface!.Color = t.Surface;
        _border!.Color = t.Border ?? Microsoft.UI.Colors.Transparent;
        foreach (var fill in _fills) fill.StrokeThickness = t.Border is null ? 0 : 1;
        Layers.Margin = new Thickness(0, t.Top, 0, 0);
        _layersClip!.Offset = new Vector2(0, (float)-t.Top); // the layers start where the island's top is
        foreach (var tab in Tabs.Children.Cast<Microsoft.UI.Xaml.Controls.Grid>())
            ((Microsoft.UI.Xaml.Controls.StackPanel)tab.Children[0]).Children[1].Visibility = t.IconTabs ? Visibility.Collapsed : Visibility.Visible;
        foreach (var bar in new Microsoft.UI.Xaml.Controls.Border[] { SeekBack, SeekFill, AlertBarTrack, AlertBarFill })
        {
            bar.Height = t.Track;
            bar.CornerRadius = new CornerRadius(t.Track / 2);
        }
        AlertBar.Height = t.Track;
        var g = GeoOf(_mode);
        (_w.X, _h.X, _r.X, _y.X) = (_w.T, _h.T, _r.T, _y.T) = (g.W, g.H, g.R, g.Y); // jump there: no spring between two looks
        ShowTab();
        FillTimer();
        Render();
    }

    void Render()
    {
        float w = (float)Math.Max(0, _w.X), h = (float)Math.Max(0, _h.X), r = (float)Math.Min(Math.Max(0, _r.X), h / 2), top = (float)_y.X;
        float left = ((float)CanvasW - w) / 2;
        if (_theme.Attached)
        {
            // Taller by its radius and raised by as much: the top corners sit above the screen, out of sight.
            r = (float)Math.Min(Math.Max(0, _r.X), h);
            _shape!.Size = new Vector2(w, h + r);
            _shape.Offset = new Vector2(left, top - r);
        }
        else
        {
            _shape!.Size = new Vector2(w, h);
            _shape.Offset = new Vector2(left, top);
        }
        _shape.CornerRadius = new Vector2(r, r);
        float side = _theme.Attached ? Math.Min(ShoulderMax, h / 2) : 0;
        for (int i = 0; i < 2; i++)
        {
            var (visual, arc, ring) = _shoulders[i];
            visual.Size = new Vector2(side, side);
            visual.Offset = new Vector3(i == 0 ? left - side : left + w, top, 0);
            // A ring from radius `side` out past the far corner, centered on the corner away from the island.
            arc.Center = new Vector2(i == 0 ? 0 : side, side);
            arc.Radius = new Vector2(side * 1.21f, side * 1.21f);
            ring.StrokeThickness = side * 0.42f + 1;
        }

        // Bubble: 32 px round, 6 px right of the pill, scaled from its center.
        float s = (float)Math.Max(0, _bubble.X), size = 32 * s;
        var center = new Vector2(((float)CanvasW + w) / 2 + 6 + 16, (float)_y.X + 16);
        _bubbleShape!.Size = new Vector2(size, size);
        _bubbleShape.CornerRadius = new Vector2(size / 2, size / 2);
        _bubbleShape.Offset = center - new Vector2(size / 2, size / 2);
        ElementCompositionPreview.GetElementVisual(BubbleText).Properties.InsertVector3("Translation", new Vector3(center.X - 16, center.Y - 16, 0));
        BubbleText.Opacity = Math.Clamp(s, 0, 1);

        // While moving, the hit region follows the shapes in 16 px steps, a little larger than them: Windows
        // hears about it a few times per animation instead of every frame. Exact again once all rests.
        var region = ShapeRect(w, h, _y.X, _animating ? 0.04 : 0);
        if (s > 0.01 && region.Bottom > 0)
            region.Right = Math.Max(region.Right, (int)Math.Ceiling((center.X + 16) * _scale));
        // The shoulders reach past the island at the top: a thin band of their own, or the region would cut them off.
        var band = side < 0.5f ? default : new RECT
        {
            Left = (int)Math.Floor((left - side) * _scale), Right = (int)Math.Ceiling((left + w + side) * _scale),
            Top = 0, Bottom = (int)Math.Ceiling((top + side) * _scale),
        };
        if (_animating && region.Bottom > 0)
            region = new RECT { Left = region.Left & ~15, Top = region.Top & ~15, Right = (region.Right + 15) & ~15, Bottom = (region.Bottom + 15) & ~15 };
        if (_animating && band.Bottom > 0)
            band = band with { Left = band.Left & ~15, Right = (band.Right + 15) & ~15 };
        _band = band;
        SetRegion(region);
    }

    RECT _band, _bandSet;

    // The shape's bounds in window pixels, grown by a fraction of its size on each side.
    RECT ShapeRect(double w, double h, double y, double grow)
    {
        w = Math.Max(0, w) * (1 + grow);
        h = Math.Max(0, h) * (1 + grow);
        var r = new RECT
        {
            Left = (int)Math.Floor((CanvasW - w) / 2 * _scale),
            Right = (int)Math.Ceiling((CanvasW + w) / 2 * _scale),
            Top = Math.Max(0, (int)Math.Floor(y * _scale)),
            Bottom = (int)Math.Ceiling((y + h) * _scale),
        };
        return r.Bottom <= r.Top ? default : r;
    }

    void SetRegion(RECT r)
    {
        _shapeHit = r;
        if (_dropStrip)
        {
            var s = DropStrip();
            r = r.Bottom <= r.Top ? s : new RECT { Left = Math.Min(r.Left, s.Left), Right = Math.Max(r.Right, s.Right), Bottom = Math.Max(r.Bottom, s.Bottom) };
        }
        if (r.Equals(_hit) && _band.Equals(_bandSet)) return;
        (_hit, _bandSet) = (r, _band);
        var rgn = CreateRectRgn(r.Left, r.Top, r.Right, r.Bottom);
        if (_band.Bottom > 0)
        {
            var band = CreateRectRgn(_band.Left, _band.Top, _band.Right, _band.Bottom);
            CombineRgn(rgn, rgn, band, 2); // RGN_OR
            DeleteObject(band);
        }
        SetWindowRgn(_hwnd, rgn, 1); // the window owns rgn from here
    }

    void Fade(UIElement layer, bool on)
    {
        var sb = new Storyboard();
        Animate(sb, layer, "Opacity", on ? 1 : 0, on ? 220 : 140);
        Animate(sb, layer.RenderTransform, "ScaleX", on ? 1 : 0.94, on ? 380 : 280);
        Animate(sb, layer.RenderTransform, "ScaleY", on ? 1 : 0.94, on ? 380 : 280);
        sb.Begin();
    }

    void Animate(Storyboard sb, DependencyObject target, string property, double to, int ms)
    {
        var a = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(_motion ? ms : 10),
            EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 },
        };
        Storyboard.SetTarget(a, target);
        Storyboard.SetTargetProperty(a, property);
        sb.Children.Add(a);
    }

    // Low-level mouse hook only while the panel is open, to close on a click outside it.
    // A click closes it on release, and only when pressed and released outside: a file dragged
    // from Explorer onto the open panel starts outside but ends on it, and must not close it.
    static bool _pressedOutside;

    void HookMouse(bool on)
    {
        if (on && _hook == 0)
            unsafe { _hook = SetWindowsHookEx(WH_MOUSE_LL, (nint)(delegate* unmanaged<int, nint, nint, nint>)&MouseProc, GetModuleHandle(0), 0); }
        else if (!on && _hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    bool OnPill(POINT pt)
    {
        int x = pt.X - _winX, y = pt.Y - _winY;
        return x >= _hit.Left && x < _hit.Right && y >= _hit.Top && y < _hit.Bottom;
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    static unsafe nint MouseProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var s = _self;
            bool outside = !s.OnPill(*(POINT*)lParam); // MSLLHOOKSTRUCT starts with the screen point
            if (wParam == WM_LBUTTONDOWN || wParam == WM_RBUTTONDOWN || wParam == WM_MBUTTONDOWN) _pressedOutside = outside;
            else if (wParam == WM_LBUTTONUP || wParam == WM_RBUTTONUP || wParam == WM_MBUTTONUP)
            {
                if (_pressedOutside && outside)
                    s.DispatcherQueue.TryEnqueue(() => { if (s._mode == Mode.Expanded) s.Settle(); });
                _pressedOutside = false;
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint data)
    {
        if (msg == WM_MOUSEACTIVATE) return (nint)MA_NOACTIVATE;
        if (msg == Tray.Message)
        {
            _self._tray.OnMessage(lParam, _self.Toggle, _self._trayMenu);
            return 0;
        }
        if (msg == Tray.TaskbarCreated) _self._tray.Add();
        if (msg == WM_COPYDATA && lParam != 0) // a --simulate command from a second instance
        {
            var copy = System.Runtime.InteropServices.Marshal.PtrToStructure<CopyData>(lParam);
            _self.Simulate(System.Runtime.InteropServices.Marshal.PtrToStringUni(copy.Data, copy.Bytes / 2) ?? "");
            return 1;
        }
        if (msg == ClipboardHistory.WM_CLIPBOARDUPDATE)
        {
            _self.OnClipboardUpdate();
            return 0;
        }
        // No frame at all: WinUI keeps a 3 px dialog frame that DWM paints white at the screen's top edge.
        if (msg == WM_NCCALCSIZE && wParam != 0) return 0;
        if (msg == WM_DPICHANGED)
        {
            // Let WinUI pick up the new scale, then undo its suggested resize: Place already sized
            // the window for this monitor, and the region must be recomputed in the new pixels.
            var result = DefSubclassProc(hwnd, msg, wParam, lParam);
            var s = _self;
            SetWindowPos(hwnd, HWND_TOPMOST, s._winX, s._winY, (int)Math.Ceiling(CanvasW * s._scale), (int)Math.Ceiling(CanvasH * s._scale), SWP_NOACTIVATE);
            s._hit = new RECT { Right = -1 };
            s.Render();
            return result;
        }
        if (msg == WM_HOTKEY && wParam == HotkeyId)
        {
            _self.Toggle();
            return 0;
        }
        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    // Damped spring, mass 1, integrated in 240 Hz sub-steps.
    sealed class Spring(double x)
    {
        public double X = x, V, T = x;

        public bool Step(double k, double c, double dt, int n)
        {
            var h = dt / n;
            for (int i = 0; i < n; i++)
            {
                V += (-k * (X - T) - c * V) * h;
                X += V * h;
            }
            if (Math.Abs(X - T) > 0.05 || Math.Abs(V) > 0.05) return true;
            (X, V) = (T, 0);
            return false;
        }
    }

    // Fully transparent window background so only the pill is drawn.
    sealed class TransparentBackdrop : SystemBackdrop
    {
        static Windows.UI.Composition.Compositor? _compositor;

        protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
        {
            base.OnTargetConnected(target, root);
            if (Windows.System.DispatcherQueue.GetForCurrentThread() is null)
                CreateDispatcherQueueController(new DispatcherQueueOptions { Size = 12, ThreadType = 2 }, out _);
            _compositor ??= new Windows.UI.Composition.Compositor();
            target.SystemBackdrop = _compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }
    }
}
