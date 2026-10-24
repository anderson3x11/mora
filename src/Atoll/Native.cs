using System.Runtime.InteropServices;

namespace Atoll;

static partial class Native
{
    public const int GWL_EXSTYLE = -20;
    public const nint WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x08000000;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20;
    public const int SW_SHOWNOACTIVATE = 4;
    public const uint WM_DPICHANGED = 0x2E0, WM_NCCALCSIZE = 0x83, WM_MOUSEACTIVATE = 0x21, WM_HOTKEY = 0x312, MA_NOACTIVATE = 3;
    public const uint MOD_ALT = 0x1, MOD_NOREPEAT = 0x4000;
    public const int WH_MOUSE_LL = 14;
    public const nint WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204, WM_MBUTTONDOWN = 0x207;
    public const nint WM_LBUTTONUP = 0x202, WM_RBUTTONUP = 0x205, WM_MBUTTONUP = 0x208;
    public static readonly nint HWND_TOPMOST = -1;

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static partial nint GetWindowLongPtr(nint hwnd, int index);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [LibraryImport("user32.dll")] public static partial int SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [LibraryImport("user32.dll")] public static partial int ShowWindow(nint hwnd, int cmd);
    [LibraryImport("user32.dll")] public static partial int SetWindowRgn(nint hwnd, nint rgn, int redraw);
    [LibraryImport("user32.dll")] public static partial int RegisterHotKey(nint hwnd, int id, uint mods, uint vk);
    [LibraryImport("shcore.dll")] public static partial int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);
    [LibraryImport("user32.dll")] public static partial nint MonitorFromWindow(nint hwnd, uint flags);
    [LibraryImport("user32.dll")] public static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] public static partial int GetCursorPos(out POINT pt);
    [LibraryImport("user32.dll")] public static partial uint GetDpiForWindow(nint hwnd);
    [LibraryImport("user32.dll")] public static partial short GetAsyncKeyState(int vk);
    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)] public static partial nint FindWindow(string? cls, string title);
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")] public static partial int PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);
    [LibraryImport("user32.dll")] public static partial int UnregisterHotKey(nint hwnd, int id);
    [LibraryImport("user32.dll")] public static partial int IsZoomed(nint hwnd);
    [LibraryImport("user32.dll")] public static partial int GetWindowRect(nint hwnd, out RECT rect);
    [LibraryImport("user32.dll")] public static partial uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")] public static unsafe partial int GetClassName(nint hwnd, char* name, int max);
    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")] public static partial int GetMonitorInfo(nint monitor, ref MONITORINFO info);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW")] public static partial nint SetWindowsHookEx(int id, nint proc, nint module, uint thread);
    [LibraryImport("user32.dll")] public static partial int UnhookWindowsHookEx(nint hook);
    [LibraryImport("user32.dll")] public static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [LibraryImport("gdi32.dll")] public static partial nint CreateRectRgn(int l, int t, int r, int b);
    [LibraryImport("comctl32.dll")] public static partial int SetWindowSubclass(nint hwnd, nint proc, nuint id, nuint data);
    [LibraryImport("comctl32.dll")] public static partial nint DefSubclassProc(nint hwnd, uint msg, nint wParam, nint lParam);
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")] public static partial nint GetModuleHandle(nint name);
    [LibraryImport("dwmapi.dll")] public static partial int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);
    [LibraryImport("dwmapi.dll")] public static partial int DwmExtendFrameIntoClientArea(nint hwnd, ref RECT margins);

    [StructLayout(LayoutKind.Sequential)] public struct DispatcherQueueOptions { public int Size, ThreadType, ApartmentType; }
    [LibraryImport("CoreMessaging.dll")] public static partial int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);
}
