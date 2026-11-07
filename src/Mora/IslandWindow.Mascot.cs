using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Mora;

// Mora's mascot, a pixel crab, drawn one rectangle per pixel. Each pose is a redraw of a small map: legs
// together or mid-step, claws down or raised, eyes looking left, ahead or right, or closed in a blink.
sealed class Crab : Grid
{
    static readonly string[] Shell =
    [
        ".C.......C.",
        "CC.......CC",
        ".C.BBBBB.C.",
        "..BBBBBBB..",
        ".BBBBBBBBB.", // the eyes are drawn over this row
        "..BBBBBBB..",
    ];
    static readonly string[] ClawsUp = ["C.C.....C.C", ".CC.....CC."];
    static readonly string[] Legs = [".L.L...L.L.", "L..L...L..L"];
    static readonly string[] LegsStep = ["..L.L.L.L..", ".L..L.L..L."];
    static readonly SolidColorBrush Coral = new(Windows.UI.Color.FromArgb(255, 0xFF, 0x7A, 0x59));
    static readonly SolidColorBrush Eye = new(Windows.UI.Color.FromArgb(255, 0x1A, 0x10, 0x0E));

    readonly double _px;
    readonly Canvas _canvas = new();
    (bool, int, bool, bool)? _pose;

    public Crab(double px)
    {
        _px = px;
        (Width, Height) = (11 * px, 8 * px);
        Children.Add(_canvas);
        Pose();
    }

    // look: -1 left, 0 ahead, 1 right.
    public void Pose(bool step = false, int look = 0, bool blink = false, bool clawsUp = false)
    {
        if (_pose == (step, look, blink, clawsUp)) return;
        _pose = (step, look, blink, clawsUp);
        _canvas.Children.Clear();
        string[] rows = [.. clawsUp ? ClawsUp : Shell[..2], .. Shell[2..], .. step ? LegsStep : Legs];
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                if (rows[y][x] != '.') Pixel(x, y, Coral);
        if (!blink)
        {
            Pixel(3 + look, 4, Eye);
            Pixel(7 + look, 4, Eye);
        }
    }

    void Pixel(int x, int y, Brush fill)
    {
        var r = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = _px, Height = _px, Fill = fill };
        Canvas.SetLeft(r, x * _px);
        Canvas.SetTop(r, y * _px);
        _canvas.Children.Add(r);
    }
}

// The crab wandering along the open panel's bottom margin, when the setting is on. It walks a little, its legs
// stepping and its eyes on where it goes, then stops: it looks around, snaps its claws or just sits, blinking
// now and then. It shows the agents' mood: hurrying while one works, stopping to wave when one needs you.
public sealed partial class IslandWindow
{
    DispatcherQueueTimer? _roam;
    Crab? _walker;
    double _walkX = 60, _walkDir = 1;
    int _tick, _pauseTicks, _idle, _blinkTicks;
    readonly Random _rng = new();

    void UpdateRoam()
    {
        bool on = _settings.MascotRoams && _mode == Mode.Expanded;
        MascotLane.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on)
        {
            _roam?.Stop();
            return;
        }
        if (_walker is null)
        {
            _walker = new Crab(2); // whole pixels: crisp
            MascotLane.Children.Add(_walker);
            Canvas.SetLeft(_walker, _walkX);
        }
        if (!_motion) return;
        if (_roam is null)
        {
            _roam = DispatcherQueue.CreateTimer();
            _roam.Interval = TimeSpan.FromMilliseconds(40);
            _roam.Tick += (_, _) => Roam();
        }
        _roam.Start();
    }

    Agents.State Mood =>
        _agents?.Sessions.Values.Any(s => s.State == Agents.State.NeedsYou) == true ? Agents.State.NeedsYou
        : _agents?.Sessions.Values.Any(s => s.State == Agents.State.Working) == true ? Agents.State.Working : Agents.State.Done;

    // Every 40 ms. Kept clear of the island's rounded bottom corners.
    void Roam()
    {
        if (_walker is null) return;
        _tick++;
        if (_blinkTicks > 0) _blinkTicks--;
        else if (_rng.NextDouble() < 0.012) _blinkTicks = 3;
        bool blink = _blinkTicks > 0;
        var mood = Mood;

        if (mood == Agents.State.NeedsYou) // stops, faces the user and waves
        {
            _walker.Pose(look: 0, blink: blink, clawsUp: _tick / 5 % 2 == 1);
            Canvas.SetTop(_walker, 0);
            return;
        }

        if (_pauseTicks > 0)
        {
            _pauseTicks--;
            switch (_idle)
            {
                case 0: // looks one way, then the other
                    _walker.Pose(look: _pauseTicks / 18 % 3 - 1, blink: blink);
                    break;
                case 1: // snaps its claws, three times, then rests
                    _walker.Pose(blink: blink, clawsUp: _pauseTicks % 50 < 24 && _pauseTicks / 4 % 2 == 1);
                    break;
                default: // sits
                    _walker.Pose(blink: blink);
                    break;
            }
            Canvas.SetTop(_walker, 1);
            return;
        }

        double min = 24, max = MascotLane.ActualWidth - 24 - _walker.Width;
        _walkX += _walkDir * (mood == Agents.State.Working ? 1.2 : 0.7);
        if (_walkX <= min || _walkX >= max)
        {
            _walkX = Math.Clamp(_walkX, min, Math.Max(min, max));
            _walkDir = -_walkDir;
        }
        else if (_rng.NextDouble() < (mood == Agents.State.Working ? 0.003 : 0.008))
        {
            _pauseTicks = _rng.Next(40, 120);
            _idle = _rng.Next(3);
            if (_rng.NextDouble() < 0.5) _walkDir = -_walkDir;
        }
        bool step = _tick / (mood == Agents.State.Working ? 3 : 5) % 2 == 1;
        _walker.Pose(step: step, look: (int)_walkDir, blink: blink);
        Canvas.SetLeft(_walker, Math.Round(_walkX));
        Canvas.SetTop(_walker, step ? 0 : 1); // a step up, a step down: it walks rather than slides
    }
}
