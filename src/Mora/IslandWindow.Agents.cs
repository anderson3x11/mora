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

    Microsoft.UI.Dispatching.DispatcherQueueTimer? _redraw;

    // Agents send several events a second: the tab, the live activity and the walking crab redraw once for a burst
    // of them, at most every 250 ms, rather than once per event.
    void Redraw()
    {
        if (_redraw is null)
        {
            _redraw = DispatcherQueue.CreateTimer();
            _redraw.Interval = TimeSpan.FromMilliseconds(250);
            _redraw.IsRepeating = false;
            _redraw.Tick += (_, _) =>
            {
                if (_mode == Mode.Expanded && _tab == Tab.Agents) FillAgents();
                UpdateRoam(); // the walking crab takes the agents' mood
                EvaluateRest(); // the live activity comes, changes or goes
            };
        }
        if (!_redraw.IsRunning) _redraw.Start();
    }

    void WireAgents()
    {
        _agents = new Agents(DispatcherQueue);
        _agents.Changed += OnAgent;
        _agents.Start();
    }

    // The live activity: the session that matters most right now (waiting for the user first, then the latest at work).
    Agents.Session? LiveAgent => !_settings.ConnectClaudeCode || !_settings.AgentsLive || _agents is null ? null
        : _agents.Sessions.Values.Where(s => s.State != Agents.State.Done)
            .OrderBy(s => s.State == Agents.State.NeedsYou ? 0 : 1).ThenByDescending(s => s.Updated).FirstOrDefault();

    bool AgentPill => LiveAgent != null;
    string _agentPillKey = "";

    // The resting island while an agent works: its logo in the ring of its progress, the project, and how it goes.
    void FillAgentPill()
    {
        if (LiveAgent is not { } s) return;
        int busy = _agents!.Sessions.Values.Count(x => x.State != Agents.State.Done);
        var key = $"{s.Name}:{s.State}:{s.ProgressLabel}";
        if (key != _agentPillKey) // the badge only when it changes: the ring keeps its pulse
        {
            _agentPillKey = key;
            CompactAgentBadge.Content = AgentBadge(s, 24, 2.4);
        }
        CompactAgentName.Text = busy > 1 ? $"{s.Name}  +{busy - 1}" : s.Name;
        bool asks = s.State == Agents.State.NeedsYou;
        CompactAgentState.Text = asks ? "Needs you" : Format(DateTime.Now - s.Started);
        CompactAgentState.Foreground = asks ? new SolidColorBrush(MicOrange) : (Brush)((FrameworkElement)Content).Resources["Muted"];
        CompactAgentState.FontFamily = (FontFamily)((FrameworkElement)Content).Resources[asks ? "Text" : "Mono"];
    }

    // An agent's mark: its app's logo inside a ring that fills with its plan (or the context in use), white while it
    // works, orange and breathing when it waits for the user, green once done. Without the app, a colored initial.
    FrameworkElement AgentBadge(Agents.Session s, double size, double stroke)
    {
        var color = s.State switch { Agents.State.NeedsYou => new SolidColorBrush(MicOrange), Agents.State.Done => new SolidColorBrush(Green), _ => new SolidColorBrush(Microsoft.UI.Colors.White) };
        var badge = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        badge.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x3A, 0x3A, 0x3C)), StrokeThickness = stroke });
        bool asks = s.State == Agents.State.NeedsYou;
        double fill = asks ? 1 : Math.Clamp(s.Progress ?? 0, 0, 1), units = Math.PI * (size - stroke) / stroke;
        if (fill > 0)
        {
            var arc = new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Stroke = color, StrokeThickness = stroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeDashCap = PenLineCap.Round,
                StrokeDashArray = fill >= 1 ? null : [Math.Max(0.01, fill * units - 1), units * 2],
                RenderTransform = new RotateTransform { Angle = -90 }, RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            };
            badge.Children.Add(arc);
            if (asks && _motion) Crab.Pulse(arc);
        }
        double inner = size - stroke * 2 - Math.Max(3, size * 0.12) * 2;
        badge.Children.Add(AgentLogo.For(s.Agent) is { } logo
            ? new Border { Width = inner, Height = inner, CornerRadius = new CornerRadius(AgentLogo.IsFile(s.Agent) ? 0 : inner * 0.24), Child = new Image { Source = logo, Stretch = AgentLogo.IsFile(s.Agent) ? Stretch.Uniform : Stretch.UniformToFill } }
            : new Border
            {
                Width = inner, Height = inner, CornerRadius = new CornerRadius(inner / 2), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xD9, 0x77, 0x57)),
                Child = new TextBlock { Text = s.Agent[..1], FontSize = inner * 0.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
        return badge;
    }

    // Under a ring: the plan or the context, else how long it has been working.
    static string Label(Agents.Session s) => s.State switch
    {
        Agents.State.NeedsYou => "Your turn",
        _ when s.ProgressLabel is { Length: > 0 } label => label,
        Agents.State.Done => "Done",
        _ => (DateTime.Now - s.Started).TotalMinutes is var m && m < 1 ? "Now" : $"{(int)m} min",
    };

    void OnAgent(Agents.Session? s, string ev)
    {
        Redraw();
        if (s is null || !_settings.ConnectClaudeCode || AgentInFront()) return;
        bool asks = ev == "Notification" && s.State == Agents.State.NeedsYou;
        bool done = ev == "Stop" && DateTime.Now - s.Started >= TimeSpan.FromSeconds(30); // quick answers are no news
        if (!(asks || done) || !FirstWordOnClaude()) return;
        var logo = AgentLogo.For(s.Agent);
        if (asks)
            Notify(new Alert(Pop.Agents, "", $"{s.Name} needs you", s.Action, MicOrange, Seconds: 6, Wide: true, Art: logo));
        else
            Notify(new Alert(Pop.Agents, "", $"{s.Name} is done", $"After {Elapsed(s)}", Green, Art: logo));
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
        if (_motion) crab.Animate(state);
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

    // Like the Batteries widget: a large ring per session (up to four, the ones needing the user first), the logo
    // inside, its progress under it. Beside them, past a hairline, the picked one in words: what was asked, what it does
    // now, Claude's plan (or its last steps), and what it has used. A ring picks its session.
    void FillAgents()
    {
        var res = ((FrameworkElement)Content).Resources;
        Brush Res(string key) => (Brush)res[key];
        AgentsTab.Children.Clear();
        var sessions = _agents?.Sessions.OrderBy(p => p.Value.State switch { Agents.State.NeedsYou => 0, Agents.State.Working => 1, _ => 2 })
            .ThenByDescending(p => p.Value.Started).Take(4).ToList() ?? [];
        if (sessions.Count == 0)
        {
            var empty = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 0) };
            empty.Children.Add(new TextBlock { Text = "No agent at work", FontFamily = (FontFamily)res["Display"], FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Res("Fg"), HorizontalAlignment = HorizontalAlignment.Center });
            empty.Children.Add(new TextBlock { Text = "Send Claude Code a message and it shows up here", FontSize = 13, Foreground = Res("Muted"), HorizontalAlignment = HorizontalAlignment.Center });
            AgentsTab.Children.Add(empty);
            return;
        }
        var (id, s) = sessions.FirstOrDefault(p => p.Key == _picked) is { Key: not null } picked ? picked : sessions[0];

        var layout = new Grid { ColumnSpacing = 22, Height = 126 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());

        var rings = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (key, one) in sessions)
        {
            bool mine = key == id, asks = one.State == Agents.State.NeedsYou;
            var cell = new StackPanel { Spacing = 7, Width = 66, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Opacity = mine || sessions.Count == 1 ? 1 : 0.55 };
            cell.Children.Add(AgentBadge(one, 60, 5));
            cell.Children.Add(new TextBlock { Text = Label(one), FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = asks ? new SolidColorBrush(MicOrange) : Res("Fg"), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
            cell.Tapped += (_, _) => { _picked = key; FillAgents(); };
            ToolTipService.SetToolTip(cell, one.Name);
            rings.Children.Add(cell);
        }
        layout.Children.Add(rings);
        var line = new Microsoft.UI.Xaml.Shapes.Rectangle { Fill = Hairline, Margin = new Thickness(0, 12, 0, 12) };
        Grid.SetColumn(line, 1);
        layout.Children.Add(line);

        var detail = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var head = new Grid { ColumnSpacing = 10 };
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock { Text = s.Name, FontFamily = (FontFamily)res["Display"], FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Res("Fg"), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
        var elapsed = new TextBlock { Text = s.State == Agents.State.Done ? s.Updated.ToString("t") : Format(DateTime.Now - s.Started), FontSize = 12, Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(elapsed, 1);
        head.Children.Add(elapsed);
        _agentClock = (elapsed, s);
        detail.Children.Add(head);
        var state = s.State switch { Agents.State.NeedsYou => ("Needs you", new SolidColorBrush(MicOrange)), Agents.State.Done => ("Done", new SolidColorBrush(Green)), _ => ("Working", (SolidColorBrush)Res("Muted")) };
        var now = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1, Margin = new Thickness(0, 0, 0, 4) };
        now.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = state.Item1, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = state.Item2 });
        now.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "  " + OneLine(s.Action), FontSize = 13, Foreground = Res("Fg") });
        detail.Children.Add(now);

        IEnumerable<(string Glyph, string Text, bool Bright)> shown = s.Plan.Count > 0
            ? PlanWindow(s.Plan).Select(p => (p.Status switch { "completed" => "\uE73E", "in_progress" => "\uE768", _ => "\uEA3A" }, p.Text, p.Status == "in_progress"))
            : s.Steps.Skip(s.State == Agents.State.Working ? 1 : 0).Take(2).Select(p => (p.Glyph, p.Text, false));
        foreach (var (glyph, text, bright) in shown)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 10, FontFamily = (FontFamily)res["Icons"], Foreground = bright ? Res("Fg") : Res("Muted") });
            row.Children.Add(new TextBlock { Text = OneLine(text), FontSize = 12, Foreground = bright ? Res("Fg") : Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
            detail.Children.Add(row);
        }
        var facts = new List<string>();
        if (s.Prompt != "") facts.Add($"\u201C{s.Prompt}\u201D");
        if (s.Context is { } c) facts.Add($"{c * 100:0}% of context");
        if (s.Files.Count > 0) facts.Add($"{s.Files.Count} file{(s.Files.Count > 1 ? "s" : "")} edited");
        if (facts.Count > 0) detail.Children.Add(new TextBlock { Text = string.Join("  \u00B7  ", facts), FontSize = 11, Foreground = Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1, Margin = new Thickness(0, 4, 0, 0) });
        Grid.SetColumn(detail, 2);
        layout.Children.Add(detail);
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