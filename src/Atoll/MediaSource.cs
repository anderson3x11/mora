using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Atoll;

// Windows only says which app is playing, and browsers hide behind an opaque id. The site is read
// from the browser window whose title contains what is playing: "<video> - YouTube — Mozilla Firefox".
// ponytail: only works when the playing tab is the visible tab of its window; otherwise no site.
static class MediaSource
{
    static readonly string[] Sites = ["YouTube Music", "YouTube", "Twitch", "SoundCloud", "Deezer", "Spotify", "Netflix", "Bandcamp", "Kick"];

    static readonly Dictionary<string, string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["firefox"] = "Firefox", ["chrome"] = "Chrome", ["msedge"] = "Edge", ["brave"] = "Brave", ["opera"] = "Opera", ["vivaldi"] = "Vivaldi",
    };

    // Site when known (YouTube, Twitch...), and a short label for the source badge.
    public static (string? Site, string Label) Identify(string appId, string title, string artist)
    {
        var app = Path.GetFileNameWithoutExtension(appId);
        if (appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !Browsers.ContainsKey(app))
            return (null, app); // a desktop player: Spotify.exe

        var windows = Windows();
        foreach (var (window, process) in windows)
        {
            if (!Browsers.TryGetValue(process, out var browser)) continue;
            if (!(title.Length > 3 && window.Contains(title)) && !(artist.Length > 2 && window.Contains(artist))) continue;
            var site = Sites.FirstOrDefault(s => window.Contains(" - " + s) || window.Contains(" | " + s));
            return (site, site ?? browser);
        }
        if (appId == "Chrome") return (null, "Chrome");
        if (appId == "MSEdge") return (null, "Edge");
        // Playing in a background tab, from a browser with an opaque id (Firefox): name it if it is the only one open.
        var open = windows.Select(w => Browsers.GetValueOrDefault(w.Process)).OfType<string>().Distinct().ToList();
        return (null, open.Count == 1 ? open[0] : "");
    }

    static List<(string Title, string Process)> Windows()
    {
        var found = new List<(string, string)>();
        var text = new StringBuilder(512);
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || GetWindowText(hwnd, text, text.Capacity) == 0) return true;
            GetWindowThreadProcessId(hwnd, out var pid);
            try { found.Add((text.ToString(), Process.GetProcessById((int)pid).ProcessName)); }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException) { } // exited meanwhile
            return true;
        }, 0);
        return found;
    }

    delegate bool EnumProc(nint hwnd, nint param);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, nint param);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(nint hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
}
