using System.Diagnostics;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
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

    record struct Geo(double W, double H, double R, double Y);
    static readonly Geo HiddenGeo = new(96, 24, 12, -40), ExpandedGeo = new(480, 212, 32, 8);

    static IslandWindow _self = null!; // one island; native callbacks route here
    readonly nint _hwnd;
    readonly Spring _w = new(HiddenGeo.W), _h = new(HiddenGeo.H), _r = new(HiddenGeo.R), _y = new(HiddenGeo.Y);
    readonly bool _motion = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
    readonly Stopwatch _clock = new();
    (double K, double C) _spring = OpenSpring;
    double _last, _scale = 1;
    bool _animating, _expanded;
    int _token, _winX, _winY;
    nint _hook;
    RECT _hit; // physical px, window-relative

    public IslandWindow()
    {
        InitializeComponent();
        _self = this;
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
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
        Render();
        ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
    }

    // ponytail: primary monitor only, follow the active window's monitor later (brief section 8).
    void Place()
    {
        var mon = MonitorFromPoint(default, 1); // MONITOR_DEFAULTTOPRIMARY
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
        if (_expanded) Collapse(); else Expand();
    }

    async void Expand()
    {
        var t = ++_token;
        _expanded = true;
        HookMouse(true);
        Retarget(ExpandedGeo, OpenSpring);
        await Task.Delay(140);
        if (t == _token) Fade(true);
    }

    async void Collapse()
    {
        var t = ++_token;
        _expanded = false;
        HookMouse(false);
        Fade(false);
        await Task.Delay(90);
        if (t == _token) Retarget(HiddenGeo, CloseSpring);
    }

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

    void Fade(bool on)
    {
        var sb = new Storyboard();
        Animate(sb, PanelContent, "Opacity", on ? 1 : 0, on ? 220 : 140);
        Animate(sb, ContentScale, "ScaleX", on ? 1 : 0.94, on ? 380 : 280);
        Animate(sb, ContentScale, "ScaleY", on ? 1 : 0.94, on ? 380 : 280);
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

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    static unsafe nint MouseProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && (wParam == WM_LBUTTONDOWN || wParam == WM_RBUTTONDOWN || wParam == WM_MBUTTONDOWN))
        {
            var pt = *(POINT*)lParam; // MSLLHOOKSTRUCT starts with the screen point
            var s = _self;
            int x = pt.X - s._winX, y = pt.Y - s._winY;
            if (x < s._hit.Left || x >= s._hit.Right || y < s._hit.Top || y >= s._hit.Bottom)
                s.DispatcherQueue.TryEnqueue(() => { if (s._expanded) s.Collapse(); });
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint data)
    {
        if (msg == WM_MOUSEACTIVATE) return (nint)MA_NOACTIVATE;
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
