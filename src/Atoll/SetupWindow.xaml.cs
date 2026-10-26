using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Atoll;

// First-run setup in four steps, and later the settings window (tabs on the left).
// Unlike the island this is an ordinary window: the user opened it to click and type in it.
public sealed partial class SetupWindow : Window
{
    enum Tab { General, Popups, Games, About }

    readonly Settings _working, _original;
    readonly bool _firstRun;
    readonly Action<Settings> _done;
    bool _finished;
    int _step;
    Tab _tab;

    // Standard controls need their styles. They load only when this window opens, so the island stays lean.
    internal static SetupWindow Open(Settings settings, bool firstRun, Action<Settings> done)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        if (!dictionaries.OfType<XamlControlsResources>().Any()) dictionaries.Add(new XamlControlsResources());
        var w = new SetupWindow(settings.Clone(), settings, firstRun, done);
        w.Activate();
        return w;
    }

    SetupWindow(Settings working, Settings original, bool firstRun, Action<Settings> done)
    {
        (_working, _original, _firstRun, _done) = (working, original, firstRun, done);
        InitializeComponent();
        Title = firstRun ? "Atoll Setup" : "Atoll Settings";
        WindowTitle.Text = Title;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Atoll.ico"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        var bar = AppWindow.TitleBar;
        bar.ButtonBackgroundColor = bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = Colors.White;
        bar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 0x9B, 0x9B, 0xA5);
        bar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 0x1C, 0x1C, 0x21);

        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        double scale = Native.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        int w = (int)((firstRun ? 600 : 820) * scale), h = (int)((firstRun ? 540 : 600) * scale);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));
        Nav.Visibility = firstRun ? Visibility.Collapsed : Visibility.Visible;
        // Closing setup early still counts as done: the defaults are safe, and it should not come back every launch.
        Closed += (_, _) => { if (!_finished && _firstRun) _done(_working); };
        Show();
    }

    void Show()
    {
        Page.Children.Clear();
        Dots.Children.Clear();
        if (_firstRun) ShowStep(); else ShowTab();
    }

    // ---------- settings: tabs ----------

    void ShowTab()
    {
        (BackButton.Content, NextButton.Content) = ("Cancel", "Save");
        Nav.Children.Clear();
        foreach (var (tab, glyph, label) in new[] { (Tab.General, "", "General"), (Tab.Popups, "", "Pop-ups"), (Tab.Games, "", "Games"), (Tab.About, "", "About") })
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 15 });
            content.Children.Add(new TextBlock { Text = label, FontSize = 13 });
            var item = new Button
            {
                Content = content,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(14, 9, 14, 9),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(0),
                Background = tab == _tab ? Brush("Raised") : new SolidColorBrush(Colors.Transparent),
                Foreground = tab == _tab ? new SolidColorBrush(Colors.White) : Brush("Muted"),
            };
            item.Click += (_, _) => { _tab = tab; Show(); };
            Nav.Children.Add(item);
            // Keeps keyboard focus on the current tab after a rebuild, without the keyboard focus ring on a mouse click.
            if (tab == _tab) item.Loaded += (_, _) => item.Focus(FocusState.Programmatic);
        }

        switch (_tab)
        {
            case Tab.General:
                Heading("General", "How Atoll starts and how it rests on screen.");
                Page.Children.Add(Card(
                    Row("Start Atoll when Windows starts", "It waits in the tray, next to the clock.", Toggle(() => _working.StartWithWindows, v => _working.StartWithWindows = v)),
                    Row("Keep a small pill on screen while music plays", "Off by default: without a notch, it can cover browser tabs.", Toggle(() => _working.ShowCompactPill, v => _working.ShowCompactPill = v)),
                    Row("Open the island", "Click, then press the shortcut you want. It works from any app.", ShortcutPicker())));
                break;
            case Tab.Popups:
                Heading("Pop-ups", "What may open the island on its own. Each one leaves after 3 seconds, and none show during fullscreen games.");
                Page.Children.Add(PopupsCard());
                break;
            case Tab.Games:
                Heading("Games", "What Atoll does while a fullscreen game or video is in front. Anything not allowed waits, and shows up as one summary when you leave the game.");
                Page.Children.Add(Subheading("Any game", "For every game without its own settings below."));
                Page.Children.Add(ProfileCard(_working.AnyGame, Row("Ask the first time I launch a new game",
                    $"Press {_working.HotkeyName} for music controls, or do nothing and Atoll stays out.",
                    Toggle(() => _working.AskNewGames, v => _working.AskNewGames = v))));
                Page.Children.Add(Subheading("Per game", "Games Atoll has seen. Give one its own settings, like everything on for a chill game and nothing for a nervous one."));
                Page.Children.Add(PerGame());
                break;
            default:
                Heading("About", $"Atoll {typeof(App).Assembly.GetName().Version?.ToString(3)}, a Dynamic Island for Windows.");
                Page.Children.Add(Card(
                    Row("Never takes your keyboard", "Every key you press keeps going to the app you are in, even while the island opens.", null),
                    Row("No ads, ever", "The island only shows things you are doing. Never promotions, including ours.", null),
                    Row("Stays on this PC", "Settings, the shelf and the clipboard history never leave your computer.", null)));
                break;
        }
    }

    // One folded row per game seen: customized games open on their own settings, the others offer to start
    // from the "Any game" ones. Folded, a long list of games stays short.
    StackPanel PerGame()
    {
        var list = new StackPanel { Spacing = 6 };
        var games = _working.SeenGames.Union(_working.GameProfiles.Keys).Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (games.Count == 0)
        {
            list.Children.Add(new TextBlock { Text = "No game yet. They show up here after you play them.", Foreground = Brush("Muted") });
            return list;
        }
        foreach (var exe in games)
        {
            if (!_working.GameProfiles.TryGetValue(exe, out var profile))
            {
                var customize = new Button { Content = "Customize" };
                customize.Click += (_, _) => { _working.GameProfiles[exe] = _working.AnyGame.Clone(); _expanded = exe; Show(); };
                list.Children.Add(Card(Row(exe, "Uses the \"Any game\" settings.", customize)));
                continue;
            }
            var header = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
            header.Children.Add(new TextBlock { Text = exe });
            header.Children.Add(new TextBlock { Text = Summary(profile), Foreground = Brush("Muted"), FontSize = 12.5 });
            var reset = new HyperlinkButton { Content = "Use the \"Any game\" settings instead", Padding = new Thickness(0), Margin = new Thickness(0, 8, 0, 0) };
            reset.Click += (_, _) => { _working.GameProfiles.Remove(exe); Show(); };
            var body = new StackPanel();
            body.Children.Add(ProfileCard(profile));
            body.Children.Add(reset);
            list.Children.Add(new Expander
            {
                Header = header, Content = body, IsExpanded = exe == _expanded,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            });
        }
        return list;
    }

    string? _expanded; // the game just customized opens unfolded

    string Summary(GameProfile p)
    {
        var shows = p.Pops.Count == 0 ? "nothing pops up" : $"{p.Pops.Count} kind{(p.Pops.Count > 1 ? "s" : "")} of pop-up";
        return (p.MusicCard ? $"Music on {_working.HotkeyName}" : $"{_working.HotkeyName} left to the game") + ", " + shows;
    }

    // The shortcut's role and what may pop up during a game.
    Border ProfileCard(GameProfile p, params UIElement[] extra)
    {
        Grid PopRow(Pop kind, string title) => Row(title, null, Toggle(() => p.Shows(kind), v =>
        {
            p.Pops.Remove(kind);
            if (v) p.Pops.Add(kind);
        }));
        return Card([
            Row($"{_working.HotkeyName} opens music controls", "Otherwise the game keeps the shortcut. While the card shows, the arrow keys switch tracks and set the volume.",
                Toggle(() => p.MusicCard, v => p.MusicCard = v)),
            PopRow(Pop.Music, "Show music changes"),
            PopRow(Pop.Timers, "Show finished timers"),
            PopRow(Pop.Downloads, "Show finished downloads"),
            PopRow(Pop.Devices, "Show headphones and battery"),
            PopRow(Pop.Clipboard, "Show clipboard copies"),
            PopRow(Pop.Volume, "Show volume changes"),
            .. extra,
        ]);
    }

    // ---------- first run: four steps ----------

    void ShowStep()
    {
        switch (_step)
        {
            case 0:
                Heading("A living island at the top of your screen", "Music, timers, downloads and device events show up in a small black island, then get out of the way.");
                Page.Children.Add(Card(Row("Open it any time", "From any app, or from the Atoll icon next to the clock. You can change the shortcut in Settings.", Key(_working.HotkeyName))));
                break;
            case 1:
                Heading("When you play fullscreen games", "Pick what Atoll does. You can change it later in Settings.");
                Page.Children.Add(GameModeCard());
                break;
            case 2:
                Heading("What can pop up on its own", "Each pop-up leaves after 3 seconds, and none show during fullscreen games.");
                Page.Children.Add(PopupsCard());
                break;
            default:
                Heading("Two promises", "Made once, kept always.");
                Page.Children.Add(Card(
                    Row("Never takes your keyboard", "Every key you press keeps going to the app you are in, even while the island opens.", null),
                    Row("No ads, ever", "The island only shows things you are doing. Never promotions, including ours.", null)));
                Page.Children.Add(Card(Row("Start Atoll when Windows starts", "It waits in the tray, next to the clock.", Toggle(() => _working.StartWithWindows, v => _working.StartWithWindows = v))));
                break;
        }
        BackButton.Visibility = _step == 0 ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = _step == 3 ? "Finish" : "Next";
        for (int i = 0; i < 4; i++)
            Dots.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = i == _step ? 18 : 7, Height = 7, RadiusX = 3.5, RadiusY = 3.5,
                Fill = i == _step ? Brush("Accent") : Brush("Raised"),
            });
    }

    // ---------- shared sections ----------

    Border GameModeCard()
    {
        (GameMode Mode, string Title, string Description)[] choices =
        [
            (GameMode.Quiet, "Stay out of my way", $"Nothing pops up and {_working.HotkeyName} is left to the game. Best for shooters, RPGs, anything immersive."),
            (GameMode.Music, "Only when I ask", $"No pop-ups, but {_working.HotkeyName} opens music controls that close by themselves. Best for chill games with music."),
            (GameMode.Ask, "Decide per game", "The first time you launch a game, Atoll asks which of the two you want."),
        ];
        var rows = choices.Select(c =>
        {
            var label = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
            label.Children.Add(new TextBlock { Text = c.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            label.Children.Add(new TextBlock { Text = c.Description, TextWrapping = TextWrapping.Wrap, Foreground = Brush("Muted"), FontSize = 12.5 });
            var current = _working.AskNewGames ? GameMode.Ask : _working.AnyGame.MusicCard ? GameMode.Music : GameMode.Quiet;
            var radio = new RadioButton { GroupName = "games", Content = label, IsChecked = current == c.Mode, Margin = new Thickness(0, 10, 0, 10) };
            radio.Checked += (_, _) =>
            {
                _working.AnyGame.MusicCard = c.Mode == GameMode.Music;
                _working.AskNewGames = c.Mode == GameMode.Ask;
            };
            return (UIElement)radio;
        }).ToArray();
        return Card(rows);
    }

    Border PopupsCard() => Card(
        Row("Music changes", "The new track, with its artwork.", Toggle(() => _working.PopMusic, v => _working.PopMusic = v)),
        Row("Timers", "When a timer is done.", Toggle(() => _working.PopTimers, v => _working.PopTimers = v)),
        Row("Finished downloads", "With a button to show the file.", Toggle(() => _working.PopDownloads, v => _working.PopDownloads = v)),
        Row("Headphones and battery", "Audio output switches, low battery, charging.", Toggle(() => _working.PopDevices, v => _working.PopDevices = v)),
        Row("Clipboard copies", "Private copies from password managers never show.", Toggle(() => _working.PopClipboard, v => _working.PopClipboard = v)),
        Row("Volume", "Windows shows its own volume pop-up too.", Toggle(() => _working.PopVolume, v => _working.PopVolume = v)));

    // A rounded card of rows separated by hairlines, like the island's panel.
    Border Card(params UIElement[] rows)
    {
        var stack = new StackPanel();
        for (int i = 0; i < rows.Length; i++)
        {
            if (i > 0) stack.Children.Add(new Border { Height = 1, Background = Brush("Raised") });
            stack.Children.Add(rows[i]);
        }
        return new Border { Background = Brush("Card"), CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 2, 14, 2), Child = stack };
    }

    Grid Row(string title, string? description, FrameworkElement? control)
    {
        var row = new Grid { Padding = new Thickness(0, 12, 0, 12), ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        if (description != null)
            text.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Foreground = Brush("Muted"), FontSize = 12.5 });
        row.Children.Add(text);
        if (control != null)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            row.Children.Add(control);
        }
        return row;
    }

    // Click the button, press a shortcut: it is checked right away (needs a modifier, not AltGr,
    // not already held by Windows or another app) and only kept when Save is clicked.
    StackPanel ShortcutPicker()
    {
        var button = new Button
        {
            Content = _working.HotkeyName, MinWidth = 150, Padding = new Thickness(12, 6, 12, 6),
            FontFamily = new FontFamily("Cascadia Mono,Consolas"), FontSize = 12,
        };
        var status = new TextBlock { FontSize = 12, Foreground = Brush("Muted"), TextWrapping = TextWrapping.Wrap, MaxWidth = 220, HorizontalAlignment = HorizontalAlignment.Right };
        bool recording = false;
        button.Click += (_, _) =>
        {
            recording = true;
            button.Content = "Press keys…";
            status.Text = "Esc cancels. If nothing happens, another app already uses that shortcut.";
        };
        Root.PreviewKeyDown += (_, e) =>
        {
            if (!recording) return;
            e.Handled = true;
            uint vk = (uint)e.Key, mods = Hotkey.Held();
            if (Hotkey.IsModifierKey(vk)) return; // wait for the actual key
            recording = false;
            if (vk == 0x1B) { button.Content = _working.HotkeyName; status.Text = ""; return; }
            string? problem =
                mods == 0 && vk is < 0x7C or > 0x87 ? "Add Alt, Ctrl, Shift or Win, so typing never triggers it." :
                (mods & (Hotkey.Ctrl | Hotkey.Alt)) == (Hotkey.Ctrl | Hotkey.Alt) ? "Ctrl + Alt is AltGr on many keyboards, used to type characters." :
                (mods, vk) != (_original.HotkeyModifiers, _original.HotkeyKey) && !Hotkey.IsFree(mods, vk) ? "Windows or another app already uses it." :
                null;
            if (problem != null)
            {
                button.Content = _working.HotkeyName;
                status.Text = $"{Hotkey.Name(mods, vk)}: {problem}";
                return;
            }
            (_working.HotkeyModifiers, _working.HotkeyKey) = (mods, vk);
            button.Content = _working.HotkeyName;
            status.Text = "Applied when you click Save.";
        };
        var reset = new HyperlinkButton { Content = $"Reset to {Hotkey.DefaultName}", FontSize = 12, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right };
        reset.Click += (_, _) =>
        {
            (_working.HotkeyModifiers, _working.HotkeyKey) = (Hotkey.Alt, Hotkey.KeyAboveTab);
            button.Content = _working.HotkeyName;
            status.Text = "";
        };
        var panel = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(button);
        panel.Children.Add(status);
        panel.Children.Add(reset);
        return panel;
    }

    static ToggleSwitch Toggle(Func<bool> get, Action<bool> set)
    {
        var t = new ToggleSwitch { IsOn = get(), OnContent = "", OffContent = "", MinWidth = 0 };
        t.Toggled += (_, _) => set(t.IsOn);
        return t;
    }

    Border Key(string keys) => new()
    {
        Background = Brush("Raised"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 5, 10, 5),
        Child = new TextBlock { Text = keys, FontFamily = new FontFamily("Cascadia Mono,Consolas"), FontSize = 12 },
    };

    void Heading(string title, string subtitle)
    {
        Page.Children.Add(new TextBlock { Text = title, FontSize = 26, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        Page.Children.Add(new TextBlock { Text = subtitle, TextWrapping = TextWrapping.Wrap, Foreground = Brush("Muted"), Margin = new Thickness(0, -6, 0, 6) });
    }

    StackPanel Subheading(string title, string subtitle)
    {
        var p = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        p.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        p.Children.Add(new TextBlock { Text = subtitle, TextWrapping = TextWrapping.Wrap, Foreground = Brush("Muted"), FontSize = 12.5 });
        return p;
    }

    Brush Brush(string key) => (Brush)Root.Resources[key];

    void Back_Click(object sender, RoutedEventArgs e)
    {
        if (!_firstRun)
        {
            _finished = true;
            Close();
            return;
        }
        _step--;
        Show();
    }

    void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_firstRun && _step < 3)
        {
            _step++;
            Show();
            return;
        }
        _finished = true;
        _done(_working);
        Close();
    }
}
