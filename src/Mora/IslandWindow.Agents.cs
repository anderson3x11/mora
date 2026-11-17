using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using static Mora.Native;

namespace Mora;

// Agents tab: each Claude Code session with what it is doing, and an alert when one needs the user or ends a
// long task. Not while the user is looking at an agent's window: they already see it.
public sealed partial class IslandWindow
{
    Agents? _agents;

    RailWindow? _rail;

    void WireAgents()
    {
        _agents = new Agents(DispatcherQueue);
        _agents.Changed += OnAgent;
        _agents.Start();
        var expire = DispatcherQueue.CreateTimer(); // finished sessions leave the rail after a while
        expire.Interval = TimeSpan.FromSeconds(20);
        expire.Tick += (_, _) => UpdateRail();
        expire.Start();
    }

    // The rail on the screen's edge: while sessions run, never over a fullscreen game.
    void UpdateRail()
    {
        if (_agents is null || !_settings.ConnectClaudeCode || !_settings.AgentsRail || _game != null)
        {
            _rail?.Hide();
            return;
        }
        _rail ??= new RailWindow(Mascot, () =>
        {
            _tab = Tab.Agents;
            SetMode(Mode.Expanded, Via.Click);
            ShowTab();
        }, () => OpenSetup(firstRun: false));
        _rail.Show(_agents.Sessions.Values, _monitor != 0 ? _monitor : MonitorFromWindow(_hwnd, 1));
    }

    void OnAgent(Agents.Session? s, string ev)
    {
        if (_mode == Mode.Expanded && _tab == Tab.Agents) FillAgents();
        UpdateRoam(); // the walking crab takes the agents' mood
        UpdateRail();
        if (s is null || !_settings.ConnectClaudeCode || AgentInFront()) return;
        bool asks = ev == "Notification" && s.State == Agents.State.NeedsYou;
        bool done = ev == "Stop" && DateTime.Now - s.Started >= TimeSpan.FromSeconds(30); // quick answers are no news
        if (!(asks || done) || !FirstWordOnClaude()) return;
        if (asks)
            Notify(new Alert(Pop.Agents, "", $"{s.Name} needs you", s.Action, MicOrange, Seconds: 6, Wide: true, Agent: s.State));
        else
            Notify(new Alert(Pop.Agents, "", $"{s.Name} is done", $"After {Elapsed(s)}", Green, Agent: s.State));
    }

    // The Claude app sends its own notification for the same moment (a question, the end of a task). Whichever of
    // the two comes first speaks; the other, within 15 seconds, stays quiet.
    // ponytail: a second, unrelated Claude event in those 15 seconds is dropped too.
    DateTime _claudeSpoke;

    bool FirstWordOnClaude()
    {
        if (DateTime.Now - _claudeSpoke < TimeSpan.FromSeconds(15)) return false;
        _claudeSpoke = DateTime.Now;
        return true;
    }

    // Where agents run: the Claude app, terminals and editors.
    static readonly HashSet<string> AgentHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "claude", "WindowsTerminal", "cmd", "powershell", "pwsh", "conhost", "Code", "Cursor", "wezterm-gui", "alacritty",
    };

    static bool AgentInFront()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        return AgentHosts.Contains(Path.GetFileNameWithoutExtension(MediaSource.ProcessPath(pid)));
    }

    static string Elapsed(Agents.Session s)
    {
        var t = DateTime.Now - s.Started;
        return t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min {t.Seconds:00} s" : $"{t.Seconds} s";
    }

    // The crab in a card or an alert: it blinks now and then, marches on the spot while its agent works and
    // waves its claws when it needs the user.
    FrameworkElement Mascot(Agents.State state, double px)
    {
        var crab = new Crab(px) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (!_motion) return crab;
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(120);
        int tick = 0, blinkAt = _rng.Next(15, 40);
        timer.Tick += (_, _) =>
        {
            tick++;
            bool blink = tick % blinkAt is 0 or 1;
            crab.Pose(step: state == Agents.State.Working && tick / 3 % 2 == 1, blink: blink,
                clawsUp: state == Agents.State.NeedsYou && tick / 2 % 2 == 1);
        };
        crab.Loaded += (_, _) => timer.Start();
        crab.Unloaded += (_, _) => timer.Stop();
        return crab;
    }
    // A turning three-quarter circle. Not a ProgressRing: the island does not load WinUI's control styles.
    FrameworkElement Spinner(Brush stroke)
    {
        var turn = new RotateTransform();
        var arc = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 10, Height = 10, Stroke = stroke, StrokeThickness = 1.6, StrokeDashArray = [13, 7],
            RenderTransform = turn, RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5), VerticalAlignment = VerticalAlignment.Center,
        };
        if (!_motion) return arc;
        var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        var a = new DoubleAnimation { From = 0, To = 360, Duration = TimeSpan.FromSeconds(0.9) };
        Storyboard.SetTarget(a, turn);
        Storyboard.SetTargetProperty(a, "Angle");
        sb.Children.Add(a);
        arc.Loaded += (_, _) => sb.Begin();
        arc.Unloaded += (_, _) => sb.Stop();
        return arc;
    }

    string? _picked; // the session shown in detail, by id; the first one when unset or gone
    (TextBlock Text, Agents.Session Session)? _agentClock; // its elapsed time, updated every second without a rebuild

    void TickAgents()
    {
        if (_agentClock is ({ } text, { State: not Agents.State.Done } s)) text.Text = Format(DateTime.Now - s.Started);
    }

    // Rings for up to four sessions on the left, the ones needing the user first; on the right, the picked one in
    // detail: what was asked, how it goes, what it does now, Claude's plan (or its last steps) and a footer with the
    // context in use and the files edited.
    void FillAgents()
    {
        var res = ((FrameworkElement)Content).Resources;
        Brush Res(string key) => (Brush)res[key];
        AgentsTab.Children.Clear();
        var sessions = _agents?.Sessions.OrderBy(p => p.Value.State switch { Agents.State.NeedsYou => 0, Agents.State.Working => 1, _ => 2 })
            .ThenByDescending(p => p.Value.Started).Take(4).ToList() ?? [];
        if (sessions.Count == 0)
        {
            AgentsTab.Children.Add(new TextBlock
            {
                Text = "Send Claude Code a message and it shows up here.", FontSize = 11.5, Foreground = Res("Muted"),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 40, 0, 0),
            });
            return;
        }
        var (id, s) = sessions.FirstOrDefault(p => p.Key == _picked) is { Key: not null } picked ? picked : sessions[0];

        var layout = new Grid { ColumnSpacing = 10, Height = 114 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(sessions.Count > 2 ? 132 : 66) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());

        // The rings, two per column.
        var rings = new Grid { RowSpacing = 4, ColumnSpacing = 4 };
        rings.RowDefinitions.Add(new RowDefinition());
        rings.RowDefinitions.Add(new RowDefinition());
        rings.ColumnDefinitions.Add(new ColumnDefinition());
        if (sessions.Count > 2) rings.ColumnDefinitions.Add(new ColumnDefinition());
        for (int k = 0; k < sessions.Count; k++)
        {
            var (key, one) = sessions[k];
            var cell = new StackPanel { Spacing = 2, Padding = new Thickness(4), CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Center,
                Background = key == id ? Res("Raised") : new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            cell.Children.Add(RailWindow.RingOf(one, Mascot, 34, 3));
            cell.Children.Add(new TextBlock { Text = RailWindow.Label(one), FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = one.State == Agents.State.NeedsYou ? new SolidColorBrush(MicOrange) : Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
            cell.Tapped += (_, _) => { _picked = key; FillAgents(); };
            ToolTipService.SetToolTip(cell, one.Name);
            Grid.SetRow(cell, k % 2);
            Grid.SetColumn(cell, k / 2);
            rings.Children.Add(cell);
        }
        layout.Children.Add(rings);

        // The picked session.
        var card = new Grid { RowSpacing = 3 };
        for (int r = 0; r < 4; r++) card.RowDefinitions.Add(new RowDefinition { Height = r == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var head = new Grid { ColumnSpacing = 8 };
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var who = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1, VerticalAlignment = VerticalAlignment.Center };
        who.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = s.Name, FontSize = 12.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Res("Fg") });
        if (s.Prompt != "") who.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "   " + s.Prompt, FontSize = 11, Foreground = Res("Muted") });
        head.Children.Add(who);
        var state = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        if (s.State == Agents.State.Working) state.Children.Add(Spinner(Res("Fg")));
        else state.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(s.State == Agents.State.NeedsYou ? MicOrange : Green), VerticalAlignment = VerticalAlignment.Center });
        state.Children.Add(new TextBlock { Text = s.State switch { Agents.State.NeedsYou => "Needs you", Agents.State.Done => "Done", _ => "Working" },
            FontSize = 10.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Res("Fg"), VerticalAlignment = VerticalAlignment.Center });
        var pill = new Border { Background = Res("Line"), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 9, 2), Child = state };
        Grid.SetColumn(pill, 1);
        head.Children.Add(pill);
        card.Children.Add(head);

        var now = new Grid();
        now.Children.Add(new TextBlock { Text = OneLine(s.Action), FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Res("Fg"), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1, Margin = new Thickness(0, 0, 48, 0) });
        var elapsed = new TextBlock { Text = s.State == Agents.State.Done ? s.Updated.ToString("t") : Format(DateTime.Now - s.Started), FontSize = 10, FontFamily = (FontFamily)res["Mono"],
            Foreground = Res("Muted"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        now.Children.Add(elapsed);
        _agentClock = (elapsed, s);
        Grid.SetRow(now, 1);
        card.Children.Add(now);

        // The plan around where it is (the step under way and the next one, else the last ones done), or the last steps.
        var lines = new StackPanel { Spacing = 1 };
        IEnumerable<(string Glyph, string Text, bool Bright)> shown = s.Plan.Count > 0
            ? PlanWindow(s.Plan).Select(p => (p.Status switch { "completed" => "\uE73E", "in_progress" => "\uE768", _ => "\uEA3A" }, p.Text, p.Status == "in_progress"))
            : s.Steps.Skip(s.State == Agents.State.Working ? 1 : 0).Take(2).Select(p => (p.Glyph, p.Text, false));
        foreach (var (glyph, text, bright) in shown)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            line.Children.Add(new FontIcon { Glyph = glyph, FontSize = 9.5, FontFamily = (FontFamily)res["Icons"], Foreground = bright ? Res("Fg") : Res("Muted") });
            line.Children.Add(new TextBlock { Text = OneLine(text), FontSize = 10.5, Foreground = bright ? Res("Fg") : Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
            lines.Children.Add(line);
        }
        Grid.SetRow(lines, 2);
        card.Children.Add(lines);

        var facts = new List<string>();
        if (s.Plan.Count > 0) facts.Add($"Plan {s.ProgressLabel}");
        if (s.Context is { } c) facts.Add($"{c * 100:0}% of context");
        if (s.Files.Count > 0) facts.Add($"{s.Files.Count} file{(s.Files.Count > 1 ? "s" : "")} edited");
        var footer = new TextBlock { Text = string.Join("  ·  ", facts), FontSize = 10, Foreground = Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
        Grid.SetRow(footer, 3);
        card.Children.Add(footer);

        var frame = new Border { Background = Res("Raised"), CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 9, 12, 9), Child = card };
        Grid.SetColumn(frame, 1);
        layout.Children.Add(frame);
        AgentsTab.Children.Add(layout);
    }

    // Two lines of the plan: the step under way and the next, or, near the end, the last ones done.
    static IEnumerable<(string Text, string Status)> PlanWindow(List<(string Text, string Status)> plan)
    {
        int at = plan.FindIndex(p => p.Status != "completed");
        int from = at < 0 ? plan.Count - 2 : at;
        return plan.Skip(Math.Max(0, Math.Min(from, plan.Count - 2))).Take(2);
    }
}