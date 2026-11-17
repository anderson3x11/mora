using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Mora;

// An agent's logo: a PNG the user put in Mora's "logos" folder (claude.png, codex.png), shown as it is; else the icon
// of its app as installed (the Claude app, Codex), drawn by Windows from the Start menu's apps folder, the same as its
// shortcut. Without either, null: callers show a colored initial.
static class AgentLogo
{
    // Apps by agent: their Store package family and app id, the same on every PC.
    static readonly Dictionary<string, string> Apps = new()
    {
        ["Claude Code"] = "Claude_pzs8sxrjxfjjc!Claude",
        ["Codex"] = "OpenAI.Codex_2p2nqsd0c76g0!App",
    };

    static readonly Dictionary<string, ImageSource?> Cache = [];
    static readonly HashSet<string> FromFile = [];

    public static readonly string Folder = Path.Combine(Path.GetDirectoryName(Settings.FilePath)!, "logos");

    // The user's own PNG: shown whole, transparency included, rather than clipped into an app tile.
    public static bool IsFile(string agent) => FromFile.Contains(agent);

    // UI thread only (it builds a bitmap).
    public static ImageSource? For(string agent)
    {
        if (Cache.TryGetValue(agent, out var cached)) return cached;
        ImageSource? logo = null;
        var png = Path.Combine(Folder, agent.Split(' ')[0].ToLowerInvariant() + ".png"); // "Claude Code" → claude.png
        if (File.Exists(png))
        {
            FromFile.Add(agent);
            return Cache[agent] = new BitmapImage(new Uri(png)) { DecodePixelHeight = 96 };
        }
        if (Apps.TryGetValue(agent, out var app))
            try { logo = Load("shell:AppsFolder\\" + app, 64); }
            catch (Exception e) when (e is COMException or ArgumentException or FileNotFoundException) { } // not installed
        return Cache[agent] = logo;
    }

    static WriteableBitmap? Load(string path, int size)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(path, 0, ref iid, out var factory) != 0) return null;
        try
        {
            if (factory.GetImage(new SIZE { cx = size, cy = size }, 0x1, out var hbitmap) != 0) return null; // SIIGBF_BIGGERSIZEOK
            try
            {
                var info = new BITMAP();
                GetObject(hbitmap, Marshal.SizeOf<BITMAP>(), ref info);
                int w = info.bmWidth, h = info.bmHeight;
                var header = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                var pixels = new byte[w * h * 4];
                var dc = GetDC(0);
                GetDIBits(dc, hbitmap, 0, (uint)h, pixels, ref header, 0);
                ReleaseDC(0, dc);
                var bitmap = new WriteableBitmap(w, h);
                using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(pixels); // premultiplied BGRA, as the shell gives it
                bitmap.Invalidate();
                return bitmap;
            }
            finally { DeleteObject(hbitmap); }
        }
        finally { Marshal.ReleaseComObject(factory); }
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out nint hbitmap); }

    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public short bmPlanes, bmBitsPixel; public nint bmBits; }
    [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    static extern int SHCreateItemFromParsingName(string path, nint bindCtx, ref Guid iid, out IShellItemImageFactory item);
    [DllImport("gdi32.dll")] static extern int GetObject(nint obj, int size, ref BITMAP bitmap);
    [DllImport("gdi32.dll")] static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint obj);
    [DllImport("user32.dll")] static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(nint hwnd, nint dc);
}
