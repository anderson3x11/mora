using System.Runtime.InteropServices;

namespace Atoll;

// The shortcut that opens the island: modifier flags as RegisterHotKey takes them, plus a virtual key.
static class Hotkey
{
    public const uint Alt = 0x1, Ctrl = 0x2, Shift = 0x4, Win = 0x8, NoRepeat = 0x4000;

    // The key above Tab, by position rather than letter: ² on AZERTY, ` on QWERTY, ^ on QWERTZ.
    // Never needed to type with Alt held, and rarely bound by games, unlike Alt + a letter.
    public static uint KeyAboveTab => MapVirtualKey(0x29, 1); // scan code 0x29, MAPVK_VSC_TO_VK
    public static string DefaultName => Name(Alt, KeyAboveTab);

    // "Alt + Q", "Win + Alt + Space", "Alt + ²": key names from the current keyboard layout.
    public static string Name(uint mods, uint vk)
    {
        var parts = new List<string>();
        if ((mods & Win) != 0) parts.Add("Win");
        if ((mods & Ctrl) != 0) parts.Add("Ctrl");
        if ((mods & Alt) != 0) parts.Add("Alt");
        if ((mods & Shift) != 0) parts.Add("Shift");
        parts.Add(KeyName(vk));
        return string.Join(" + ", parts);
    }

    static string KeyName(uint vk) => vk switch
    {
        0x20 => "Space",
        0x09 => "Tab",
        0x0D => "Enter",
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
        >= 0x25 and <= 0x28 => ((string[])["Left", "Up", "Right", "Down"])[vk - 0x25],
        _ => MapVirtualKey(vk, 2) is var ch and > 0 ? char.ToUpperInvariant((char)(ch & 0xFFFF)).ToString() : $"Key {vk}",
    };

    // Modifiers held right now, as RegisterHotKey flags.
    public static uint Held()
    {
        static bool Down(int vk) => GetAsyncKeyState(vk) < 0;
        uint mods = 0;
        if (Down(0x12)) mods |= Alt;
        if (Down(0x11)) mods |= Ctrl;
        if (Down(0x10)) mods |= Shift;
        if (Down(0x5B) || Down(0x5C)) mods |= Win;
        return mods;
    }

    public static bool IsModifierKey(uint vk) => vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;

    // Free when nothing else (Windows, another app) holds it. Atoll's own current shortcut counts as taken.
    public static bool IsFree(uint mods, uint vk)
    {
        if (!RegisterHotKey(0, 0xBFFE, mods | NoRepeat, vk)) return false;
        UnregisterHotKey(0, 0xBFFE);
        return true;
    }

    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(nint hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(nint hwnd, int id);
}
