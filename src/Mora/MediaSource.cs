using System.Runtime.InteropServices;
using System.Text;

namespace Mora;

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

    // Titled visible windows with their process name. Runs on the UI thread on media changes, so it must
    // stay cheap: process names come from a small cache by process id instead of listing every process
    // on the PC each time, as System.Diagnostics.Process does.
    static readonly Dictionary<uint, string> NameByPid = [];

    static List<(string Title, string Process)> Windows()
    {
        var found = new List<(string, string)>();
        var text = new StringBuilder(512);
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || GetWindowText(hwnd, text, text.Capacity) == 0) return true;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (!NameByPid.TryGetValue(pid, out var name)) NameByPid[pid] = name = ProcessName(pid);
            found.Add((text.ToString(), name));
            return true;
        }, 0);
        if (NameByPid.Count > 512) NameByPid.Clear(); // ids get reused; a fresh start now and then is enough
        return found;
    }

    static string ProcessName(uint pid) => Path.GetFileNameWithoutExtension(ProcessPath(pid));

    public static string ProcessPath(uint pid)
    {
        var process = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process == 0) return "";
        try
        {
            var path = new StringBuilder(1024);
            int size = path.Capacity;
            return QueryFullProcessImageName(process, 0, path, ref size) ? path.ToString() : "";
        }
        finally { CloseHandle(process); }
    }

    [DllImport("kernel32.dll")] static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder path, ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(nint handle);

    delegate bool EnumProc(nint hwnd, nint param);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, nint param);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(nint hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
}
