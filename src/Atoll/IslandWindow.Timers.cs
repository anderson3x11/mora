using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace Atoll;

// One countdown at a time, started from the Timers tab; it pops an alert when done.
public sealed partial class IslandWindow
{
    static readonly Color Amber = Color.FromArgb(255, 0xFF, 0xB4, 0x54);

    record RunningTimer(DateTime End, int Seconds, string Label);

    RunningTimer? _timer;
    DispatcherQueueTimer? _timerTick;

    void StartTimer(int seconds)
    {
        _timer = new(DateTime.UtcNow.AddSeconds(seconds), seconds, seconds == 1500 ? "Focus" : $"{seconds / 60} min timer");
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
        _timer = null;
        _timerTick!.Stop();
        FillTimer();
        EvaluateRest();
        Notify(new Alert(Pop.Timers, "", t.Label, "Timer finished", Amber, ActionLabel: "Again", Action: () => StartTimer(t.Seconds)));
    }

    void FillTimer()
    {
        TimerIdle.Visibility = _timer is null ? Visibility.Visible : Visibility.Collapsed;
        TimerRunning.Visibility = _timer is null ? Visibility.Collapsed : Visibility.Visible;
        if (_timer is not { } t) return;
        TimerLabel.Text = t.Label;
        TimerLeft.Text = CompactTimerText.Text = BubbleText.Text = Format(TimeSpan.FromSeconds(Math.Ceiling((t.End - DateTime.UtcNow).TotalSeconds)));
    }

    void TimerStart_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) =>
        StartTimer(int.Parse((string)((FrameworkElement)sender).Tag));

    void TimerAdd_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (_timer is { } t) _timer = t with { End = t.End.AddMinutes(1), Seconds = t.Seconds + 60 };
        FillTimer();
    }

    void TimerCancel_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        _timer = null;
        _timerTick?.Stop();
        FillTimer();
        EvaluateRest();
    }
}
