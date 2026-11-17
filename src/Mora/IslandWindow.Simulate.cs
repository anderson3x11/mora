using System.Text.RegularExpressions;

namespace Mora;

// Test hook for events a machine cannot produce (no Bluetooth headset, no battery): run
// `Mora.exe --simulate <event> ...` while Mora runs, and the running instance handles the event
// through the same code as the real one.
//   headphones "<audio output name>" [percent]    charging <percent>    battery <before> <after>
//   focus <focus seconds> <break seconds>: a focus session with short rounds
//   track "<title>" "<artist>": a track change, with the current artwork
//   open <tab>, close: the panel on a tab (Home, Media, Agents...), for screenshots
//   agent "<title>" "<text>": a coding agent asking for the user, with the mascot
//   settings: the settings window
//   settings-chaos <n>: n random changes of the settings, applied like the settings window does
public sealed partial class IslandWindow
{
    // Random settings, one change every 50 to 300 ms, through the same path as the settings window.
    async void SettingsChaos(int times)
    {
        var rng = new Random(times);
        var flags = typeof(Settings).GetProperties().Where(p => p.PropertyType == typeof(bool) && p.CanWrite && p.Name is not ("StartWithWindows" or "AskNewGames")).ToArray();
        for (int i = 0; i < times; i++)
        {
            var next = _settings.Clone();
            var flag = flags[rng.Next(flags.Length)];
            flag.SetValue(next, !(bool)flag.GetValue(next)!);
            if (rng.Next(5) == 0) next.ClockFormat = (ClockStyle)rng.Next(4);
            ApplySettings(next, firstRun: false);
            await Task.Delay(rng.Next(50, 300));
        }
    }

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
            case "open" when a.Length >= 2 && Enum.TryParse<Tab>(a[1], true, out var tab):
                _tab = tab;
                SetMode(Mode.Expanded, Via.Click);
                ShowTab();
                break;
            case "settings-chaos" when a.Length >= 2 && int.TryParse(a[1], out var times):
                SettingsChaos(times);
                break;
            case "settings":
                OpenSetup(firstRun: false);
                break;
            case "close":
                Settle();
                break;
            case "agent" when a.Length >= 3:
                Notify(new Alert(Pop.Agents, "", a[1], a[2], MicOrange, Seconds: 6, Wide: true, Agent: Agents.State.NeedsYou), force: true);
                break;
            case "focus" when a.Length >= 3 && int.TryParse(a[1], out var work) && int.TryParse(a[2], out var rest):
                (_focusWork, _focusBreak) = (work, rest);
                StartFocus(1, rest: false);
                break;
        }
    }
}
