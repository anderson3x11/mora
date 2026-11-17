using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System.Power;
using Windows.UI;
using static Mora.Native;

namespace Mora;

record Alert(Pop Kind, string Glyph, string Title, string Sub = "", Color? Tint = null, double? Level = null,
             string? ActionLabel = null, Action? Action = null, ImageSource? Art = null,
             string? Action2Label = null, Action? Action2 = null, int Seconds = 3, bool Wide = false, Action? Expired = null,
             bool FillUp = false,  // the level bar fills from empty when shown (charging)
             Agents.State? Agent = null); // a coding agent's alert: the mascot instead of the icon

// Short-lived events: shown one at a time for ~3 s, held longer only while hovered,
// queued while the panel is open, held during fullscreen games.
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
            if (_mode is Mode.Expanded or Mode.Game) return; // already on screen, likely the user's own skip
            var sub = _media.Artist == "" ? "Now playing" : "Now playing · " + _media.Artist;
            Notify(new Alert(Pop.Music, "", _media.Title, sub, Art: _media.Art));
        };

        try
        {
            _audio = new Audio(DispatcherQueue);
            _audio.VolumeChanged += (level, muted) => { if (_mode != Mode.Game) Notify(muted // the music card shows its own level
                ? new Alert(Pop.Volume, "", "Muted", Level: 0)
                : new Alert(Pop.Volume, "", $"Volume {Math.Round(level * 100)}", Level: level)); };
            _audio.OutputChanged += async name => ShowOutput(name, await BluetoothBattery.For(name));
        }
        catch (System.Runtime.InteropServices.COMException) { } // audio service stopped: no audio alerts

        _privacy = new PrivacyWatch(DispatcherQueue);
        _privacy.Started += (device, app) => Notify(device == PrivacyWatch.Device.Microphone
            ? new Alert(Pop.Privacy, "", $"{app} is using your microphone", "An orange dot stays in the panel while it lasts", MicOrange, Wide: true)
            : new Alert(Pop.Privacy, "", $"{app} is using your camera", "A green dot stays in the panel while it lasts", CameraGreen, Wide: true));
        _privacy.Changed += FillPrivacyDots;
        FillPrivacyDots();

        _notifications = new Notifications(DispatcherQueue, id => _settings.NotifyApps.ContainsKey(id));
        _notifications.Arrived += t =>
        {
            if (t.AppId.Contains("Claude", StringComparison.OrdinalIgnoreCase) && !FirstWordOnClaude()) return;
            Notify(new Alert(Pop.Notifications, "", t.Title == "" ? t.App : t.Title,
                t.Title == "" ? t.Body : t.Body == "" ? t.App : $"{t.App} · {t.Body}", Art: t.Logo, Seconds: 4));
        };
        _ = _notifications.StartAsync();

        _downloads = new Downloads(DispatcherQueue);
        _downloads.Finished += path => Notify(new Alert(Pop.Downloads, "", Path.GetFileName(path),
            "Download finished " + Size(path), Green, ActionLabel: "Show",
            Action: () => Process.Start("explorer.exe", $"/select,\"{path}\"")));

        if (PowerManager.BatteryStatus != BatteryStatus.NotPresent)
        {
            _battery = PowerManager.RemainingChargePercent;
            PowerManager.RemainingChargePercentChanged += (_, _) => DispatcherQueue.TryEnqueue(OnBattery);
            PowerManager.PowerSupplyStatusChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                if (PowerManager.PowerSupplyStatus == PowerSupplyStatus.Adequate) ShowCharging(PowerManager.RemainingChargePercent);
            });
        }
    }

    // A new audio output, with its battery when it is a Bluetooth headset that reports one.
    void ShowOutput(string name, int? battery) => Notify(battery is int b
        ? new Alert(Pop.Devices, "", name, $"Connected, battery {b}%", Tint: b <= 20 ? Red : null)
        : new Alert(Pop.Devices, "", name, "Audio output"));

    // Plugged in: the bar fills up to the current charge, like the iPhone's charging moment.
    void ShowCharging(int percent) =>
        Notify(new Alert(Pop.Devices, BluetoothBattery.Glyph(percent, charging: true), $"Charging, {percent}%", Tint: Green, Level: percent / 100.0, FillUp: true));

    static readonly Color MicOrange = Color.FromArgb(255, 0xFF, 0x9F, 0x0A), CameraGreen = Color.FromArgb(255, 0x30, 0xD1, 0x58);
    PrivacyWatch? _privacy;
    Notifications? _notifications;

    // One dot per device in use, with the apps using it on hover.
    void FillPrivacyDots()
    {
        PrivacyDots.Children.Clear();
        foreach (var group in _privacy!.InUse.GroupBy(u => u.Device))
        {
            var dot = new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 7, Height = 7,
                Fill = new SolidColorBrush(group.Key == PrivacyWatch.Device.Microphone ? MicOrange : CameraGreen),
            };
            var what = group.Key == PrivacyWatch.Device.Microphone ? "microphone" : "camera";
            Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(dot, $"Using the {what}: {string.Join(", ", group.Select(u => u.App))}");
            PrivacyDots.Children.Add(dot);
        }
    }

    void OnBattery()
    {
        int prev = _battery, now = _battery = PowerManager.RemainingChargePercent;
        CheckLowBattery(prev, now, PowerManager.BatteryStatus == BatteryStatus.Discharging);
    }

    void CheckLowBattery(int prev, int now, bool discharging)
    {
        if (!discharging || !LowBatteryCrossed(prev, now)) return;
        Notify(new Alert(Pop.Devices, BluetoothBattery.Glyph(now, charging: false), $"Battery at {now}%", "Plug in soon", Red, ActionLabel: "Energy saver",
            Action: () => Process.Start(new ProcessStartInfo("ms-settings:batterysaver") { UseShellExecute = true })));
    }

    // Warn once when crossing 20% and once at 10%, not on every percent below.
    internal static bool LowBatteryCrossed(int prev, int now) => (prev > 20 && now <= 20) || (prev > 10 && now <= 10);

    static string Size(string path)
    {
        double b;
        try { b = new FileInfo(path).Length; }
        catch (IOException) { return ""; } // moved or deleted right after finishing
        string[] units = ["B", "KB", "MB", "GB"];
        int i = 0;
        while (b >= 1024 && i < units.Length - 1) { b /= 1024; i++; }
        return $"· {b:0.#} {units[i]}";
    }

    static readonly Color QuietIndigo = Color.FromArgb(255, 0x5E, 0x5C, 0xE6); // the iPhone's Focus moon

    void Quiet_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        _settings.DoNotDisturb = !_settings.DoNotDisturb;
        _settings.Save();
        FillQuiet();
        UpdateTray();
        if (!_settings.DoNotDisturb && _game is null && _held.Count > 0) ShowHeld("during Do not disturb");
    }

    void FillQuiet() => QuietIcon.Foreground = _settings.DoNotDisturb
        ? new SolidColorBrush(QuietIndigo) : (Brush)((FrameworkElement)Content).Resources["Muted"];

    // force: Mora's own questions and summaries, shown whatever the pop-up settings or a running game.
    void Notify(Alert a, bool force = false)
    {
        if (!force && (!_settings.Allows(a.Kind) || _setup != null)) return;
        if (!force && _settings.DoNotDisturb && a.Kind != Pop.Volume) // the volume keys are the user's own doing
        {
            _held.Add(a);
            return;
        }
        if (!force && _game != null && !Profile!.Shows(a.Kind))
        {
            if (a.Kind != Pop.Volume) _held.Add(a); // shown as one summary after the game; a stale volume level is noise
            return;
        }
        if (_mode == Mode.Alert && _alert?.Kind == a.Kind && a.Kind is Pop.Volume or Pop.Music)
        {
            _alert = a; // a held volume key or a new track updates the alert in place instead of popping again
            FillAlert();
            ArmAlert();
            return;
        }
        if (_settings.PlaySounds && a.Kind is Pop.Timers or Pop.Notifications or Pop.Agents) Chime(a.Kind, _settings.SoundVolume);
        _alerts.Enqueue(a);
        if (_mode is Mode.Hidden or Mode.Compact) NextAlert();
    }

    // Windows' own sounds: a ring when a timer or a focus round ends, a short note for app notifications
    // (their Windows banner, sound included, is off). Nothing for the rest: music, volume, devices are no news.
    // volume: 0 to 100, where 100 is the sound's loudest without clipping. Also played by Settings to try it.
    internal static void Chime(Pop kind, int volume)
    {
        var name = kind == Pop.Timers ? "Alarm02.wav" : "Windows Notify Messaging.wav";
        if (!Sounds.TryGetValue((name, volume), out var sound))
            Sounds[(name, volume)] = sound = Loud(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", name), volume / 100.0);
        if (sound != 0) PlaySound(sound, 0, 0x7); // SND_ASYNC | SND_NODEFAULT | SND_MEMORY
    }

    // ponytail: one copy per volume ever played, never freed (a playing sound reads it); a few KB each.
    static readonly Dictionary<(string, int), nint> Sounds = [];

    // Windows' notification sounds are mixed at different levels (Messaging peaks at a quarter of full scale):
    // brought to the chosen share of full scale, then kept in native memory, which an async PlaySound reads while it plays.
    static nint Loud(string path, double level)
    {
        byte[] b;
        try { b = File.ReadAllBytes(path); }
        catch (IOException) { return 0; }
        int i = 12;
        while (i + 8 <= b.Length && System.Text.Encoding.ASCII.GetString(b, i, 4) != "data") i += 8 + BitConverter.ToInt32(b, i + 4);
        if (i + 8 <= b.Length && BitConverter.ToInt16(b, 20) == 1 && BitConverter.ToInt16(b, 34) == 16) // 16-bit PCM
        {
            var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(b.AsSpan(i + 8, Math.Min(BitConverter.ToInt32(b, i + 4), b.Length - i - 8) & ~1));
            int peak = 1;
            foreach (var s in samples) peak = Math.Max(peak, Math.Abs((int)s));
            var gain = level * short.MaxValue / peak;
            for (int k = 0; k < samples.Length; k++) samples[k] = (short)(samples[k] * gain);
        }
        var native = System.Runtime.InteropServices.Marshal.AllocHGlobal(b.Length);
        System.Runtime.InteropServices.Marshal.Copy(b, 0, native, b.Length);
        return native;
    }

    [System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    static extern bool PlaySound(nint sound, nint module, uint flags);

    void NextAlert()
    {
        _alert = _alerts.Dequeue();
        SetMode(Mode.Alert); // fills the layer when it fades in
        ArmAlert();
    }

    async void ArmAlert()
    {
        var t = ++_alertToken;
        await Task.Delay(TimeSpan.FromSeconds(_alert?.Seconds ?? 3) + TimeSpan.FromMilliseconds(200));
        while (t == _alertToken && _mode == Mode.Alert && GetCursorPos(out var pt) != 0 && OnPill(pt))
            await Task.Delay(700);
        if (t != _alertToken || _mode != Mode.Alert) return;
        if (_alert?.Kind == Pop.Music && _motion && _alerts.Count == 0)
        {
            await MusicOut();
            if (t != _alertToken || _mode != Mode.Alert) return; // hovered, clicked or replaced meanwhile
        }
        var expired = _alert?.Expired;
        _alert = null;
        expired?.Invoke(); // left alone until it went away: the alert's default answer
        Settle();
    }

    void FillAlert()
    {
        if (_alert is not { } a) return;
        AlertLayer.Width = GeoOf(Mode.Alert).W;
        AlertIcon.Glyph = a.Glyph;
        var res = ((FrameworkElement)Content).Resources;
        AlertIcon.Foreground = a.Tint is { } iconTint ? new SolidColorBrush(iconTint) : (Brush)res["Fg"];
        AlertIcon.Visibility = a.Art is null && a.Agent is null ? Visibility.Visible : Visibility.Collapsed;
        AlertMascot.Content = a.Agent is { } agent ? Mascot(agent, 2) : null;
        AlertIconFrame.Background = a.Art is { } art
            ? new ImageBrush { ImageSource = art, Stretch = Stretch.UniformToFill }
            : (Brush)res["Raised"];
        AlertIconFrame.CornerRadius = new CornerRadius(a.Art is null ? 17 : 9); // artwork and pictures keep their corners
        AlertTitle.Text = a.Title;
        AlertSub.Text = a.Sub;
        AlertSub.Visibility = a.Level is null ? Visibility.Visible : Visibility.Collapsed;
        AlertBar.Visibility = a.Level is null ? Visibility.Collapsed : Visibility.Visible;
        var level = Math.Clamp(a.Level ?? 0, 0, 1);
        AlertBarFill.Background = a.FillUp && a.Tint is { } tint ? new SolidColorBrush(tint) : (Brush)res["Ink"];
        if (a.FillUp && _motion)
        {
            AlertBarScale.ScaleX = 0;
            var fill = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            Animate(fill, AlertBarScale, "ScaleX", level, 900);
            fill.BeginTime = TimeSpan.FromMilliseconds(150);
            fill.Begin();
        }
        else AlertBarScale.ScaleX = level;
        AlertAction.Visibility = a.ActionLabel is null ? Visibility.Collapsed : Visibility.Visible;
        AlertActionText.Text = a.ActionLabel ?? "";
        AlertAction2.Visibility = a.Action2Label is null ? Visibility.Collapsed : Visibility.Visible;
        AlertWave.Visibility = a.Kind == Pop.Music && a.ActionLabel is null ? Visibility.Visible : Visibility.Collapsed;
        AlertAction2Text.Text = a.Action2Label ?? "";
        if (a.Kind == Pop.Music && _motion) MusicIn();
        else
        {
            (AlertArtMove.ScaleX, AlertArtMove.ScaleY, AlertArtMove.Rotation, AlertArtMove.TranslateY) = (1, 1, 0, 0);
            (AlertTextMove.X, AlertTextMove.Y, AlertText.Opacity, AlertIconFrame.Opacity) = (0, 0, 1, 1);
        }
    }

    // A new track comes down out of the screen's edge, as the island itself does, and goes back up the same way
    // before the island closes.
    void MusicIn()
    {
        var sb = new Storyboard();
        var glide = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };
        (AlertArtMove.Rotation, AlertArtMove.TranslateY, AlertTextMove.X, AlertTextMove.Y, AlertIconFrame.Opacity) = (0, 0, 0, 0, 1);
        var settle = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 };
        Tween(sb, AlertArtMove, "TranslateY", -26, 0, 520, settle);
        Tween(sb, AlertArtMove, "ScaleX", 0.8, 1, 520, settle);
        Tween(sb, AlertArtMove, "ScaleY", 0.8, 1, 520, settle);
        Tween(sb, AlertIconFrame, "Opacity", 0, 1, 260, glide);
        Tween(sb, AlertTextMove, "Y", -12, 0, 460, glide, 70);
        Tween(sb, AlertText, "Opacity", 0, 1, 300, glide, 90);
        sb.Begin();
    }

    Task MusicOut()
    {
        var sb = new Storyboard();
        var ease = new ExponentialEase { EasingMode = EasingMode.EaseIn, Exponent = 3 };
        Tween(sb, AlertArtMove, "TranslateY", 0, -22, 240, ease);
        Tween(sb, AlertArtMove, "ScaleX", 1, 0.85, 240, ease);
        Tween(sb, AlertArtMove, "ScaleY", 1, 0.85, 240, ease);
        Tween(sb, AlertIconFrame, "Opacity", 1, 0, 220, ease);
        Tween(sb, AlertTextMove, "Y", 0, -10, 220, ease);
        Tween(sb, AlertText, "Opacity", 1, 0, 200, ease);
        sb.Begin();
        return Task.Delay(220);
    }

    static void Tween(Storyboard sb, DependencyObject target, string property, double from, double to, int ms, EasingFunctionBase ease, int delay = 0)
    {
        var a = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(ms), BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = ease };
        Storyboard.SetTarget(a, target);
        Storyboard.SetTargetProperty(a, property);
        sb.Children.Add(a);
    }

    void AlertAction_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => RunAlertAction(e, a => a.Action);
    void AlertAction2_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => RunAlertAction(e, a => a.Action2);

    void RunAlertAction(Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e, Func<Alert, Action?> pick)
    {
        e.Handled = true; // not a click on the alert body, which opens the panel
        var a = _alert;
        _alertToken++;
        _alert = null;
        if (a != null) pick(a)?.Invoke();
        if (_mode == Mode.Alert) Settle();
    }
}
