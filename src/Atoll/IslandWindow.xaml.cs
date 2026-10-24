using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using static Atoll.Native;

namespace Atoll;

// The island never activates: WS_EX_NOACTIVATE + MA_NOACTIVATE, shown with SW_SHOWNOACTIVATE,
// and the window region follows the pill so everything around it is click-through.
public sealed partial class IslandWindow : Window
{
    // Fixed window canvas in DIPs: room for the largest state plus spring overshoot.
    const double CanvasW = 560, CanvasH = 300;
    const int HotkeyId = 1;
    static readonly (double K, double C) OpenSpring = (340, 27), CloseSpring = (520, 46), Reduced = (900, 60);

    enum Mode { Hidden, Compact, Alert, Game, Expanded }
    enum Via { None, Hotkey, Click, Hover }
    record struct Geo(double W, double H, double R, double Y);

    enum Tab { Media, Timers, Clipboard }

    Geo GeoOf(Mode m) => m switch
    {
        Mode.Compact => new(214, 32, 16, 8),
        Mode.Alert => new(_alert?.Action2Label is null ? 304 : 384, 50, 25, 8), // a question with two answers needs room
        Mode.Game => new(288, 66, 33, 8),
        Mode.Expanded => new(384, _tab switch
        {
            Tab.Media => 170,
            Tab.Timers => 124,
            _ => 54 + 31 * Math.Clamp(_clipboard?.Items.Count ?? 0, 1, 4), // one 26 px row + 5 px gap per copy
        }, 26, 8),
        _ => new(96, 24, 12, -40),
    };

    static IslandWindow _self = null!; // one island; native callbacks route here
    readonly nint _hwnd;
    readonly Spring _w, _h, _r, _y;
    readonly bool _motion = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
    readonly Stopwatch _clock = new();
    readonly Media _media;
    readonly Settings _settings = Settings.Load();
    readonly Tray _tray;
    readonly IReadOnlyList<(string Label, Action Run)> _trayMenu;
    readonly List<Visual> _wave = [];
    readonly DispatcherQueueTimer _waveTimer;
    readonly DispatcherQueueTimer _progressTimer;
    (double K, double C) _spring = OpenSpring;
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
        _self = this;
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var g = GeoOf(Mode.Hidden);
        (_w, _h, _r, _y) = (new(g.W), new(g.H), new(g.R), new(g.Y));
        SystemBackdrop = new TransparentBackdrop();
        Title = "Atoll";

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
        RegisterHotKey(_hwnd, HotkeyId, MOD_ALT | MOD_NOREPEAT, 'Q');

        Place();
        SetWindowRgn(_hwnd, CreateRectRgn(0, 0, 0, 0), 1); // click-through from the start; Render only updates on change
        Render();
        ShowWindow(_hwnd, SW_SHOWNOACTIVATE);

        _progressTimer = DispatcherQueue.CreateTimer();
        _progressTimer.Interval = TimeSpan.FromSeconds(1);
        _progressTimer.Tick += (_, _) => UpdateProgress();
        _waveTimer = DispatcherQueue.CreateTimer();
        BuildWave();
        ShowTab();
        FillTimer();
        _clipboard = new ClipboardHistory(_hwnd);
        FillClipboard();
        WatchGames();
        _media = new Media(DispatcherQueue);
        _media.Changed += OnMediaChanged;
        WireAlerts();
        _tray = new Tray(_hwnd);
        _trayMenu =
        [
            ("Open Atoll    Alt+Q", () => SetMode(Mode.Expanded, Via.Click)),
            ("Edit settings (applies on restart)", () =>
            {
                Settings.EnsureFile();
                Process.Start("notepad.exe", $"\"{Settings.FilePath}\"");
            }),
            ("Quit Atoll", () =>
            {
                _tray.Remove();
                HookMouse(false);
                Application.Current.Exit();
            }),
        ];
        _ = _media.StartAsync();
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
        if (_game != null)
        {
            if (GamePolicy == GameMode.Music) ToggleGameCard();
            return;
        }
        if (_mode == Mode.Expanded) Settle(); else SetMode(Mode.Expanded, Via.Hotkey);
    }

    // The resting pill, when enabled, stays while media exists (paused included), never over a game.
    bool RestingPill => _settings.ShowCompactPill && _media.HasSession && _game is null;

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
        var prev = _mode;
        var t = ++_token;
        (_mode, _via) = (mode, via);
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
        if (_mode == Mode.Hidden && RestingPill) SetMode(Mode.Compact);
        else if (_mode == Mode.Compact && !RestingPill) SetMode(Mode.Hidden);
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

    // ponytail: waveform stepped at 30 fps. Any per-vsync animation recomposes the window every frame
    // (~10% of a core at 240 Hz); the decorative bars don't need more. The pill's springs keep full rate.
    void BuildWave()
    {
        foreach (var bar in Wave.Children)
        {
            var v = ElementCompositionPreview.GetElementVisual(bar);
            v.CenterPoint = new Vector3(1.5f, 8, 0);
            v.Scale = new Vector3(1, 0.22f, 1);
            _wave.Add(v);
        }
        _waveTimer.Interval = TimeSpan.FromMilliseconds(33);
        _waveTimer.Tick += (_, _) =>
        {
            double t = _clock.Elapsed.TotalSeconds;
            double[] periods = [0.9, 0.76, 1.04, 0.82];
            for (int i = 0; i < _wave.Count; i++)
                _wave[i].Scale = new Vector3(1, (float)(0.25 + 0.75 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * t / periods[i]))), 1);
        };
    }

    // The waveform only runs while the pill shows and music plays; paused bars sit low and still.
    void UpdateWave()
    {
        bool run = _motion && _mode == Mode.Compact && _media.Playing;
        if (run == _waveTimer.IsRunning) return;
        if (run) _waveTimer.Start();
        else
        {
            _waveTimer.Stop();
            foreach (var v in _wave) v.Scale = new Vector3(1, 0.22f, 1);
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
        ClipboardTab.Visibility = _tab == Tab.Clipboard ? Visibility.Visible : Visibility.Collapsed;
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
        ((Microsoft.UI.Xaml.Controls.Panel)sender).Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0x1C, 0x1C, 0x21));

    void Button_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        ((Microsoft.UI.Xaml.Controls.Panel)sender).Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    // Retargets from the current position and velocity, never from zero.
    void Retarget(Geo g, (double K, double C) spring)
    {
        (_w.T, _h.T, _r.T, _y.T) = (g.W, g.H, g.R, g.Y);
        _spring = _motion ? spring : Reduced;
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
        bool moving = _w.Step(k, c, dt, n) | _h.Step(k, c, dt, n) | _r.Step(k, c, dt, n) | _y.Step(k, c, dt, n);
        Render();
        if (moving) return;
        CompositionTarget.Rendering -= OnRendering;
        _animating = false;
    }

    void Render()
    {
        double w = Math.Max(0, _w.X), h = Math.Max(0, _h.X);
        Pill.Width = w;
        Pill.Height = h;
        Pill.CornerRadius = new CornerRadius(Math.Min(Math.Max(0, _r.X), h / 2));
        PillOffset.Y = _y.X;

        var r = new RECT
        {
            Left = (int)Math.Floor((CanvasW - w) / 2 * _scale),
            Right = (int)Math.Ceiling((CanvasW + w) / 2 * _scale),
            Top = Math.Max(0, (int)Math.Floor(_y.X * _scale)),
            Bottom = (int)Math.Ceiling((_y.X + h) * _scale),
        };
        if (r.Bottom <= r.Top) r = default;
        if (r.Equals(_hit)) return;
        _hit = r;
        SetWindowRgn(_hwnd, CreateRectRgn(r.Left, r.Top, r.Right, r.Bottom), 1);
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
        if (code >= 0 && (wParam == WM_LBUTTONDOWN || wParam == WM_RBUTTONDOWN || wParam == WM_MBUTTONDOWN))
        {
            var s = _self;
            if (!s.OnPill(*(POINT*)lParam)) // MSLLHOOKSTRUCT starts with the screen point
                s.DispatcherQueue.TryEnqueue(() => { if (s._mode == Mode.Expanded) s.Settle(); });
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
        if (msg == ClipboardHistory.WM_CLIPBOARDUPDATE)
        {
            _self.OnClipboardUpdate();
            return 0;
        }
        // No frame at all: WinUI keeps a 3 px dialog frame that DWM paints white at the screen's top edge.
        if (msg == WM_NCCALCSIZE && wParam != 0) return 0;
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
