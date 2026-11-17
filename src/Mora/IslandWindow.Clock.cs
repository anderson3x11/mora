using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Mora;

// The time and date, in up to four places, each one a setting: a resting pill (or the bubble beside the music
// pill), a tall pill beside the open panel when no timer needs it, the top right of the panel off the Media tab,
// and the game music card. Formats follow Windows' own (24 or 12 hour, language).
public sealed partial class IslandWindow
{
    DispatcherQueueTimer? _clockTick;
    bool _bubbleClock; // the bubble shows the time rather than a countdown

    bool ClockPill => _settings.ShowClockPill;

    // The time, and the date when the format has one: "14:32" + "Lun. 27 oct.", "14:32:07", "14:32" + "Lundi 27 octobre".
    (string Time, string? Date) ClockParts(DateTime now) => _settings.ClockFormat switch
    {
        ClockStyle.Time => (now.ToString("t"), null),
        ClockStyle.TimeWithSeconds => (now.ToString("T"), null),
        ClockStyle.FullDate => (now.ToString("t"), Capitalized(now.ToString("dddd d MMMM"))),
        _ => (now.ToString("t"), Capitalized(now.ToString("ddd d MMM"))),
    };

    static string Capitalized(string s) => s.Length == 0 ? s : char.ToUpper(s[0], CultureInfo.CurrentCulture) + s[1..];

    // A time alone fits the timer's short pill; with a date it needs the music pill's width.
    double ClockPillWidth => _settings.ClockFormat is ClockStyle.Time or ClockStyle.TimeWithSeconds ? 132 : 214;

    // The time bright and firm, the date quieter beside it.
    void TwoTone(TextBlock text, DateTime now)
    {
        var res = ((FrameworkElement)Content).Resources;
        var (time, date) = ClockParts(now);
        text.Inlines.Clear();
        text.Inlines.Add(new Run { Text = time, Foreground = (Brush)res["Fg"], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        if (date != null) text.Inlines.Add(new Run { Text = "   " + date, Foreground = (Brush)res["Muted"], FontWeight = Microsoft.UI.Text.FontWeights.Normal });
    }

    void FillClock()
    {
        var now = DateTime.Now;
        var res = ((FrameworkElement)Content).Resources;
        if (!TimerPill && !MediaPill && !AgentPill)
        {
            CompactTimerIcon.Visibility = Visibility.Collapsed;
            (CompactTimerText.FontFamily, CompactTimerText.HorizontalAlignment) = ((FontFamily)res["Text"], HorizontalAlignment.Center);
            TwoTone(CompactTimerText, now);
        }
        if (_bubbleClock)
        {
            if (_tallBubble) FillFace(now);
            else
            {
                (BubbleText.FontFamily, BubbleText.Foreground) = ((FontFamily)res["Text"], (Brush)res["Fg"]);
                BubbleText.Text = now.ToString("t");
            }
        }
        TwoTone(PanelClock, now);
        GameClock.Text = now.ToString("t");
        if (_mode == Mode.Expanded && _tab == Tab.Agents) TickAgents(); // the elapsed time
        if (_mode == Mode.Expanded && _tab == Tab.Home) FillHome();     // the countdown, "in 12 min"
        if (_mode == Mode.Compact && AgentPill) FillAgentPill();          // its elapsed time
    }

    // The clock face beside the open panel: hands, then the time and the day under it.
    void FillFace(DateTime now)
    {
        if (ClockTicks.Children.Count == 0)
            for (int i = 0; i < 12; i++) // the hour marks, the quarters longer and brighter
            {
                bool quarter = i % 3 == 0;
                var mark = new Microsoft.UI.Xaml.Shapes.Rectangle
                {
                    Width = quarter ? 1.6 : 1, Height = quarter ? 4 : 2.5,
                    Fill = (Brush)((FrameworkElement)Content).Resources[quarter ? "Fg" : "Muted"],
                    RenderTransform = new RotateTransform { Angle = i * 30, CenterX = quarter ? 0.8 : 0.5, CenterY = 17 },
                };
                Canvas.SetLeft(mark, 19 - mark.Width / 2);
                Canvas.SetTop(mark, 2);
                ClockTicks.Children.Add(mark);
            }
        HourHand.Angle = (now.Hour % 12 + now.Minute / 60.0) * 30;
        MinuteHand.Angle = (now.Minute + now.Second / 60.0) * 6;
        SecondHand.Angle = now.Second * 6;
        FaceTime.Text = now.ToString("t");
        FaceDay.Text = Capitalized(now.ToString("ddd d"));
    }

    // Ticks once a second while the island shows anything; hidden, it rests.
    void UpdateClockTick()
    {
        UpdateRoam(); // the crab walks while the panel is open
        if (_clockTick is null)
        {
            _clockTick = DispatcherQueue.CreateTimer();
            _clockTick.Interval = TimeSpan.FromSeconds(1);
            _clockTick.Tick += (_, _) => FillClock();
        }
        if (_mode == Mode.Hidden) _clockTick.Stop();
        else
        {
            FillClock();
            _clockTick.Start();
        }
    }
}
