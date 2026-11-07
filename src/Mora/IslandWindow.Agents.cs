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

    void WireAgents()
    {
        _agents = new Agents(DispatcherQueue);
        _agents.Changed += OnAgent;
        _agents.Start();
    }

    void OnAgent(Agents.Session? s, string ev)
    {
        if (_mode == Mode.Expanded && _tab == Tab.Agents) FillAgents();
        UpdateRoam(); // the walking crab takes the agents' mood
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

    // Up to two sessions side by side, the ones needing the user first: who asked what, how it is going, what
    // it is doing now and the steps just before.
    // ponytail: past two sessions the rest wait off screen; a scrolling list if people run more at once.
    void FillAgents()
    {
        var res = ((FrameworkElement)Content).Resources;
        Brush Res(string key) => (Brush)res[key];
        AgentsTab.Children.Clear();
        var sessions = _agents?.Sessions.Values.OrderBy(s => s.State switch { Agents.State.NeedsYou => 0, Agents.State.Working => 1, _ => 2 })
            .ThenByDescending(s => s.Started).Take(2).ToList() ?? [];
        if (sessions.Count == 0)
        {
            AgentsTab.Children.Add(new TextBlock
            {
                Text = "Send Claude Code a message and it shows up here.", FontSize = 11.5, Foreground = Res("Muted"),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 40, 0, 0),
            });
            return;
        }
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int k = 0; k < sessions.Count; k++)
        {
            var s = sessions[k];
            var card = new Grid { RowSpacing = 3 };
            card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Who: the mascot, the project and what was asked, and the state.
            var head = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 0, 0, 4) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(8), Background = Res("Line"), Child = Mascot(s.State, 2) });
            var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            who.Children.Add(new TextBlock { Text = s.Name, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Res("Fg"), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
            if (s.Prompt != "") who.Children.Add(new TextBlock { Text = s.Prompt, FontSize = 10.5, Foreground = Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
            Grid.SetColumn(who, 1);
            head.Children.Add(who);
            var state = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            if (s.State == Agents.State.Working) state.Children.Add(Spinner(Res("Fg")));
            else state.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(s.State == Agents.State.NeedsYou ? MicOrange : Green), VerticalAlignment = VerticalAlignment.Center });
            state.Children.Add(new TextBlock
            {
                Text = s.State switch { Agents.State.NeedsYou => "Needs you", Agents.State.Done => "Done", _ => "Working" },
                FontSize = 10.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Res("Fg"), VerticalAlignment = VerticalAlignment.Center,
            });
            var pill = new Border { Background = Res("Line"), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 3, 9, 3), VerticalAlignment = VerticalAlignment.Center, Child = state };
            Grid.SetColumn(pill, 2);
            head.Children.Add(pill);
            card.Children.Add(head);

            // Now: the current step, with the time since the prompt.
            var now = new Grid();
            now.Children.Add(new TextBlock { Text = OneLine(s.Action), FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Res("Fg"), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1, Margin = new Thickness(0, 0, 44, 0) });
            now.Children.Add(new TextBlock
            {
                Text = s.State == Agents.State.Done ? s.Started.ToString("t") : Format(DateTime.Now - s.Started), FontSize = 10, FontFamily = (FontFamily)res["Mono"],
                Foreground = Res("Muted"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetRow(now, 1);
            card.Children.Add(now);

            // Before: the steps just done.
            var before = new StackPanel { Spacing = 1 };
            foreach (var (glyph, text) in s.Steps.Skip(s.State == Agents.State.Working ? 1 : 0).Take(2))
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                line.Children.Add(new FontIcon { Glyph = glyph, FontSize = 10, FontFamily = (FontFamily)res["Icons"], Foreground = Res("Muted") });
                line.Children.Add(new TextBlock { Text = OneLine(text), FontSize = 10.5, Foreground = Res("Muted"), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
                before.Children.Add(line);
            }
            Grid.SetRow(before, 2);
            card.Children.Add(before);

            var frame = new Border { Background = Res("Raised"), CornerRadius = new CornerRadius(14), Padding = new Thickness(11, 9, 11, 9), Child = card };
            Grid.SetColumn(frame, k);
            if (sessions.Count == 1) Grid.SetColumnSpan(frame, 2); // alone, it takes the whole width
            grid.Children.Add(frame);
        }
        AgentsTab.Children.Add(grid);
    }
}