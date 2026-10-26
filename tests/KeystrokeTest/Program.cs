// Types continuously into a text box while driving the island through every state
// (shortcut open/close, click inside the panel, click outside). Passes only if every
// typed character arrives and this window stays foreground the whole time.
// Start Atoll first, then run this.
using System.Runtime.InteropServices;
using System.Text;

Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

var mon = Screen.PrimaryScreen!.Bounds;
GetDpiForMonitor(MonitorFromPoint(default, 1), 0, out var dpi, out _);
double s = dpi / 96.0;
int Px(double dip) => (int)(dip * s);

// Clipboard calls need a single-threaded apartment, which top-level statements do not give Main.
int exit = 1;
var ui = new Thread(() =>
{
    var form = new Form
    {
        Text = "Atoll keystroke test",
        StartPosition = FormStartPosition.Manual,
        Bounds = new Rectangle(mon.Left + mon.Width / 2 - Px(300), mon.Top + Px(360), Px(600), Px(300)),
    };
    var box = new TextBox { Multiline = true, Dock = DockStyle.Top, Height = Px(180) };
    form.Controls.Add(box);
    string? dropped = null; // a file dragged out of the shelf lands here
    form.AllowDrop = true;
    form.DragEnter += (_, e) => e.Effect = e.Data!.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    form.DragDrop += (_, e) => dropped = ((string[])e.Data!.GetData(DataFormats.FileDrop)!)[0];

    form.Shown += async (_, _) =>
    {
        var fg = form.Handle;
        var outside = new Point(form.Left + form.Width / 2, form.Bottom - Px(40)); // form area below the text box
        // Points inside the open panel, in DIPs from its top-left corner (430 wide, 8 below the screen edge).
        Point Panel(int x, int y) => new(mon.Left + mon.Width / 2 - Px(215) + Px(x), mon.Top + Px(8 + y));

        // The test copies text, so put the user's clipboard back afterwards.
        var savedText = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        var savedHtml = Clipboard.ContainsText(TextDataFormat.Html) ? Clipboard.GetText(TextDataFormat.Html) : null;
        try
        {
            var host = new Host(
                Text: () => (string)form.Invoke(() => box.Text),
                Copy: t => form.Invoke(() => Clipboard.SetText(t)),
                Fullscreen: on => form.Invoke(() =>
                {
                    form.FormBorderStyle = on ? FormBorderStyle.None : FormBorderStyle.Sizable;
                    form.Bounds = on ? mon : new Rectangle(mon.Left + mon.Width / 2 - Px(300), mon.Top + Px(360), Px(600), Px(300));
                }),
                StartDrag: path => form.BeginInvoke(() => form.DoDragDrop(new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy)),
                Dropped: () => (string?)form.Invoke(() => dropped));
            exit = await Task.Run(() => Run(fg, outside, Panel, s, host));
        }
        finally
        {
            var o = new DataObject();
            if (savedText != null) o.SetText(savedText);
            if (savedHtml != null) o.SetText(savedHtml, TextDataFormat.Html);
            if (savedText != null || savedHtml != null) Clipboard.SetDataObject(o, true);
        }
        form.Close();
    };
    Application.Run(form);
});
ui.SetApartmentState(ApartmentState.STA);
ui.Start();
ui.Join();
return exit;

static int Run(nint fg, Point outside, Func<int, int, Point> panel, double s, Host host)
{
    var play = panel(215, 123);
    Point mediaTab = panel(40, 22), clipboardTab = panel(260, 22), pasteFirst = panel(395, 56), firstShelfRow = panel(100, 56);
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
    if (GetWindowRgnBox(island, out _) == 0) // ERROR: no region at all, the whole invisible window eats clicks
    {
        Console.WriteLine("FAIL: island window has no region, it blocks clicks around the pill");
        return 1;
    }

    // The island's window region follows its shape: empty when hidden, short for an alert, tall for the panel.
    void Expect(string state, string step)
    {
        double h = GetWindowRgnBox(island, out var box) == 2 ? box.Bottom - box.Top : 0;
        var actual = h == 0 ? "hidden" : h < 100 * s ? "alert" : "panel";
        if (actual != state) throw new InvalidOperationException($"island should show {state} after {step}, shows {actual}");
    }

    // A browser finishing a download renames its temp file; Atoll answers with an alert.
    var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    void FinishDownload(int n)
    {
        var temp = Path.Combine(downloads, $"atoll-keystroke-test-{n}.txt.crdownload");
        File.WriteAllText(temp, "test");
        File.Move(temp, temp[..^".crdownload".Length]);
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
            FinishDownload(cycle); Type(30); Expect("alert", "a download finished"); // pops up mid-sentence
            Shortcut(); Type(40); Expect("panel", "the shortcut");         // open over the alert, typing through the spring
            Click(play); Type(20); Click(play); Type(20); Expect("panel", "button clicks"); // play/pause twice: music ends as it was
            Shortcut(); Type(40); Expect("hidden", "the shortcut");        // close with the shortcut
            var clip = $"[pasted {cycle}]";
            host.Copy(clip); Type(20); Expect("alert", "a copy");    // copy alert, mid-sentence
            Shortcut(); Type(20); Expect("panel", "the shortcut");         // open again
            Click(clipboardTab); Thread.Sleep(400);
            Click(pasteFirst); Thread.Sleep(300);               // Atoll types Ctrl+V into this window
            expected.Append(clip);
            Click(mediaTab); Type(30);
            Click(outside); Type(40); Expect("hidden", "outside click"); // click outside closes it
        }

        // Shelf: drag a file from this window onto the open panel, then drag it back out of the shelf.
        var file = Path.Combine(Path.GetTempPath(), "atoll-shelf-test.txt");
        File.WriteAllText(file, "shelf");
        Shortcut(); Type(30); Expect("panel", "the shortcut");
        MoveTo(outside); Down(); host.StartDrag(file); Thread.Sleep(150);
        DragTo(panel(215, 60)); Thread.Sleep(400); Up(); Thread.Sleep(600);
        Expect("panel", "a file dropped on it");                   // the press started outside, the panel stays
        MoveTo(firstShelfRow); Down(); DragTo(outside); Thread.Sleep(400); Up(); Thread.Sleep(600);
        if (!string.Equals(host.Dropped(), file, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"the shelf handed back \"{host.Dropped()}\" instead of the dropped file");
        File.Delete(file);
        Type(30);
        Click(outside); Type(40); Expect("hidden", "outside click");

        // A borderless fullscreen "game" with the default setting: the island stays out of the way.
        host.Fullscreen(true); Type(250);                        // Atoll checks every 1.5 s
        FinishDownload(98); Type(40); Expect("hidden", "a download during a game");
        Shortcut(); Type(40); Expect("hidden", "the shortcut during a game"); // the shortcut is released to the game
        host.Fullscreen(false); Type(200); Expect("alert", "leaving the game"); // one summary of what was held
    }
    catch (InvalidOperationException e)
    {
        Console.WriteLine($"FAIL: {e.Message}, stopped typing");
        return 1;
    }
    finally
    {
        foreach (var f in Directory.GetFiles(downloads, "atoll-keystroke-test-*")) File.Delete(f);
    }
    Thread.Sleep(500);

    var got = host.Text();
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

// Atoll's default shortcut: Alt + the key above Tab (² on AZERTY, ` on QWERTY).
static void Shortcut()
{
    var key = (ushort)MapVirtualKey(0x29, 1);
    Key(0x12, '\0', 0); Key(key, '\0', 0); Key(key, '\0', 2);
    Thread.Sleep(60); // a person holds Alt a little longer than the key
    Key(0x12, '\0', 2);
}

static void MoveTo(Point p)
{
    var screen = Screen.PrimaryScreen!.Bounds;
    Send(new INPUT { Type = 0, Mi = new MOUSEINPUT { Dx = p.X * 65536 / screen.Width, Dy = p.Y * 65536 / screen.Height, Flags = 0x1 | 0x8000 } }); // MOVE | ABSOLUTE
}

// Small steps, so drag-and-drop sees real movement rather than a jump.
static void DragTo(Point to)
{
    GetCursorPos(out var from);
    for (int i = 1; i <= 20; i++)
    {
        MoveTo(new Point(from.X + (to.X - from.X) * i / 20, from.Y + (to.Y - from.Y) * i / 20));
        Thread.Sleep(15);
    }
}

static void Down() => Send(new INPUT { Type = 0, Mi = new MOUSEINPUT { Flags = 0x2 } });
static void Up() => Send(new INPUT { Type = 0, Mi = new MOUSEINPUT { Flags = 0x4 } });

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
[DllImport("user32.dll")] static extern bool GetCursorPos(out Point p);
[DllImport("user32.dll")] static extern nint GetForegroundWindow();
[DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint FindWindow(string? cls, string title);
[DllImport("user32.dll")] static extern int GetWindowRgnBox(nint hwnd, out RECT box);
[DllImport("user32.dll")] static extern nint MonitorFromPoint(Point pt, uint flags);
[DllImport("shcore.dll")] static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);

[StructLayout(LayoutKind.Explicit)]
struct INPUT
{
    [FieldOffset(0)] public uint Type;
    [FieldOffset(8)] public MOUSEINPUT Mi;
    [FieldOffset(8)] public KEYBDINPUT Ki;
}
record Host(Func<string> Text, Action<string> Copy, Action<bool> Fullscreen, Action<string> StartDrag, Func<string?> Dropped);
struct RECT { public int Left, Top, Right, Bottom; }
struct MOUSEINPUT { public int Dx, Dy; public uint Data, Flags, Time; public nint Extra; }
struct KEYBDINPUT { public ushort Vk; public char Scan; public uint Flags, Time; public nint Extra; }
