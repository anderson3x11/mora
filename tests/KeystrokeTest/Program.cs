// Types continuously into a text box while driving the island through every state
// (Alt+Q open/close, click inside the panel, click outside). Passes only if every
// typed character arrives and this window stays foreground the whole time.
// Start Atoll first, then run this.
using System.Runtime.InteropServices;
using System.Text;

Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

var mon = Screen.PrimaryScreen!.Bounds;
GetDpiForMonitor(MonitorFromPoint(default, 1), 0, out var dpi, out _);
double s = dpi / 96.0;
int Px(double dip) => (int)(dip * s);

var form = new Form
{
    Text = "Atoll keystroke test",
    StartPosition = FormStartPosition.Manual,
    Bounds = new Rectangle(mon.Left + mon.Width / 2 - Px(300), mon.Top + Px(360), Px(600), Px(300)),
};
var box = new TextBox { Multiline = true, Dock = DockStyle.Top, Height = Px(180) };
form.Controls.Add(box);

int exit = 1;
form.Shown += async (_, _) =>
{
    var fg = form.Handle;
    var outside = new Point(form.Left + form.Width / 2, form.Bottom - Px(40)); // form area below the text box
    var panel = new Point(mon.Left + mon.Width / 2, mon.Top + Px(110));
    exit = await Task.Run(() => Run(fg, outside, panel, () => (string)form.Invoke(() => box.Text)));
    form.Close();
};
Application.Run(form);
return exit;

static int Run(nint fg, Point outside, Point panel, Func<string> text)
{
    Click(outside);
    Thread.Sleep(300);
    if (GetForegroundWindow() != fg)
    {
        Console.WriteLine("FAIL: test window could not take the foreground");
        return 2;
    }

    var island = FindWindow(null, "Atoll");
    if (island == 0)
    {
        Console.WriteLine("FAIL: Atoll is not running");
        return 2;
    }
    // The island's window region is empty when hidden and covers the pill when open.
    void Expect(bool open, string step)
    {
        if ((GetWindowRgnBox(island, out _) == 2) != open)
            throw new InvalidOperationException($"island should be {(open ? "open" : "hidden")} after {step}");
    }

    var rng = new Random(1);
    var expected = new StringBuilder();
    void Type(int n)
    {
        for (int i = 0; i < n; i++)
        {
            var ch = "abcdefghijklmnopqrstuvwxyz "[rng.Next(27)];
            Key(0, ch, 0x4); Key(0, ch, 0x4 | 0x2); // KEYEVENTF_UNICODE, then KEYUP
            expected.Append(ch);
            Thread.Sleep(8);
            if (GetForegroundWindow() != fg) throw new InvalidOperationException($"foreground lost after {expected.Length} characters");
        }
    }

    try
    {
        for (int cycle = 0; cycle < 10; cycle++)
        {
            AltQ(); Type(40); Expect(true, "Alt+Q");           // open, typing through the spring
            Click(panel); Type(40); Expect(true, "panel click"); // click inside the open panel
            AltQ(); Type(40); Expect(false, "Alt+Q");          // close with the shortcut
            AltQ(); Type(30); Expect(true, "Alt+Q");           // open again
            Click(outside); Type(40); Expect(false, "outside click"); // click outside closes it
        }
    }
    catch (InvalidOperationException e)
    {
        Console.WriteLine($"FAIL: {e.Message}, stopped typing");
        return 1;
    }
    Thread.Sleep(500);

    var got = text();
    bool ok = got == expected.ToString();
    Console.WriteLine($"typed {expected.Length}, received {got.Length}, foreground kept");
    if (got != expected.ToString())
    {
        int i = 0;
        while (i < got.Length && i < expected.Length && got[i] == expected[i]) i++;
        Console.WriteLine($"first difference at {i}: expected \"{Slice(expected.ToString(), i)}\" got \"{Slice(got, i)}\"");
    }
    Console.WriteLine(ok ? "PASS" : "FAIL");
    return ok ? 0 : 1;
}

static string Slice(string t, int i) => t.Substring(i, Math.Min(20, t.Length - i));

static void AltQ()
{
    Key(0x12, '\0', 0); Key((ushort)'Q', '\0', 0); Key((ushort)'Q', '\0', 2);
    Thread.Sleep(60); // a person holds Alt a little longer than Q
    Key(0x12, '\0', 2);
}

static void Click(Point p)
{
    SetCursorPos(p.X, p.Y);
    Thread.Sleep(30);
    Send(new INPUT { Type = 0, Mi = new MOUSEINPUT { Flags = 0x2 } }); // LEFTDOWN
    Send(new INPUT { Type = 0, Mi = new MOUSEINPUT { Flags = 0x4 } }); // LEFTUP
}

static void Key(ushort vk, char scan, uint flags) => Send(new INPUT { Type = 1, Ki = new KEYBDINPUT { Vk = vk, Scan = scan, Flags = flags } });

static unsafe void Send(INPUT input) => SendInput(1, &input, sizeof(INPUT));

[DllImport("user32.dll")] static extern unsafe uint SendInput(uint n, INPUT* inputs, int size);
[DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] static extern nint GetForegroundWindow();
[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint FindWindow(string? cls, string title);
[DllImport("user32.dll")] static extern int GetWindowRgnBox(nint hwnd, out Rectangle box);
[DllImport("user32.dll")] static extern nint MonitorFromPoint(Point pt, uint flags);
[DllImport("shcore.dll")] static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);

[StructLayout(LayoutKind.Explicit)]
struct INPUT
{
    [FieldOffset(0)] public uint Type;
    [FieldOffset(8)] public MOUSEINPUT Mi;
    [FieldOffset(8)] public KEYBDINPUT Ki;
}
struct MOUSEINPUT { public int Dx, Dy; public uint Data, Flags, Time; public nint Extra; }
struct KEYBDINPUT { public ushort Vk; public char Scan; public uint Flags, Time; public nint Extra; }
