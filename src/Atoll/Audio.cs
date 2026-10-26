using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace Atoll;

// Core Audio: master volume of the default output, and switches of the default output
// (headphones plugged in or connected). Callbacks come from audio threads; events are raised on the UI thread.
sealed class Audio
{
    public event Action<float, bool>? VolumeChanged; // level 0..1, muted
    public event Action<string>? OutputChanged;      // friendly name of the new default output

    readonly DispatcherQueue _ui;
    readonly IMMDeviceEnumerator _devices = (IMMDeviceEnumerator)new MMDeviceEnumerator();
    readonly VolumeCallback _volumeCallback;
    readonly DeviceCallback _deviceCallback;
    IAudioEndpointVolume? _volume;
    IAudioMeterInformation? _meter;

    // Loudness of what the default output plays right now, 0..1. A read of a shared value, no capture.
    public float Peak
    {
        get { float p = 0; _meter?.GetPeakValue(out p); return p; }
    }

    public Audio(DispatcherQueue ui)
    {
        _ui = ui;
        _volumeCallback = new(this);
        _deviceCallback = new(this);
        _devices.RegisterEndpointNotificationCallback(_deviceCallback);
        Attach();
    }

    public float Level
    {
        get { float v = 0; _volume?.GetMasterVolumeLevelScalar(out v); return v; }
        set { var ctx = Guid.Empty; _volume?.SetMasterVolumeLevelScalar(value, ref ctx); }
    }

    void Attach()
    {
        _volume?.UnregisterControlChangeNotify(_volumeCallback);
        _volume = null;
        _meter = null;
        if (_devices.GetDefaultAudioEndpoint(0, 1, out var device) != 0) return; // eRender, eMultimedia; none plugged
        var iid = typeof(IAudioEndpointVolume).GUID;
        device.Activate(ref iid, 1, 0, out var volume); // CLSCTX_INPROC_SERVER
        _volume = (IAudioEndpointVolume)volume;
        _volume.RegisterControlChangeNotify(_volumeCallback);
        var meterId = typeof(IAudioMeterInformation).GUID;
        device.Activate(ref meterId, 1, 0, out var meter);
        _meter = (IAudioMeterInformation)meter;
    }

    static string FriendlyName(IMMDevice device)
    {
        device.OpenPropertyStore(0, out var store); // STGM_READ
        var key = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14); // PKEY_Device_FriendlyName
        store.GetValue(ref key, out var value);
        try { return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) ?? "" : ""; } // VT_LPWSTR
        finally { PropVariantClear(ref value); }
    }

    sealed class VolumeCallback(Audio audio) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(nint data) // AUDIO_VOLUME_NOTIFICATION_DATA: Guid, BOOL muted, float master
        {
            bool muted = Marshal.ReadInt32(data, 16) != 0;
            float level = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, 20));
            audio._ui.TryEnqueue(() => audio.VolumeChanged?.Invoke(level, muted));
            return 0;
        }
    }

    sealed class DeviceCallback(Audio audio) : IMMNotificationClient
    {
        public void OnDefaultDeviceChanged(int flow, int role, string? id)
        {
            if (flow != 0 || role != 1 || id is null) return; // render, multimedia: one event per switch
            audio._ui.TryEnqueue(() =>
            {
                try
                {
                    audio.Attach();
                    if (audio._devices.GetDevice(id, out var device) == 0) audio.OutputChanged?.Invoke(FriendlyName(device));
                }
                catch (COMException) { } // the device went away again while switching
            });
        }

        public void OnDeviceStateChanged(string id, int state) { }
        public void OnDeviceAdded(string id) { }
        public void OnDeviceRemoved(string id) { }
        public void OnPropertyValueChanged(string id, PropertyKey key) { }
    }

    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant value);

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey(Guid format, int id) { public Guid Format = format; public int Id = id; }

    [StructLayout(LayoutKind.Sequential)]
    struct PropVariant { public ushort Type, R1, R2, R3; public nint Pointer, Pointer2; }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int flow, int mask, out nint devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        void RegisterEndpointNotificationCallback(IMMNotificationClient client);
        void UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        void Activate(ref Guid iid, int context, nint parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
        void OpenPropertyStore(int access, out IPropertyStore store);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        void GetCount(out int count);
        void GetAt(int index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
    }

    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMNotificationClient
    {
        void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, int state);
        void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        void OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? id);
        void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PropertyKey key);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
        void UnregisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
        void GetChannelCount(out uint count);
        void SetMasterVolumeLevel(float db, ref Guid context);
        void SetMasterVolumeLevelScalar(float level, ref Guid context);
        void GetMasterVolumeLevel(out float db);
        void GetMasterVolumeLevelScalar(out float level);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        void GetPeakValue(out float peak);
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(nint data);
    }
}
