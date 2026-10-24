using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.System.Power;
using Windows.UI;
using static Atoll.Native;

namespace Atoll;

record Alert(Pop Kind, string Glyph, string Title, string Sub = "", Color? Tint = null, double? Level = null,
             string? ActionLabel = null, Action? Action = null, ImageSource? Art = null);

// Short-lived events: shown one at a time for ~3 s, held longer only while hovered,
// queued while the panel is open.
public sealed partial class IslandWindow
{
    static readonly Color Green = Color.FromArgb(255, 0x5B, 0xD6, 0x8A), Red = Color.FromArgb(255, 0xFF, 0x6B, 0x5B);

    readonly Queue<Alert> _alerts = new();
    Alert? _alert;
    int _alertToken, _battery;
    Audio? _audio;
    Downloads? _downloads;

    void WireAlerts()
    {
        _media.TrackChanged += () =>
        {
            if (_mode == Mode.Expanded) return; // already on screen, likely the user's own skip
            var sub = _media.Artist == "" ? "Now playing" : "Now playing · " + _media.Artist;
            Notify(new Alert(Pop.Music, "", _media.Title, sub, Art: _media.Art));
        };

        try
        {
            _audio = new Audio(DispatcherQueue);
            _audio.VolumeChanged += (level, muted) => Notify(muted
                ? new Alert(Pop.Volume, "", "Muted", Level: 0)
                : new Alert(Pop.Volume, "", $"Volume {Math.Round(level * 100)}", Level: level));
            _audio.OutputChanged += name => Notify(new Alert(Pop.Devices, "", name, "Audio output"));
        }
        catch (System.Runtime.InteropServices.COMException) { } // audio service stopped: no audio alerts

        _downloads = new Downloads(DispatcherQueue);
        _downloads.Finished += path => Notify(new Alert(Pop.Downloads, "", Path.GetFileName(path),
            "Download finished · " + Size(path), Green, ActionLabel: "Show",
            Action: () => Process.Start("explorer.exe", $"/select,\"{path}\"")));

        if (PowerManager.BatteryStatus != BatteryStatus.NotPresent)
        {
            _battery = PowerManager.RemainingChargePercent;
            PowerManager.RemainingChargePercentChanged += (_, _) => DispatcherQueue.TryEnqueue(OnBattery);
            PowerManager.PowerSupplyStatusChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                if (PowerManager.PowerSupplyStatus == PowerSupplyStatus.Adequate)
                    Notify(new Alert(Pop.Devices, "", "Charging", $"{PowerManager.RemainingChargePercent}%", Green));
            });
        }
    }

    void OnBattery()
    {
        int prev = _battery, now = _battery = PowerManager.RemainingChargePercent;
        if (PowerManager.BatteryStatus != BatteryStatus.Discharging || !LowBatteryCrossed(prev, now)) return;
        Notify(new Alert(Pop.Devices, "", $"Battery at {now}%", "Plug in soon", Red, ActionLabel: "Energy saver",
            Action: () => Process.Start(new ProcessStartInfo("ms-settings:batterysaver") { UseShellExecute = true })));
    }

    // Warn once when crossing 20% and once at 10%, not on every percent below.
    internal static bool LowBatteryCrossed(int prev, int now) => (prev > 20 && now <= 20) || (prev > 10 && now <= 10);

    static string Size(string path)
    {
        double b = new FileInfo(path).Exists ? new FileInfo(path).Length : 0;
        string[] units = ["B", "KB", "MB", "GB"];
        int i = 0;
        while (b >= 1024 && i < units.Length - 1) { b /= 1024; i++; }
        return $"{b:0.#} {units[i]}";
    }

    void Notify(Alert a)
    {
        if (!_settings.Allows(a.Kind)) return;
        if (_mode == Mode.Alert && _alert?.Kind == Pop.Volume && a.Kind == Pop.Volume)
        {
            _alert = a; // a held volume key updates the HUD in place instead of queueing 30 alerts
            FillAlert();
            ArmAlert();
            return;
        }
        _alerts.Enqueue(a);
        if (_mode is Mode.Hidden or Mode.Compact) NextAlert();
    }

    void NextAlert()
    {
        _alert = _alerts.Dequeue();
        SetMode(Mode.Alert); // fills the layer when it fades in
        ArmAlert();
    }

    async void ArmAlert()
    {
        var t = ++_alertToken;
        await Task.Delay(3200);
        while (t == _alertToken && _mode == Mode.Alert && GetCursorPos(out var pt) != 0 && OnPill(pt))
            await Task.Delay(700);
        if (t != _alertToken || _mode != Mode.Alert) return;
        _alert = null;
        Settle();
    }

    void FillAlert()
    {
        if (_alert is not { } a) return;
        AlertIcon.Glyph = a.Glyph;
        AlertIcon.Foreground = new SolidColorBrush(a.Tint ?? Color.FromArgb(255, 0xF4, 0xF4, 0xF6));
        AlertIcon.Visibility = a.Art is null ? Visibility.Visible : Visibility.Collapsed;
        AlertIconFrame.Background = a.Art is { } art
            ? new ImageBrush { ImageSource = art, Stretch = Stretch.UniformToFill }
            : new SolidColorBrush(Color.FromArgb(255, 0x1C, 0x1C, 0x21));
        AlertTitle.Text = a.Title;
        AlertSub.Text = a.Sub;
        AlertSub.Visibility = a.Level is null ? Visibility.Visible : Visibility.Collapsed;
        AlertBar.Visibility = a.Level is null ? Visibility.Collapsed : Visibility.Visible;
        var level = Math.Clamp(a.Level ?? 0, 0, 1);
        AlertBarDone.Width = new GridLength(level, GridUnitType.Star);
        AlertBarLeft.Width = new GridLength(1 - level, GridUnitType.Star);
        AlertAction.Visibility = a.ActionLabel is null ? Visibility.Collapsed : Visibility.Visible;
        AlertActionText.Text = a.ActionLabel ?? "";
    }

    void AlertAction_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        e.Handled = true; // not a click on the alert body, which opens the panel
        var a = _alert;
        _alertToken++;
        _alert = null;
        a?.Action?.Invoke();
        if (_mode == Mode.Alert) Settle();
    }
}
