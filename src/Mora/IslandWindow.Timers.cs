using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Mora;

// One countdown at a time, started from the Timers tab; it pops an alert when done.
// A focus session chains rounds by itself: 25 min of focus, a 5 min break, a 15 min one every 4th round,
// until stopped.
public sealed partial class IslandWindow
{
    record RunningTimer(DateTime End, int Seconds, string Label, int Round = 0, bool Break = false); // Round 0: a plain timer

    RunningTimer? _timer;
    DispatcherQueueTimer? _timerTick;
    int _focusWork = 1500, _focusBreak = 300; // shortened only by the simulate hook

    void StartTimer(int seconds) => Run(new(DateTime.UtcNow.AddSeconds(seconds), seconds, $"{seconds / 60} min timer"));

    void StartFocus(int round, bool rest)
    {
        int seconds = !rest ? _focusWork : round % 4 == 0 ? _focusBreak * 3 : _focusBreak;
        Run(new(DateTime.UtcNow.AddSeconds(seconds), seconds, rest ? $"Break · round {round} done" : $"Focus · round {round}", round, rest));
    }

    void Run(RunningTimer timer)
    {
        _timer = timer;
        if (_timerTick is null)
        {
            _timerTick = DispatcherQueue.CreateTimer();
            _timerTick.Interval = TimeSpan.FromSeconds(1);
            _timerTick.Tick += (_, _) => OnTimerTick();
        }
        _timerTick.Start();
        FillTimer();
        EvaluateRest(); // the countdown pill or bubble, when enabled
    }

    void OnTimerTick()
    {
        if (_timer is not { } t) return;
        if (DateTime.UtcNow < t.End)
        {
            FillTimer();
            return;
        }
        if (t.Round > 0)
        {
            int round = t.Break ? t.Round + 1 : t.Round;
            StartFocus(round, !t.Break);
            Notify(t.Break
                ? new Alert(Pop.Timers, "", "Back to focus", $"Round {round}", _theme.Warm, ActionLabel: "Stop", Action: StopTimer)
                : new Alert(Pop.Timers, "", "Break time", $"{_timer!.Seconds / 60} min, round {round} done", _theme.Calm, ActionLabel: "Stop", Action: StopTimer));
            return;
        }
        StopTimer();
        Notify(new Alert(Pop.Timers, "", t.Label, "Timer finished", _theme.Warm, ActionLabel: "Again", Action: () => StartTimer(t.Seconds)));
    }

    void StopTimer()
    {
        _timer = null;
        _timerTick?.Stop();
        FillTimer();
        EvaluateRest();
    }

    void FillTimer()
    {
        TimerIdle.Visibility = _timer is null ? Visibility.Visible : Visibility.Collapsed;
        TimerRunning.Visibility = _timer is null ? Visibility.Collapsed : Visibility.Visible;
        if (_timer is not { } t) return;
        TimerLabel.Text = t.Label;
        var left = TimeSpan.FromSeconds(Math.Ceiling((t.End - DateTime.UtcNow).TotalSeconds));
        TimerLeft.Text = CompactTimerText.Text = Format(left);
        BubbleText.Text = _tallBubble ? $"{(int)left.TotalMinutes:00}\n{left.Seconds:00}" : Format(left); // minutes over seconds in the tall pill
        TimerLeft.Foreground = CompactTimerText.Foreground = CompactTimerIcon.Foreground = BubbleText.Foreground = TimerFill.Background = new SolidColorBrush(t.Break ? _theme.Calm : _theme.Warm);
        var done = t.Seconds > 0 ? Math.Clamp(1 - left.TotalSeconds / t.Seconds, 0, 1) : 0;
        TimerDone.Width = new GridLength(done, GridUnitType.Star);
        TimerRest.Width = new GridLength(1 - done, GridUnitType.Star);
    }

    void TimerStart_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) =>
        StartTimer(int.Parse((string)((FrameworkElement)sender).Tag));

    void FocusStart_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => StartFocus(1, rest: false);

    void TimerAdd_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (_timer is { } t) _timer = t with { End = t.End.AddMinutes(1), Seconds = t.Seconds + 60 };
        FillTimer();
    }

    void TimerCancel_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => StopTimer();
}
