using Windows.Devices.Enumeration;

namespace Mora;

// Battery level that Windows reads from Bluetooth headsets (the one shown in Settings > Bluetooth),
// found by matching the device name inside the new audio output's name ("Headphones (WH-1000XM4)").
// Only asked when the default output changes, so a full device listing is fine.
static class BluetoothBattery
{
    const string LevelKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2"; // DEVPKEY_Bluetooth_Battery

    public static async Task<int?> For(string outputName)
    {
        try
        {
            var devices = await DeviceInformation.FindAllAsync("", [LevelKey], DeviceInformationKind.Device);
            foreach (var d in devices)
                if (d.Properties.TryGetValue(LevelKey, out var level) && level is byte percent
                    && !string.IsNullOrEmpty(d.Name) && outputName.Contains(d.Name, StringComparison.OrdinalIgnoreCase))
                    return percent;
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { }
        return null; // not Bluetooth, or a headset that does not report its battery
    }

    // Segoe battery glyphs: ten steps, plain or charging.
    public static string Glyph(int percent, bool charging) =>
        ((char)((charging ? 0xEBAB : 0xEBA0) + Math.Clamp((int)Math.Round(percent / 10.0), 0, 10))).ToString();
}
