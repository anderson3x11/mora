using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace Atoll;

// Which apps use the microphone or the camera right now, from the sources behind Windows' own privacy
// icons: active capture sessions of the audio engine (microphone) and the camera sensor activity
// monitor (camera). The usage keys in the registry are not reliable: Windows 11 26H2 stopped writing them.
sealed class PrivacyWatch
{
    public enum Device { Microphone, Camera }

    public event Action<Device, string>? Started; // raised on the UI thread
    public event Action? Changed;
    public List<(Device Device, string App)> InUse { get; private set; } = [];

    readonly DispatcherQueue _ui;
    readonly CameraCallback _callback;
    readonly IMFSensorActivityMonitor? _monitor; // kept alive: it reports from its own thread
    volatile uint[] _cameraPids = [];

    public PrivacyWatch(DispatcherQueue ui)
    {
        _ui = ui;
        _callback = new(this);
        if (MFStartup(0x20070, 0) == 0 && MFCreateSensorActivityMonitor(_callback, out var monitor) == 0) // MF_VERSION
        {
            _monitor = monitor;
            _monitor.Start();
        }
        new Thread(Watch) { IsBackground = true, Name = "Atoll privacy watch" }.Start();
    }

    // ponytail: reads the capture sessions every second (a few COM calls, well under a millisecond);
    // per-endpoint session notifications if that ever shows up in a profile.
    void Watch()
    {
        // By CLSID: a second [ComImport] class for MMDeviceEnumerator (Audio has one) fails to cast.
        var devices = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
        List<(Device, string)>? inUse = null;
        while (true)
        {
            List<(Device, string)> now;
            try { now = Scan(devices); }
            catch (COMException) { now = inUse ?? []; } // a microphone went away mid-scan
            if (inUse is null) _ui.TryEnqueue(() => { InUse = now; Changed?.Invoke(); }); // already in use at launch: no pop-up
            else if (!now.SequenceEqual(inUse)) _ui.TryEnqueue(() => Update(now));
            inUse = now;
            Thread.Sleep(1000);
        }
    }

    void Update(List<(Device, string)> now)
    {
        var before = InUse;
        InUse = now;
        foreach (var (device, app) in now.Except(before)) Started?.Invoke(device, app);
        if (!now.SequenceEqual(before)) Changed?.Invoke();
    }

    List<(Device, string)> Scan(IMMDeviceEnumerator devices)
    {
        var users = new List<(Device, uint)>();
        devices.EnumAudioEndpoints(1, 1, out var inputs); // eCapture, DEVICE_STATE_ACTIVE
        inputs.GetCount(out var count);
        var iid = typeof(IAudioSessionManager2).GUID;
        for (uint i = 0; i < count; i++)
        {
            inputs.Item(i, out var input);
            input.Activate(ref iid, 1, 0, out var manager); // CLSCTX_INPROC_SERVER
            ((IAudioSessionManager2)manager).GetSessionEnumerator(out var sessions);
            sessions.GetCount(out var n);
            for (int k = 0; k < n; k++)
            {
                sessions.GetSession(k, out var session);
                session.GetState(out var state);
                session.GetProcessId(out var pid);
                if (state == 1) users.Add((Device.Microphone, pid)); // AudioSessionStateActive
            }
        }
        foreach (var pid in _cameraPids) users.Add((Device.Camera, pid));

        // Windows' own services (the camera frame server holding a capture card, system sounds) are not apps.
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return users
            .Select(u => (u.Item1, Path: MediaSource.ProcessPath(u.Item2)))
            .Where(u => u.Path != "" && !u.Path.StartsWith(windows, StringComparison.OrdinalIgnoreCase))
            .Select(u => (u.Item1, Path.GetFileNameWithoutExtension(u.Path)))
            .Distinct().ToList();
    }

    // Each report lists every camera with the processes that opened it.
    sealed class CameraCallback(PrivacyWatch watch) : IMFSensorActivitiesReportCallback
    {
        public int OnActivitiesReport(IMFSensorActivitiesReport report)
        {
            var pids = new List<uint>();
            report.GetCount(out var cameras);
            for (uint i = 0; i < cameras; i++)
            {
                report.GetActivityReport(i, out var camera);
                camera.GetProcessCount(out var processes);
                for (uint p = 0; p < processes; p++)
                {
                    camera.GetProcessActivity(p, out var activity);
                    activity.GetProcessId(out var pid);
                    activity.GetStreamingState(out var streaming);
                    if (streaming) pids.Add(pid);
                }
            }
            watch._cameraPids = [.. pids];
            return 0;
        }
    }

    [DllImport("mfplat.dll")] static extern int MFStartup(int version, int flags);
    [DllImport("mfsensorgroup.dll")] static extern int MFCreateSensorActivityMonitor(IMFSensorActivitiesReportCallback callback, out IMFSensorActivityMonitor monitor);

    // Only the methods used are named; the placeholders keep the vtable order.
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator { void EnumAudioEndpoints(int flow, int mask, out IMMDeviceCollection devices); }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection { void GetCount(out uint count); void Item(uint index, out IMMDevice device); }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice { void Activate(ref Guid iid, int context, nint parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result); }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2 { void _1(); void _2(); void GetSessionEnumerator(out IAudioSessionEnumerator sessions); }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator { void GetCount(out int count); void GetSession(int index, out IAudioSessionControl2 session); }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        void GetState(out int state);
        void _2(); void _3(); void _4(); void _5(); void _6(); void _7(); void _8(); void _9(); void _10(); void _11();
        void GetProcessId(out uint pid);
    }

    [ComImport, Guid("D0CEF145-B3F4-4340-A2E5-7A5080CA05CB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSensorActivityMonitor { void Start(); void Stop(); }

    [ComImport, Guid("DE5072EE-DBE3-46DC-8A87-B6F631194751"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSensorActivitiesReportCallback { [PreserveSig] int OnActivitiesReport(IMFSensorActivitiesReport report); }

    [ComImport, Guid("683F7A5E-4A19-43CD-B1A9-DBF4AB3F7777"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSensorActivitiesReport { void GetCount(out uint count); void GetActivityReport(uint index, out IMFSensorActivityReport report); }

    [ComImport, Guid("3E8C4BE1-A8C2-4528-90DE-2851BDE5FEAD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSensorActivityReport
    {
        void _1(); void _2();
        void GetProcessCount(out uint count);
        void GetProcessActivity(uint index, out IMFSensorProcessActivity activity);
    }

    [ComImport, Guid("39DC7F4A-B141-4719-813C-A7F46162A2B8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSensorProcessActivity { void GetProcessId(out uint pid); void GetStreamingState(out bool streaming); }
}
