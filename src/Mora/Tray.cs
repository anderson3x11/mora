using System.Runtime.InteropServices;

namespace Mora;

// Notification area icon: left click toggles the island, right click opens a small menu.
// Its messages arrive in the island's window procedure as Tray.Message.
// The menu needs a foreground owner to close properly, so it gets a hidden window of its own:
// the island itself is never activated.
sealed class Tray
{
    public const uint Message = 0x8001; // WM_APP + 1
    public static readonly uint TaskbarCreated = RegisterWindowMessage("TaskbarCreated");
    const uint WM_LBUTTONUP = 0x202, WM_RBUTTONUP = 0x205;

    readonly nint _hwnd, _menuOwner;
    nint _icon;
    string _tip;

    public Tray(nint hwnd, string tip)
    {
        (_hwnd, _tip) = (hwnd, tip);
        _icon = LoadImage(0, Path.Combine(AppContext.BaseDirectory, "Mora.ico"), 1, 0, 0, 0x10 | 0x40); // IMAGE_ICON, LR_LOADFROMFILE | LR_DEFAULTSIZE
        _menuOwner = CreateWindowEx(0, "STATIC", "Mora menu", 0x80000000, 0, 0, 0, 0, 0, 0, 0, 0); // WS_POPUP, never shown
        Add();
    }

    // Also called when Explorer restarts and the taskbar forgets every icon.
    public void Add()
    {
        var data = Data();
        Shell_NotifyIcon(0, ref data); // NIM_ADD
    }

    // Hover text, which names the shortcut.
    public void SetTip(string tip)
    {
        _tip = tip;
        var data = Data();
        Shell_NotifyIcon(1, ref data); // NIM_MODIFY
    }

    // The crab says how things are: as usual; claws up in orange while an agent waits for the user; asleep, gray and
    // eyes shut, during Do not disturb or a game. Drawn at the size Windows wants, whole pixels, no tile behind.
    public enum Mood { Usual, Waiting, Asleep }

    static readonly string[] Usual = ["C......C", "CC....CC", ".BBBBBB.", "BBEBBEBB", ".BBBBBB.", "B.B..B.B"];
    static readonly string[] ClawsUp = ["C.C..C.C", ".C....C.", ".BBBBBB.", "BBEBBEBB", ".BBBBBB.", "B.B..B.B"];
    static readonly Dictionary<Mood, nint> Icons = [];
    Mood _mood;

    public void Show(Mood mood)
    {
        if (mood == _mood && _icon != 0) return;
        _mood = mood;
        if (!Icons.TryGetValue(mood, out var icon))
            Icons[mood] = icon = mood switch
            {
                Mood.Waiting => Draw(ClawsUp, 0xFFFF9F0A, 0xFF1A100E),
                Mood.Asleep => Draw(Usual, 0xFF8E8E93, 0xFF48484A),
                _ => Draw(Usual, 0xFFFF7A59, 0xFF1A100E),
            };
        _icon = icon;
        var data = Data();
        Shell_NotifyIcon(1, ref data); // NIM_MODIFY
    }

    static nint Draw(string[] map, uint body, uint eye)
    {
        int size = GetSystemMetrics(49); // SM_CXSMICON: the tray's size at this scale
        int px = Math.Max(1, size / map[0].Length), x0 = (size - map[0].Length * px) / 2, y0 = (size - map.Length * px) / 2;
        var pixels = new uint[size * size];
        for (int y = 0; y < map.Length; y++)
            for (int x = 0; x < map[y].Length; x++)
            {
                if (map[y][x] == '.') continue;
                for (int dy = 0; dy < px; dy++)
                    for (int dx = 0; dx < px; dx++)
                        pixels[(y0 + y * px + dy) * size + x0 + x * px + dx] = map[y][x] == 'E' ? eye : body;
            }
        var header = new BitmapInfoHeader { Size = 40, Width = size, Height = -size, Planes = 1, BitCount = 32 };
        var color = CreateDIBSection(0, ref header, 0, out var bits, 0, 0);
        Marshal.Copy((int[])(object)pixels.Select(p => (int)p).ToArray(), 0, bits, pixels.Length);
        var mask = CreateBitmap(size, size, 1, 1, 0);
        var info = new IconInfo { Icon = true, Mask = mask, Color = color };
        var icon = CreateIconIndirect(ref info);
        DeleteObject(color);
        DeleteObject(mask);
        return icon;
    }

    public void Remove()
    {
        var data = Data();
        Shell_NotifyIcon(2, ref data); // NIM_DELETE
    }

    public void OnMessage(nint lParam, Action toggle, IReadOnlyList<(string Label, Action Run)> menu)
    {
        var mouse = (uint)(lParam & 0xFFFF);
        if (mouse == WM_LBUTTONUP) toggle();
        if (mouse != WM_RBUTTONUP) return;

        var m = CreatePopupMenu();
        for (int i = 0; i < menu.Count; i++) AppendMenu(m, 0, (nuint)(i + 1), menu[i].Label);
        GetCursorPos(out var pt);
        SetForegroundWindow(_menuOwner);
        int picked = TrackPopupMenuEx(m, 0x100 | 0x80 | 0x2, pt.X, pt.Y, _menuOwner, 0); // RETURNCMD | NONOTIFY | RIGHTBUTTON
        PostMessage(_menuOwner, 0, 0, 0); // WM_NULL, so the menu closes on the next click elsewhere
        DestroyMenu(m);
        if (picked > 0) menu[picked - 1].Run();
    }

    NotifyIconData Data() => new()
    {
        Size = Marshal.SizeOf<NotifyIconData>(),
        Hwnd = _hwnd,
        Id = 1,
        Flags = 0x1 | 0x2 | 0x4, // NIF_MESSAGE | NIF_ICON | NIF_TIP
        CallbackMessage = Message,
        Icon = _icon,
        Tip = _tip,
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotifyIconData
    {
        public int Size;
        public nint Hwnd;
        public uint Id, Flags, CallbackMessage;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Item;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct BitmapInfoHeader { public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPels, YPels, Used, Important; }
    [StructLayout(LayoutKind.Sequential)] struct IconInfo { public bool Icon; public int X, Y; public nint Mask, Color; }

    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("gdi32.dll")] static extern nint CreateDIBSection(nint dc, ref BitmapInfoHeader header, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] static extern nint CreateBitmap(int w, int h, uint planes, uint bits, nint data);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint obj);
    [DllImport("user32.dll")] static extern nint CreateIconIndirect(ref IconInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint LoadImage(nint instance, string name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenu(nint menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")] static extern int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint owner, nint parameters);
    [DllImport("user32.dll")] static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] static extern bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point pt);
}
