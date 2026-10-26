using System.Text.RegularExpressions;

namespace Mora;

// Test hook for events a machine cannot produce (no Bluetooth headset, no battery): run
// `Mora.exe --simulate <event> ...` while Mora runs, and the running instance handles the event
// through the same code as the real one.
//   headphones "<audio output name>" [percent]    charging <percent>    battery <before> <after>
//   focus <focus seconds> <break seconds>: a focus session with short rounds
//   track "<title>" "<artist>": a track change, with the current artwork
public sealed partial class IslandWindow
{
    internal const uint WM_COPYDATA = 0x4A;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal struct CopyData { public nint Id; public int Bytes; public nint Data; }

    void Simulate(string command)
    {
        var a = Regex.Matches(command, "\"([^\"]*)\"|(\\S+)").Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Value).ToArray();
        if (a.Length == 0) return;
        switch (a[0])
        {
            case "headphones" when a.Length >= 2:
                ShowOutput(a[1], a.Length > 2 && int.TryParse(a[2], out var b) ? b : null);
                break;
            case "charging" when a.Length >= 2 && int.TryParse(a[1], out var level):
                ShowCharging(level);
                break;
            case "battery" when a.Length >= 3 && int.TryParse(a[1], out var before) && int.TryParse(a[2], out var after):
                CheckLowBattery(before, after, discharging: true);
                break;
            case "track" when a.Length >= 3:
                Notify(new Alert(Pop.Music, "", a[1], "Now playing · " + a[2], Art: _media.Art));
                break;
            case "focus" when a.Length >= 3 && int.TryParse(a[1], out var work) && int.TryParse(a[2], out var rest):
                (_focusWork, _focusBreak) = (work, rest);
                StartFocus(1, rest: false);
                break;
        }
    }
}
