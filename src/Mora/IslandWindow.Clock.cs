using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
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

    // "14:32", "14:32:07", "14:32 · lun. 27 oct.", "Lundi 27 octobre · 14:32"
    string ClockText(DateTime now) => _settings.ClockFormat switch
    {
        ClockStyle.Time => now.ToString("t"),
        ClockStyle.TimeWithSeconds => now.ToString("T"),
        ClockStyle.FullDate => Capitalized(now.ToString("dddd d MMMM")) + " · " + now.ToString("t"),
        _ => now.ToString("t") + " · " + now.ToString("ddd d MMM"),
    };

    static string Capitalized(string s) => s.Length == 0 ? s : char.ToUpper(s[0], CultureInfo.CurrentCulture) + s[1..];

    // A time alone fits the timer's short pill; with a date it needs the music pill's width.
    double ClockPillWidth => _settings.ClockFormat is ClockStyle.Time or ClockStyle.TimeWithSeconds ? 132 : 214;

    void FillClock()
    {
        var now = DateTime.Now;
        var res = ((FrameworkElement)Content).Resources;
        if (!TimerPill && !MediaPill)
        {
            CompactTimerIcon.Glyph = "";
            CompactTimerText.Text = ClockText(now);
            CompactTimerIcon.Foreground = CompactTimerText.Foreground = (Brush)res["Fg"];
        }
        if (_bubbleClock)
        {
            BubbleText.Foreground = (Brush)res["Fg"];
            if (_tallBubble)
            {
                // Hours over minutes, and the day under them, smaller.
                BubbleText.Inlines.Clear();
                var t = now.ToString("t", CultureInfo.CurrentCulture).Split(':', ' ');
                BubbleText.Inlines.Add(new Run { Text = t[0] });
                BubbleText.Inlines.Add(new LineBreak());
                BubbleText.Inlines.Add(new Run { Text = t.Length > 1 ? t[1] : "" });
                BubbleText.Inlines.Add(new LineBreak());
                BubbleText.Inlines.Add(new Run { Text = now.ToString("d MMM"), FontSize = 11, Foreground = (Brush)res["Muted"] });
            }
            else BubbleText.Text = now.ToString("t");
        }
        PanelClock.Text = ClockText(now);
        GameClock.Text = now.ToString("t");
    }

    // Ticks once a second while the island shows anything; hidden, it rests.
    void UpdateClockTick()
    {
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
