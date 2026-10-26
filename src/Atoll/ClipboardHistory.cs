using System.Runtime.InteropServices;
using System.Text;

namespace Atoll;

// A copied text, or a copied image kept as the clipboard's own DIB bytes.
sealed class Clip(string? text, byte[]? dib)
{
    public string? Text { get; } = text;
    public byte[]? Dib { get; } = dib;
    public Microsoft.UI.Xaml.Media.ImageSource? Thumb { get; set; }
    public int Width => BitConverter.ToInt32(Dib!, 4);
    public int Height => Math.Abs(BitConverter.ToInt32(Dib!, 8)); // negative for top-down images

    public bool Same(Clip other) => Text != null ? Text == other.Text : other.Dib != null && Dib.AsSpan().SequenceEqual(other.Dib);

    // The DIB as a .bmp file, for the image decoder: a 14 byte file header pointing at the pixels.
    public byte[] Bmp()
    {
        var dib = Dib!;
        int header = BitConverter.ToInt32(dib, 0), bits = BitConverter.ToUInt16(dib, 14), compression = BitConverter.ToInt32(dib, 16), used = BitConverter.ToInt32(dib, 32);
        int colors = used != 0 ? used : bits <= 8 ? 1 << bits : 0;
        int pixels = 14 + header + (compression == 3 && header == 40 ? 12 : 0) + colors * 4; // BI_BITFIELDS masks, palette
        var bmp = new byte[14 + dib.Length];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BitConverter.TryWriteBytes(bmp.AsSpan(2), bmp.Length);
        BitConverter.TryWriteBytes(bmp.AsSpan(10), pixels);
        dib.CopyTo(bmp, 14);
        return bmp;
    }
}

// Recent copies of text and images, kept in memory only (nothing written to disk). Copies that password
// managers and other apps flag as private are never recorded.
sealed class ClipboardHistory
{
    public const uint WM_CLIPBOARDUPDATE = 0x031D;
    const uint CF_UNICODETEXT = 13, CF_DIB = 8;
    static readonly uint[] PrivateFormats =
    [
        RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"),
        RegisterClipboardFormat("Clipboard Viewer Ignore"),
        RegisterClipboardFormat("CanIncludeInClipboardHistory"), // usually set to 0; treated as private either way
    ];

    readonly nint _hwnd;
    Clip? _selfCopy;
    public List<Clip> Items { get; } = [];

    public ClipboardHistory(nint hwnd)
    {
        _hwnd = hwnd;
        AddClipboardFormatListener(hwnd);
    }

    // Called on WM_CLIPBOARDUPDATE. Returns the new copy when it is a fresh one worth announcing.
    // Text wins over an image: copying cells or a document often puts both on the clipboard.
    public Clip? OnUpdate()
    {
        if (PrivateFormats.Any(f => IsClipboardFormatAvailable(f))) return null;
        var text = ReadText()?.Trim();
        var clip = !string.IsNullOrEmpty(text) ? new Clip(text, null) : Read(CF_DIB) is { } dib ? new Clip(null, dib) : null;
        if (clip is null) return null;
        bool fresh = Items.Count == 0 || !Items[0].Same(clip); // OLE apps report one copy twice
        if (Items.Find(clip.Same) is { } known)
        {
            Items.Remove(known);
            clip = known; // keeps its thumbnail
        }
        Items.Insert(0, clip);
        // ponytail: at most 3 images (a 4K screenshot is 33 MB as a DIB); keep them compressed if more are wanted.
        if (Items.Count(i => i.Dib != null) > 3) Items.Remove(Items.Last(i => i.Dib != null));
        if (Items.Count > 8) Items.RemoveAt(8);
        if (_selfCopy != null && clip.Same(_selfCopy))
        {
            _selfCopy = null; // our own Paste put it there
            return null;
        }
        return fresh ? clip : null;
    }

    // Puts the copy back on the clipboard and sends Ctrl+V to whatever app is in the foreground.
    // The island never takes focus, so that app is the one the user was working in.
    public void Paste(Clip clip)
    {
        _selfCopy = clip;
        if (!Open()) return;
        try
        {
            EmptyClipboard();
            var bytes = clip.Text is { } text ? Encoding.Unicode.GetBytes(text + "\0") : clip.Dib!;
            var mem = GlobalAlloc(0x2, (nuint)bytes.Length); // GMEM_MOVEABLE
            Marshal.Copy(bytes, 0, GlobalLock(mem), bytes.Length);
            GlobalUnlock(mem);
            SetClipboardData(clip.Text != null ? CF_UNICODETEXT : CF_DIB, mem); // the clipboard owns mem from here
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

    byte[]? Read(uint format)
    {
        if (!IsClipboardFormatAvailable(format) || !Open()) return null;
        try
        {
            var mem = GetClipboardData(format);
            if (mem == 0) return null;
            var bytes = new byte[(int)GlobalSize(mem)];
            try { Marshal.Copy(GlobalLock(mem), bytes, 0, bytes.Length); }
            finally { GlobalUnlock(mem); }
            return bytes.Length > 40 ? bytes : null; // at least a BITMAPINFOHEADER
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
    [DllImport("kernel32.dll")] static extern nuint GlobalSize(nint mem);
    [DllImport("user32.dll")] static extern uint SendInput(uint count, ref Input inputs, int size);
}
