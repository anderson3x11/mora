using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;

namespace Atoll;

// Which apps use the microphone or the camera right now, as Windows records it for its own privacy
// icon: an app is in use while its LastUsedTimeStop is 0. A thread sleeps on registry change
// notifications, so this costs nothing until something starts or stops.
sealed class PrivacyWatch
{
    public enum Device { Microphone, Camera }
    const string Store = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

    public event Action<Device, string>? Started; // raised on the UI thread
    public event Action? Changed;
    public List<(Device Device, string App)> InUse { get; private set; } = [];

    readonly DispatcherQueue _ui;

    public PrivacyWatch(DispatcherQueue ui)
    {
        _ui = ui;
        InUse = Scan();
        new Thread(Watch) { IsBackground = true, Name = "Atoll privacy watch" }.Start();
    }

    void Watch()
    {
        using var mic = Registry.CurrentUser.OpenSubKey(Store + "microphone");
        using var cam = Registry.CurrentUser.OpenSubKey(Store + "webcam");
        if (mic is null && cam is null) return;
        using var changed = new AutoResetEvent(false);
        var handle = changed.SafeWaitHandle.DangerousGetHandle();
        var inUse = Scan();
        while (true)
        {
            // Re-armed every round: a notification fires once. LAST_SET covers the timestamps, NAME new apps.
            // Windows reliably signals an app starting, not always one stopping, so while something is in
            // use the keys are also read every 2 s. Nothing in use: the thread just sleeps.
            foreach (var key in new[] { mic, cam })
                if (key != null) RegNotifyChangeKeyValue(key.Handle.DangerousGetHandle(), true, 0x1 | 0x4, handle, true);
            if (changed.WaitOne(inUse.Count > 0 ? 2000 : Timeout.Infinite))
                Thread.Sleep(150); // an app writes Start and Stop separately: read once it is done
            var now = Scan();
            if (now.SequenceEqual(inUse)) continue;
            inUse = now;
            _ui.TryEnqueue(() => Update(now));
        }
    }

    void Update(List<(Device, string)> now)
    {
        var before = InUse;
        InUse = now;
        foreach (var (device, app) in now.Except(before)) Started?.Invoke(device, app);
        if (!now.SequenceEqual(before)) Changed?.Invoke();
    }

    static List<(Device, string)> Scan()
    {
        var found = new List<(Device, string)>();
        foreach (var (device, name) in new[] { (Device.Microphone, "microphone"), (Device.Camera, "webcam") })
        {
            using var key = Registry.CurrentUser.OpenSubKey(Store + name);
            if (key is null) continue;
            foreach (var sub in key.GetSubKeyNames())
            {
                using var app = key.OpenSubKey(sub);
                if (app is null) continue;
                if (sub == "NonPackaged")
                {
                    foreach (var desktop in app.GetSubKeyNames())
                    {
                        using var entry = app.OpenSubKey(desktop);
                        if (entry != null && InUseNow(entry)) found.Add((device, DesktopName(desktop)));
                    }
                }
                else if (InUseNow(app)) found.Add((device, PackagedName(sub)));
            }
        }
        return found.Distinct().ToList();
    }

    static bool InUseNow(RegistryKey entry) =>
        entry.GetValue("LastUsedTimeStart") is long start && start != 0 && entry.GetValue("LastUsedTimeStop") is long stop && stop == 0;

    // "C:#Users#me#AppData#Local#Discord#app-1.0#Discord.exe" -> "Discord"
    static string DesktopName(string key) => Path.GetFileNameWithoutExtension(key.Replace('#', '\\'));

    // "Microsoft.WindowsCamera_8wekyb3d8bbwe" -> "WindowsCamera"
    static string PackagedName(string key)
    {
        var name = key.Split('_')[0];
        return name[(name.LastIndexOf('.') + 1)..];
    }

    [DllImport("advapi32.dll")] static extern int RegNotifyChangeKeyValue(nint key, bool subtree, uint filter, nint evt, bool async);
}
