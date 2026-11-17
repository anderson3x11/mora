using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using static Mora.Native;

namespace Mora;

// The agents' rail: a black strip grown out of the screen's right edge, like the notch out of the top, with a
// ring per coding agent session. The ring fills with Claude's plan as it gets done (or with the context in use,
// without a plan), white while it works, orange when it needs the user, green once done, the crab inside.
// A ring opens the Agents tab; the round button under the rail opens Settings. Never takes the focus.
sealed class RailWindow : Window
{
    const double W = 60, Shoulder = 14, Corner = 26, Ring = 40, Item = 58, Gap = 12, Pad = 16, Button = 40;
    const int Most = 4; // sessions shown at once, the ones needing the user first

    readonly nint _hwnd;
    readonly Grid _root = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    readonly Microsoft.UI.Xaml.Shapes.Path _strip = new() { Fill = new SolidColorBrush(Colors.Black) };
    readonly StackPanel _rings = new() { Spacing = Gap, HorizontalAlignment = HorizontalAlignment.Center };
    readonly TranslateTransform _slide = new() { X = W + 4 };
    readonly Func<Agents.State, double, FrameworkElement> _mascot;
    readonly Action _open, _settings;
    bool _shown;
    string _key = "";

    public RailWindow(Func<Agents.State, double, FrameworkElement> mascot, Action open, Action settings)
    {
        (_mascot, _open, _settings) = (mascot, open, settings);
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SystemBackdrop = new IslandWindow.TransparentBackdrop();
        Title = "Mora agents";
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
        var glass = new RECT { Left = -1, Top = -1, Right = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(_hwnd, ref glass);
        unsafe { SetWindowSubclass(_hwnd, (nint)(delegate* unmanaged<nint, uint, nint, nint, nuint, nuint, nint>)&WndProc, 1, 0); }

        var gear = new Grid { Width = Button, Height = Button, CornerRadius = new CornerRadius(Button / 2), Background = new SolidColorBrush(Colors.Black), HorizontalAlignment = HorizontalAlignment.Center };
        gear.Children.Add(new FontIcon { Glyph = "", FontSize = 16, FontFamily = new FontFamily("Segoe Fluent Icons,Segoe MDL2 Assets"), Foreground = new SolidColorBrush(Colors.White) });
        gear.Tapped += (_, _) => _settings();
        ToolTipService.SetToolTip(gear, "Settings");
        var column = new Grid { Width = W, RenderTransform = _slide };
        column.Children.Add(_strip);
        column.Children.Add(new Grid { Margin = new Thickness(0, Shoulder + Pad, 0, 0), VerticalAlignment = VerticalAlignment.Top, Children = { _rings } });
        _root.Children.Add(column);
        _root.Children.Add(gear);
        _gear = gear;
        Content = _root;
    }

    readonly Grid _gear;

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint data)
    {
        if (msg == WM_MOUSEACTIVATE) return (nint)MA_NOACTIVATE;
        if (msg == WM_NCCALCSIZE && wParam != 0) return 0;
        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    // Shows the sessions worth a look (working, waiting for the user, or done in the last two minutes), or slides
    // away when there are none. monitor: the island's screen.
    public void Show(IEnumerable<Agents.Session> sessions, nint monitor)
    {
        var list = sessions.Where(s => s.State != Agents.State.Done || DateTime.Now - s.Updated < TimeSpan.FromMinutes(2))
            .OrderBy(s => s.State switch { Agents.State.NeedsYou => 0, Agents.State.Working => 1, _ => 2 }).ThenByDescending(s => s.Started)
            .Take(Most).ToList();
        if (list.Count == 0)
        {
            Hide();
            return;
        }
        // Rebuilt only when something on it changes: the crabs keep their step.
        var key = string.Join("|", list.Select(s => $"{s.Name}:{s.State}:{Label(s)}"));
        if (key != _key)
        {
            _key = key;
            _rings.Children.Clear();
            foreach (var s in list) _rings.Children.Add(RingFor(s));
        }
        double h = Pad * 2 + list.Count * Item + (list.Count - 1) * Gap;
        _strip.Data = StripShape(h);
        _gear.Margin = new Thickness(0, Shoulder * 2 + h + 10, 0, 0);
        Place(monitor, Shoulder * 2 + h + 10 + Button);
        if (!_shown)
        {
            _shown = true;
            ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
            Slide(0);
        }
    }

    public void Hide()
    {
        if (!_shown) return;
        _shown = false;
        _key = "";
        Slide(W + 4, () => { if (!_shown) ShowWindow(_hwnd, 0); }); // SW_HIDE once out of sight
    }

    void Slide(double to, Action? done = null)
    {
        var sb = new Storyboard();
        var a = new DoubleAnimation { To = to, Duration = TimeSpan.FromMilliseconds(to == 0 ? 420 : 260),
            EasingFunction = to == 0 ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 } : new ExponentialEase { EasingMode = EasingMode.EaseIn, Exponent = 3 } };
        Storyboard.SetTarget(a, _slide);
        Storyboard.SetTargetProperty(a, "X");
        sb.Children.Add(a);
        if (done != null) sb.Completed += (_, _) => done();
        sb.Begin();
    }

    // Against the right edge, centered in the screen's height. Sized in the screen's own pixels.
    void Place(nint monitor, double height)
    {
        var info = new MONITORINFO { Size = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        GetDpiForMonitor(monitor, 0, out var dpi, out _);
        double scale = dpi / 96.0;
        int w = (int)Math.Ceiling(W * scale), h = (int)Math.Ceiling(height * scale);
        int x = info.Monitor.Right - w, y = (info.Work.Top + info.Work.Bottom - h) / 2;
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, w, h, SWP_NOACTIVATE);
        _root.Width = W;
        _root.Height = height;
    }

    // The strip: square against the edge, round corners on the screen side, and a curved shoulder above and below
    // where it meets the edge, as the notch meets the top of the screen.
    static Geometry StripShape(double h)
    {
        double top = Shoulder, bottom = Shoulder + h;
        var f = new PathFigure { StartPoint = new(W, 0), IsClosed = true, IsFilled = true };
        f.Segments.Add(new ArcSegment { Point = new(W - Shoulder, top), Size = new(Shoulder, Shoulder), SweepDirection = SweepDirection.Clockwise });
        f.Segments.Add(new LineSegment { Point = new(Corner, top) });
        f.Segments.Add(new ArcSegment { Point = new(0, top + Corner), Size = new(Corner, Corner), SweepDirection = SweepDirection.Counterclockwise });
        f.Segments.Add(new LineSegment { Point = new(0, bottom - Corner) });
        f.Segments.Add(new ArcSegment { Point = new(Corner, bottom), Size = new(Corner, Corner), SweepDirection = SweepDirection.Counterclockwise });
        f.Segments.Add(new LineSegment { Point = new(W - Shoulder, bottom) });
        f.Segments.Add(new ArcSegment { Point = new(W, bottom + Shoulder), Size = new(Shoulder, Shoulder), SweepDirection = SweepDirection.Clockwise });
        var g = new PathGeometry();
        g.Figures.Add(f);
        return g;
    }

    // Under the ring: the plan or the context, else how long it has been working, in minutes (the rail redraws
    // on events and every 20 seconds, not every second).
    public static string Label(Agents.Session s) => s.State switch
    {
        Agents.State.NeedsYou => "Your turn",
        _ when s.ProgressLabel is { Length: > 0 } label => label,
        Agents.State.Done => "Done",
        _ => (DateTime.Now - s.Started).TotalMinutes is var m && m < 1 ? "Now" : $"{(int)m} min",
    };

    static void Breathe(UIElement arc)
    {
        var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
        var a = new DoubleAnimation { From = 1, To = 0.35, Duration = TimeSpan.FromMilliseconds(900), EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        Storyboard.SetTarget(a, arc);
        Storyboard.SetTargetProperty(a, "Opacity");
        sb.Children.Add(a);
        sb.Begin();
    }

    public static readonly SolidColorBrush Track = new(Windows.UI.Color.FromArgb(255, 0x2C, 0x2C, 0x2E)),
        White = new(Colors.White), Orange = new(Windows.UI.Color.FromArgb(255, 0xFF, 0x9F, 0x0A)),
        Green = new(Windows.UI.Color.FromArgb(255, 0x30, 0xD1, 0x58)), Muted = new(Windows.UI.Color.FromArgb(255, 0x98, 0x98, 0x9D));

    FrameworkElement RingFor(Agents.Session s)
    {
        bool asks = s.State == Agents.State.NeedsYou;
        var item = new StackPanel { Spacing = 4, Background = new SolidColorBrush(Colors.Transparent) };
        item.Children.Add(RingOf(s, _mascot, Ring, 3.5));
        item.Children.Add(new TextBlock
        {
            Text = Label(s),
            FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = asks ? Orange : Muted, FontFamily = new FontFamily("Segoe UI Variable Text,Segoe UI"),
        });
        item.Tapped += (_, _) => _open();
        ToolTipService.SetToolTip(item, $"{s.Name}: {s.Action}" + (s.Plan.Count > 0 ? $"\nPlan {s.ProgressLabel}" : "") + (s.Context is { } c ? $"\nContext {c * 100:0}% used" : ""));
        return item;
    }

    // A session's ring, also used by the Agents tab: the track, the arc of its progress in its state's color
    // (whole and breathing when it waits for the user), the crab inside.
    public static FrameworkElement RingOf(Agents.Session s, Func<Agents.State, double, FrameworkElement> mascot, double size, double stroke)
    {
        var color = s.State switch { Agents.State.NeedsYou => Orange, Agents.State.Done => Green, _ => White };
        var ring = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
        ring.Children.Add(new Ellipse { Stroke = Track, StrokeThickness = stroke });
        // The arc: a dashed circle whose one dash is as long as the progress, starting at the top.
        bool asks = s.State == Agents.State.NeedsYou; // the whole ring, breathing, whatever the progress
        double units = Math.PI * (size - stroke) / stroke, fill = asks ? 1 : Math.Clamp(s.Progress ?? 0, 0, 1);
        if (fill > 0)
        {
            var arc = new Ellipse
            {
                Stroke = color, StrokeThickness = stroke, StrokeDashCap = PenLineCap.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                StrokeDashArray = fill >= 1 ? null : [Math.Max(0.01, fill * units - 1), units * 2],
                RenderTransform = new RotateTransform { Angle = -90 }, RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            };
            ring.Children.Add(arc);
            if (asks) Breathe(arc);
        }
        ring.Children.Add(mascot(s.State, size >= 40 ? 2 : 1.5));
        return ring;
    }
}
