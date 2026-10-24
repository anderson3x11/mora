using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Atoll;

// First-run setup in four steps, and later the settings window (the same choices on one page).
// Unlike the island this is an ordinary window: the user opened it to click and type in it.
public sealed partial class SetupWindow : Window
{
    readonly Settings _working;
    readonly bool _firstRun;
    readonly Action<Settings> _done;
    bool _finished;
    int _step;

    // Standard controls need their styles. They load only when this window opens, so the island stays lean.
    internal static SetupWindow Open(Settings settings, bool firstRun, Action<Settings> done)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        if (!dictionaries.OfType<XamlControlsResources>().Any()) dictionaries.Add(new XamlControlsResources());
        var w = new SetupWindow(settings.Clone(), firstRun, done);
        w.Activate();
        return w;
    }

    SetupWindow(Settings working, bool firstRun, Action<Settings> done)
    {
        (_working, _firstRun, _done) = (working, firstRun, done);
        InitializeComponent();
        Title = firstRun ? "Atoll Setup" : "Atoll Settings";
        SystemBackdrop = new MicaBackdrop();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Atoll.ico"));
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        double scale = Native.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        int w = (int)(540 * scale), h = (int)((firstRun ? 540 : 800) * scale);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));
        // Closing setup early still counts as done: the defaults are safe, and it should not come back every launch.
        Closed += (_, _) => { if (!_finished && _firstRun) _done(_working); };
        ShowStep();
    }

    void ShowStep()
    {
        Page.Children.Clear();
        Dots.Children.Clear();
        if (!_firstRun)
        {
            Page.Children.Add(Games());
            Page.Children.Add(Popups());
            Page.Children.Add(Island());
            (BackButton.Content, NextButton.Content) = ("Cancel", "Save");
            return;
        }

        switch (_step)
        {
            case 0:
                Heading("A living island at the top of your screen");
                Body("Music, timers, downloads and device events show up in a small black island, then get out of the way.");
                Body("Open it any time with Alt+Q, or from the Atoll icon next to the clock.");
                break;
            case 1:
                Page.Children.Add(Games());
                break;
            case 2:
                Page.Children.Add(Popups());
                break;
            default:
                Heading("Two promises");
                Body("Atoll never takes your keyboard. Every key you press keeps going to the app you are in, even while the island opens.");
                Body("No ads, ever. The island only shows things you are doing. Never promotions, including ours.");
                Page.Children.Add(StartCheck());
                break;
        }
        BackButton.Visibility = _step == 0 ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = _step == 3 ? "Finish" : "Next";
        for (int i = 0; i < 4; i++)
            Dots.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = i == _step ? 18 : 7,
                Height = 7,
                RadiusX = 3.5,
                RadiusY = 3.5,
                Fill = (Brush)Application.Current.Resources[i == _step ? "AccentFillColorDefaultBrush" : "ControlStrongFillColorDisabledBrush"],
            });
    }

    StackPanel Games()
    {
        var p = Section("When a fullscreen game runs");
        (GameMode Mode, string Title, string Description)[] choices =
        [
            (GameMode.Quiet, "Stay out of my way", "Nothing pops up and Alt+Q is left to the game. Best for shooters, RPGs, anything immersive."),
            (GameMode.Music, "Only when I ask", "No pop-ups, but Alt+Q opens music controls that close by themselves. Best for chill games with music."),
            (GameMode.Ask, "Decide per game", "The first time you launch a game, Atoll asks which of the two you want."),
        ];
        foreach (var c in choices)
        {
            var label = new StackPanel();
            label.Children.Add(new TextBlock { Text = c.Title, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
            label.Children.Add(new TextBlock { Text = c.Description, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
            var radio = new RadioButton { GroupName = "games", Content = label, IsChecked = _working.Games == c.Mode, Margin = new Thickness(0, 0, 0, 6) };
            radio.Checked += (_, _) => _working.Games = c.Mode;
            p.Children.Add(radio);
        }
        return p;
    }

    StackPanel Popups()
    {
        var p = Section("What can pop up on its own");
        p.Children.Add(Check("Music changes", () => _working.PopMusic, v => _working.PopMusic = v));
        p.Children.Add(Check("Timers", () => _working.PopTimers, v => _working.PopTimers = v));
        p.Children.Add(Check("Finished downloads", () => _working.PopDownloads, v => _working.PopDownloads = v));
        p.Children.Add(Check("Headphones and battery", () => _working.PopDevices, v => _working.PopDevices = v));
        p.Children.Add(Check("Clipboard copies", () => _working.PopClipboard, v => _working.PopClipboard = v));
        p.Children.Add(Check("Volume (Windows shows its own too)", () => _working.PopVolume, v => _working.PopVolume = v));
        p.Children.Add(new TextBlock { Text = "Pop-ups leave after 3 seconds, and never show during fullscreen games.", TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
        return p;
    }

    StackPanel Island()
    {
        var p = Section("Island");
        p.Children.Add(Check("Keep a small pill on screen while music plays", () => _working.ShowCompactPill, v => _working.ShowCompactPill = v));
        p.Children.Add(StartCheck());
        return p;
    }

    CheckBox StartCheck() => Check("Start Atoll when Windows starts", () => _working.StartWithWindows, v => _working.StartWithWindows = v);

    static CheckBox Check(string label, Func<bool> get, Action<bool> set)
    {
        var c = new CheckBox { Content = label, IsChecked = get() };
        c.Checked += (_, _) => set(true);
        c.Unchecked += (_, _) => set(false);
        return c;
    }

    static StackPanel Section(string title)
    {
        var p = new StackPanel { Spacing = 4 };
        p.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"], Margin = new Thickness(0, 0, 0, 6) });
        return p;
    }

    void Heading(string text) =>
        Page.Children.Add(new TextBlock { Text = text, Style = (Style)Application.Current.Resources["TitleTextBlockStyle"], TextWrapping = TextWrapping.Wrap });

    void Body(string text) =>
        Page.Children.Add(new TextBlock { Text = text, Style = (Style)Application.Current.Resources["BodyTextBlockStyle"], TextWrapping = TextWrapping.Wrap });

    void Back_Click(object sender, RoutedEventArgs e)
    {
        if (!_firstRun)
        {
            _finished = true;
            Close();
            return;
        }
        _step--;
        ShowStep();
    }

    void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_firstRun && _step < 3)
        {
            _step++;
            ShowStep();
            return;
        }
        _finished = true;
        _done(_working);
        Close();
    }
}
