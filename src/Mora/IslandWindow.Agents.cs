using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
        if (s is null || !_settings.ConnectClaudeCode || AgentInFront()) return;
        bool asks = ev == "Notification" && s.State == Agents.State.NeedsYou;
        bool done = ev == "Stop" && DateTime.Now - s.Started >= TimeSpan.FromSeconds(30); // quick answers are no news
        if (!(asks || done) || !FirstWordOnClaude()) return;
        if (asks)
            Notify(new Alert(Pop.Agents, "", $"{s.Name} needs you", s.Action, MicOrange, Seconds: 6, Wide: true));
        else
            Notify(new Alert(Pop.Agents, "", $"{s.Name} is done", $"After {Elapsed(s)}", Green));
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

    void FillAgents()
    {
        var res = ((FrameworkElement)Content).Resources;
        AgentsTab.Children.Clear();
        var sessions = _agents?.Sessions.Values.OrderBy(s => s.State switch { Agents.State.NeedsYou => 0, Agents.State.Working => 1, _ => 2 })
            .ThenByDescending(s => s.Started).Take(3).ToList() ?? []; // what fits the panel's fixed height
        if (sessions.Count == 0)
        {
            AgentsTab.Children.Add(new TextBlock
            {
                Text = "Send Claude Code a message and it shows up here.", FontSize = 11.5, Foreground = (Brush)res["Muted"],
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 40, 0, 0),
            });
            return;
        }
        foreach (var s in sessions)
        {
            var row = new Grid { Background = (Brush)res["Raised"], CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 0, 12, 0), Height = RowHeight, ColumnSpacing = 9 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center,
                Fill = s.State switch { Agents.State.NeedsYou => new SolidColorBrush(MicOrange), Agents.State.Done => new SolidColorBrush(Green), _ => (Brush)res["Ink"] },
            });
            var text = new TextBlock { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1, VerticalAlignment = VerticalAlignment.Center };
            text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = s.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = (Brush)res["Fg"] });
            text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "  " + OneLine(s.Action), Foreground = (Brush)res["Muted"] });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var time = new TextBlock
            {
                Text = s.State == Agents.State.Done ? s.Started.ToString("t") : Elapsed(s), FontSize = 10, FontFamily = (FontFamily)res["Mono"],
                Foreground = (Brush)res["Muted"], VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(time, 2);
            row.Children.Add(time);
            AgentsTab.Children.Add(row);
        }
    }
}
