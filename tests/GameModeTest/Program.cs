// Game mode with two screens: a borderless "game" fills the main screen, and the player glances at the
// other screen. Mora must stay quiet the whole time (alerts held, no "while you played" summary) and summarise
// once, when the game really ends. Then the one-screen case: minimized, the game ends after 20 seconds.
// Run through tests/run-game-mode-test.ps1 (its own settings make this executable a game). Takes the mouse.
using System.Diagnostics;
using System.Runtime.InteropServices;

Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
var screens = Screen.AllScreens;
var main = Screen.PrimaryScreen!;
var other = screens.FirstOrDefault(s => !s.Primary);
var mora = Environment.GetEnvironmentVariable("MORA_EXE")!;
int fails = 0;

var game = new Form { Text = "Fake game", FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Bounds = main.Bounds, BackColor = Color.FromArgb(20, 20, 28) };
var aside = other is null ? null : new Form { Text = "Other screen", StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(other.WorkingArea.Left + 100, other.WorkingArea.Top + 100, 500, 300) };
game.Shown += (_, _) => Task.Run(Run);
Application.Run(game);
return fails == 0 ? 0 : 1;

void Run()
{
    try
    {
        var island = FindWindow(null, "Mora");
        if (island == 0) { Fail("Mora is not running"); return; }
        Click(main.Bounds.Left + main.Bounds.Width / 2, main.Bounds.Top + main.Bounds.Height / 2); // the game takes the foreground
        Thread.Sleep(3000); // Mora checks every 1.5 s
        Simulate("headphones \"Studio\" 50");
        Expect(island, false, "an alert during the game is held");

        if (aside != null)
        {
            game.Invoke(() => aside.Show());
            game.Invoke(() => aside.Activate());
            Thread.Sleep(4000);
            Expect(island, false, "a glance at the other screen: no summary");
            Simulate("charging 80");
            Expect(island, false, "an alert while on the other screen is held");
            game.Invoke(() => game.Activate());
            Thread.Sleep(2500);
            Expect(island, false, "back in the game");
            game.Invoke(() => aside.Hide());
        }
        else Console.WriteLine("SKIP: one screen only, the two-screen part did not run");

        // One screen: the game minimized (Alt+Tab out of it) for good.
        game.Invoke(() => game.WindowState = FormWindowState.Minimized);
        var since = Stopwatch.StartNew();
        Thread.Sleep(6000);
        Expect(island, false, "minimized for a few seconds: still the same session, no summary yet");
        while (since.Elapsed.TotalSeconds < 35 && !Shown(island)) Thread.Sleep(250);
        var at = since.Elapsed.TotalSeconds;
        if (at is >= 19 and < 35) Console.WriteLine($"ok   minimized for 20 s: the game is over, its summary shows (after {at:0.0} s)");
        else Fail($"the summary showed after {at:0.0} s, expected about 20 to 23");
    }
    catch (Exception e) { Fail(e.ToString()); }
    finally { game.Invoke(() => Application.Exit()); }
}

void Simulate(string command)
{
    Process.Start(mora, "--simulate " + command)!.WaitForExit(); // as typed: Mora reads the rest of its command line
    Thread.Sleep(1200);
}

static bool Shown(nint island) => GetWindowRgnBox(island, out var box) >= 2 && box.Bottom - box.Top > 4;

void Expect(nint island, bool visible, string what)
{
    bool shown = Shown(island);
    if (shown == visible) Console.WriteLine($"ok   {what}");
    else Fail($"{what}: the island is {(shown ? "showing" : "hidden")}");
}

void Fail(string what)
{
    fails++;
    Console.WriteLine("FAIL " + what);
}

static void Click(int x, int y)
{
    SetCursorPos(x, y);
    mouse_event(2, 0, 0, 0, 0);
    mouse_event(4, 0, 0, 0, 0);
}

[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint FindWindow(string? cls, string title);
[DllImport("user32.dll")] static extern int GetWindowRgnBox(nint hwnd, out RECT box);
[DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] static extern void mouse_event(uint flags, int x, int y, uint data, nint extra);

struct RECT { public int Left, Top, Right, Bottom; }
