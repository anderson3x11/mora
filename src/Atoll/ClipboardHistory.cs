using System.Runtime.InteropServices;

namespace Atoll;

// Recent text copies, kept in memory only (nothing written to disk). Copies that password managers
// and other apps flag as private are never recorded.
sealed class ClipboardHistory
{
    public const uint WM_CLIPBOARDUPDATE = 0x031D;
    const uint CF_UNICODETEXT = 13;
    static readonly uint[] PrivateFormats =
    [
        RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"),
        RegisterClipboardFormat("Clipboard Viewer Ignore"),
        RegisterClipboardFormat("CanIncludeInClipboardHistory"), // usually set to 0; treated as private either way
    ];

    readonly nint _hwnd;
    string? _selfCopy;
    public List<string> Items { get; } = [];

    public ClipboardHistory(nint hwnd)
    {
        _hwnd = hwnd;
        AddClipboardFormatListener(hwnd);
    }

    // Called on WM_CLIPBOARDUPDATE. Returns the new text when it is a fresh copy worth announcing.
    public string? OnUpdate()
    {
        if (PrivateFormats.Any(f => IsClipboardFormatAvailable(f))) return null;
        var text = ReadText()?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        bool fresh = Items.Count == 0 || Items[0] != text; // OLE apps report one copy twice
        Items.Remove(text);
        Items.Insert(0, text);
        if (Items.Count > 8) Items.RemoveAt(8);
        if (text == _selfCopy)
        {
            _selfCopy = null; // our own Paste put it there
            return null;
        }
        return fresh ? text : null;
    }

    // Puts the text on the clipboard and sends Ctrl+V to whatever app is in the foreground.
    // The island never takes focus, so that app is the one the user was working in.
    public void Paste(string text)
    {
        _selfCopy = text;
        if (!Open()) return;
        try
        {
            EmptyClipboard();
            var bytes = (text.Length + 1) * 2;
            var mem = GlobalAlloc(0x2, (nuint)bytes); // GMEM_MOVEABLE
            Marshal.Copy((text + "\0").ToCharArray(), 0, GlobalLock(mem), text.Length + 1);
            GlobalUnlock(mem);
            SetClipboardData(CF_UNICODETEXT, mem); // the clipboard owns mem from here
        }
        finally { CloseClipboard(); }

        Span<Input> keys =
        [
            Key(0x11, false), Key(0x56, false), Key(0x56, true), Key(0x11, true), // Ctrl down, V, Ctrl up
        ];
        SendInput((uint)keys.Length, ref keys[0], Marshal.SizeOf<Input>());
    }

    string? ReadText()
    {
        if (!IsClipboardFormatAvailable(CF_UNICODETEXT) || !Open()) return null;
        try
        {
            var mem = GetClipboardData(CF_UNICODETEXT);
            if (mem == 0) return null;
            try { return Marshal.PtrToStringUni(GlobalLock(mem)); }
            finally { GlobalUnlock(mem); }
        }
        finally { CloseClipboard(); }
    }

    // Another app may hold the clipboard for a moment right after copying.
    bool Open()
    {
        for (int i = 0; i < 10; i++)
        {
            if (OpenClipboard(_hwnd)) return true;
            Thread.Sleep(10);
        }
        return false;
    }

    static Input Key(ushort vk, bool up) => new() { Type = 1, Vk = vk, Flags = up ? 2u : 0 };

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ushort Vk;
        [FieldOffset(12)] public uint Flags;
    }

    [DllImport("user32.dll")] static extern bool AddClipboardFormatListener(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string name);
    [DllImport("user32.dll")] static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern bool EmptyClipboard();
    [DllImport("user32.dll")] static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll")] static extern nint SetClipboardData(uint format, nint mem);
    [DllImport("kernel32.dll")] static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll")] static extern nint GlobalLock(nint mem);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(nint mem);
    [DllImport("user32.dll")] static extern uint SendInput(uint count, ref Input inputs, int size);
}
