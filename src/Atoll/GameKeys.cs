using System.Runtime.InteropServices;

namespace Atoll;

// Many games read the keyboard as raw input with RIDEV_NOHOTKEYS (League of Legends does), which
// silently turns off every RegisterHotKey shortcut while they have focus. During a game where the
// shortcut belongs to Atoll, a low-level keyboard hook reads it instead. The hook lives on a thread of
// its own with nothing else to do, so a busy UI thread can never delay the game's keystrokes, and it
// only exists while such a game is in front. Only the shortcut is swallowed; every other key passes.
static class GameKeys
{
    const int WH_KEYBOARD_LL = 13;
    const nint WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
    const uint WM_QUIT = 0x12;

    static Action? _onShortcut;
    static uint _mods, _vk;
    static Thread? _thread;
    static uint _threadId;
    static bool _swallowing;

    public static void Start(Action onShortcut, uint mods, uint vk)
    {
        (_onShortcut, _mods, _vk) = (onShortcut, mods, vk);
        if (_thread != null) return;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            nint hook;
            unsafe { hook = SetWindowsHookEx(WH_KEYBOARD_LL, (nint)(delegate* unmanaged<int, nint, nint, nint>)&Proc, GetModuleHandle(0), 0); }
            ready.Set();
            while (GetMessage(out _, 0, 0, 0) > 0) { } // LL hooks are called through this thread's message loop
            UnhookWindowsHookEx(hook);
        }) { IsBackground = true, Name = "Atoll game keys" };
        _thread.Start();
        ready.Wait();
    }

    public static void Stop()
    {
        if (_thread == null) return;
        PostThreadMessage(_threadId, WM_QUIT, 0, 0);
        _thread = null;
    }

    [UnmanagedCallersOnly]
    static unsafe nint Proc(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var key = (KeyInfo*)lParam;
            if (key->Vk == _vk)
            {
                bool down = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;
                if (down && Hotkey.Held() == _mods)
                {
                    if (!_swallowing) _onShortcut?.Invoke(); // not again on auto-repeat
                    _swallowing = true;
                    return 1;
                }
                if (!down && _swallowing)
                {
                    _swallowing = false;
                    return 1; // the matching key-up, so the game never sees half a key press
                }
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KeyInfo { public uint Vk, Scan, Flags, Time; public nint Extra; }

    [DllImport("user32.dll")] static extern nint SetWindowsHookEx(int id, nint proc, nint module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32.dll")] static extern int GetMessage(out Msg msg, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint msg, nint wParam, nint lParam);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] static extern nint GetModuleHandle(nint name);

    [StructLayout(LayoutKind.Sequential)]
    struct Msg { public nint Hwnd; public uint Message; public nint WParam, LParam; public uint Time; public int X, Y; }
}
