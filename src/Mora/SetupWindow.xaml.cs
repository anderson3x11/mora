using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Mora;

// First-run setup in four steps, and later the settings window (tabs on the left).
// Unlike the island this is an ordinary window: the user opened it to click and type in it.
public sealed partial class SetupWindow : Window
{
    enum Tab { General, Popups, Games, About }

    readonly Settings _working;
    Settings _applied; // what the island runs with: the settings window applies every change as it is made
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
        (_working, _applied, _firstRun, _done) = (working, original, firstRun, done);
        InitializeComponent();
        Title = firstRun ? "Mora Setup" : "Mora Settings";
        WindowTitle.Text = Title;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Mora.ico"));
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
        BackButton.Visibility = Visibility.Collapsed; // nothing to cancel: changes apply at once
        NextButton.Content = "Done";
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
                Heading("General", "How Mora starts and how it rests on screen.");
                Page.Children.Add(Card(
                    Row("Start Mora when Windows starts", "It waits in the tray, next to the clock.", Toggle(() => _working.StartWithWindows, v => _working.StartWithWindows = v)),
                    ScreenRow(),
                    Row("Keep a small pill on screen while music plays", "Off by default: without a notch, it can cover browser tabs.", Toggle(() => _working.ShowCompactPill, v => _working.ShowCompactPill = v)),
                    Row("Open the panel on the last tab used", "Off: it always opens on Home. A file dragged in still opens the Shelf.", Toggle(() => _working.ReopenLastTab, v => _working.ReopenLastTab = v)),
                    Row("Show a small countdown while a timer runs", "With the music pill on too, it splits into a round bubble beside it.", Toggle(() => _working.ShowTimerPill, v => _working.ShowTimerPill = v)),
                    Row("Open the island", "Click, then press the shortcut you want. It works from any app.", ShortcutPicker())));
                Page.Children.Add(Subheading("Clock", "The time and date on the island. The resting pill is off by default: without a notch, it can cover browser tabs."));
                Page.Children.Add(Card(
                    Row("On the resting pill", "Shows the time at the top of the screen. Next to music, it moves into the round bubble.", Toggle(() => _working.ShowClockPill, v => _working.ShowClockPill = v)),
                    Row("Beside the open panel", "A tall pill with the time and the day, when no timer runs.", Toggle(() => _working.ClockBesidePanel, v => _working.ClockBesidePanel = v)),
                    Row("In the panel", "At the top right, next to what is playing.", Toggle(() => _working.ClockInPanel, v => _working.ClockInPanel = v)),
                    Row("On the game music card", "The time, without leaving the game.", Toggle(() => _working.ClockInGameCard, v => _working.ClockInGameCard = v))));
                Page.Children.Add(ClockFormatCard());
                Page.Children.Add(Subheading("Home", "The first tab: your music, the agenda, a focus timer and the weather."));
                Page.Children.Add(Card(
                    Row("Weather", "From Open-Meteo, for your area: Windows' location, rounded to about 10 km, is all that leaves your PC.", Toggle(() => _working.ShowWeather, v => _working.ShowWeather = v)),
                    Row("Calendar link", "Windows' calendars show by themselves. For Google Calendar, paste the \"Secret address in iCal format\" from its settings; for Outlook, a published calendar's ICS link.", CalendarLinkBox())));
                Page.Children.Add(Subheading("Coding agents", "Follow your AI agents from the island: what each session is doing, and a pop-up when one needs you or finishes a long task."));
                Page.Children.Add(Card(Row("Connect Claude Code", "Adds hooks to Claude Code's settings (.claude\\settings.json in your user folder). They report to Mora on this PC only. New sessions pick them up.",
                    Toggle(() => _working.ConnectClaudeCode, v => _working.ConnectClaudeCode = v)),
                    Row("Let the crab wander", "Mora's mascot walks along the bottom of the open panel, under everything. It bobs while an agent works and waves when one needs you.",
                        Toggle(() => _working.MascotRoams, v => _working.MascotRoams = v))));
                Page.Children.Add(Subheading("Theme", "The island's whole look: its shape, how it moves and its colors."));
                Page.Children.Add(ThemeCard());
                break;
            case Tab.Popups:
                Heading("Pop-ups", "What may open the island on its own. Each one leaves after 3 seconds, and none show during fullscreen games.");
                Page.Children.Add(PopupsCard());
                Page.Children.Add(Subheading("App notifications", "Apps turned on here show their notifications in the island, and Windows stops showing them in the corner. They stay in the notification center. Apps appear once they have sent a notification."));
                Page.Children.Add(NotifyAppsCard());
                break;
            case Tab.Games:
                Heading("Games", "What Mora does while a fullscreen game is in front (games Windows knows, or installed by Steam, Riot, Epic, GOG, Ubisoft, EA or Xbox). Anything not allowed waits, and shows up as one summary when you leave the game.");
                Page.Children.Add(Subheading("Any game", "For every game without its own settings below."));
                Page.Children.Add(ProfileCard(_working.AnyGame, Row("Ask the first time I launch a new game",
                    $"Press {_working.HotkeyName} for music controls, or do nothing and Mora stays out.",
                    Toggle(() => _working.AskNewGames, v => _working.AskNewGames = v))));
                Page.Children.Add(Subheading("Per game", "Games Mora has seen. Give one its own settings, like everything on for a chill game and nothing for a nervous one."));
                Page.Children.Add(PerGame());
                break;
            default:
                Heading("About", $"Mora {typeof(App).Assembly.GetName().Version?.ToString(3)}, a Dynamic Island for Windows.");
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
                customize.Click += (_, _) => { _working.GameProfiles[exe] = _working.AnyGame.Clone(); _expanded = exe; Changed(); Show(); };
                list.Children.Add(Card(Row(exe, "Uses the \"Any game\" settings.", customize)));
                continue;
            }
            var header = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
            header.Children.Add(new TextBlock { Text = exe });
            header.Children.Add(new TextBlock { Text = Summary(profile), Foreground = Brush("Muted"), FontSize = 12.5 });
            var reset = new HyperlinkButton { Content = "Use the \"Any game\" settings instead", Padding = new Thickness(0), Margin = new Thickness(0, 8, 0, 0) };
            reset.Click += (_, _) => { _working.GameProfiles.Remove(exe); Changed(); Show(); };
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
            PopRow(Pop.Privacy, "Show microphone and camera use"),
            PopRow(Pop.Notifications, "Show app notifications"),
            PopRow(Pop.Agents, "Show coding agents"),
            .. extra,
        ]);
    }

    TextBox CalendarLinkBox()
    {
        var box = new TextBox { Text = _working.CalendarLink, PlaceholderText = "https://…/basic.ics", Width = 220 };
        box.LostFocus += (_, _) =>
        {
            if (box.Text.Trim() == _working.CalendarLink) return;
            _working.CalendarLink = box.Text.Trim();
            Changed();
        };
        return box;
    }

    UIElement ScreenRow() => Row("Only on the main screen", "Off: the island shows on the screen of the app you are in.",
        Toggle(() => _working.PrimaryScreenOnly, v => _working.PrimaryScreenOnly = v));

    // ---------- first run: four steps ----------

    void ShowStep()
    {
        switch (_step)
        {
            case 0:
                Heading("A living island at the top of your screen", "Music, timers, downloads and device events show up in a small black island, then get out of the way.");
                Page.Children.Add(Card(Row("Open it any time", "From any app, or from the Mora icon next to the clock. You can change the shortcut in Settings.", Key(_working.HotkeyName))));
                Page.Children.Add(Card(ScreenRow()));
                break;
            case 1:
                Heading("When you play fullscreen games", "Pick what Mora does. You can change it later in Settings.");
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
                Page.Children.Add(Card(Row("Start Mora when Windows starts", "It waits in the tray, next to the clock.", Toggle(() => _working.StartWithWindows, v => _working.StartWithWindows = v))));
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
            (GameMode.Ask, "Decide per game", "The first time you launch a game, Mora asks which of the two you want."),
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
                Changed();
            };
            return (UIElement)radio;
        }).ToArray();
        return Card(rows);
    }

    // Each choice shows what it looks like right now, in Windows' own format.
    Border ClockFormatCard()
    {
        var now = DateTime.Now;
        (ClockStyle Style, string Example)[] choices =
        [
            (ClockStyle.TimeAndDate, now.ToString("t") + " · " + now.ToString("ddd d MMM")),
            (ClockStyle.Time, now.ToString("t")),
            (ClockStyle.TimeWithSeconds, now.ToString("T")),
            (ClockStyle.FullDate, now.ToString("dddd d MMMM") + " · " + now.ToString("t")),
        ];
        return Card(choices.Select(c =>
        {
            var radio = new RadioButton { GroupName = "clock", Content = c.Example, IsChecked = _working.ClockFormat == c.Style, Margin = new Thickness(0, 6, 0, 6) };
            radio.Checked += (_, _) => { _working.ClockFormat = c.Style; Changed(); };
            return (UIElement)radio;
        }).ToArray());
    }

    Border ThemeCard() => Card(Theme.All.Select(t =>
    {
        var label = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
        label.Children.Add(new TextBlock { Text = t.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        label.Children.Add(new TextBlock { Text = t.Description, TextWrapping = TextWrapping.Wrap, Foreground = Brush("Muted"), FontSize = 12.5 });
        var radio = new RadioButton { GroupName = "theme", Content = label, IsChecked = _working.Theme == t.Name, Margin = new Thickness(0, 10, 0, 10) };
        radio.Checked += (_, _) => { _working.Theme = t.Name; Changed(); };
        return (UIElement)radio;
    }).ToArray());

    Border PopupsCard() => Card(
        Row("Music changes", "The new track, with its artwork.", Toggle(() => _working.PopMusic, v => _working.PopMusic = v)),
        Row("Timers", "When a timer is done.", Toggle(() => _working.PopTimers, v => _working.PopTimers = v)),
        Row("Finished downloads", "With a button to show the file.", Toggle(() => _working.PopDownloads, v => _working.PopDownloads = v)),
        Row("Headphones and battery", "Audio output switches, low battery, charging.", Toggle(() => _working.PopDevices, v => _working.PopDevices = v)),
        Row("Clipboard copies", "Private copies from password managers never show.", Toggle(() => _working.PopClipboard, v => _working.PopClipboard = v)),
        Row("Volume", "Windows shows its own volume pop-up too.", Toggle(() => _working.PopVolume, v => _working.PopVolume = v)),
        Row("Microphone and camera", "When an app starts using them. A dot also stays in the panel while it lasts.", Toggle(() => _working.PopPrivacy, v => _working.PopPrivacy = v)),
        Row("Coding agents", "When one needs you, or finishes a task of 30 seconds or more. Not while its window is in front.", Toggle(() => _working.PopAgents, v => _working.PopAgents = v)),
        Row("Sounds", "A ring when a timer or focus round ends, a short note for app notifications.", Toggle(() => _working.PlaySounds, v => _working.PlaySounds = v)),
        Row("Sound volume", "On top of the Windows volume.", SoundVolume()));

    // The volume slider, and a button per sound to hear it at that volume.
    StackPanel SoundVolume()
    {
        var slider = new Slider { Minimum = 0, Maximum = 100, StepFrequency = 5, Value = _working.SoundVolume, Width = 130, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, e) => { _working.SoundVolume = (int)e.NewValue; Changed(); };
        Button Try(string label, Pop kind)
        {
            var b = new Button { Content = label, Padding = new Thickness(10, 4, 10, 4) };
            b.Click += (_, _) => IslandWindow.Chime(kind, _working.SoundVolume);
            return b;
        }
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { slider, Try("Timer", Pop.Timers), Try("Notification", Pop.Notifications) } };
    }

    Border NotifyAppsCard()
    {
        var apps = Notifications.Seen.Concat(_working.NotifyApps).DistinctBy(a => a.Key).OrderBy(a => a.Value).ToList();
        if (!Notifications.Allowed) return Card(Row("Mora cannot read notifications", "Allow it in Windows Settings, Privacy and security, Notifications.", null));
        if (apps.Count == 0) return Card(Row("No app has sent a notification yet", "Since Mora started.", null));
        return Card([.. apps.Select(a => Row(a.Value, null, Toggle(() => _working.NotifyApps.ContainsKey(a.Key), v =>
        {
            if (v) _working.NotifyApps[a.Key] = a.Value;
            else _working.NotifyApps.Remove(a.Key);
        })))]);
    }

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
                (mods, vk) != (_applied.HotkeyModifiers, _applied.HotkeyKey) && !Hotkey.IsFree(mods, vk) ? "Windows or another app already uses it." :
                null;
            if (problem != null)
            {
                button.Content = _working.HotkeyName;
                status.Text = $"{Hotkey.Name(mods, vk)}: {problem}";
                return;
            }
            (_working.HotkeyModifiers, _working.HotkeyKey) = (mods, vk);
            button.Content = _working.HotkeyName;
            status.Text = "Applied.";
            Changed();
        };
        var reset = new HyperlinkButton { Content = $"Reset to {Hotkey.DefaultName}", FontSize = 12, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right };
        reset.Click += (_, _) =>
        {
            (_working.HotkeyModifiers, _working.HotkeyKey) = (Hotkey.Alt, Hotkey.KeyAboveTab);
            button.Content = _working.HotkeyName;
            status.Text = "";
            Changed();
        };
        var panel = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(button);
        panel.Children.Add(status);
        panel.Children.Add(reset);
        return panel;
    }

    ToggleSwitch Toggle(Func<bool> get, Action<bool> set)
    {
        var t = new ToggleSwitch { IsOn = get(), OnContent = "", OffContent = "", MinWidth = 0 };
        t.Toggled += (_, _) => { set(t.IsOn); Changed(); };
        return t;
    }

    // The settings window hands every change to the island right away, which saves it. First-run setup waits for Finish.
    void Changed()
    {
        if (_firstRun) return;
        _applied = _working.Clone();
        _done(_applied);
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
        _step--; // first-run setup only: the settings window has no Back button
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
        if (_firstRun) _done(_working); // the settings window has applied everything already
        Close();
    }
}
