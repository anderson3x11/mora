using Microsoft.UI.Xaml;

namespace Atoll;

public partial class App : Application
{
    IslandWindow? _island;
    Mutex? _single;

    public App()
    {
        InitializeComponent();
        // A background utility that vanishes is worse than one missing a feature:
        // log the unexpected and keep running.
        UnhandledException += (_, e) =>
        {
            e.Handled = true;
            try
            {
                var log = Path.Combine(Path.GetDirectoryName(Settings.FilePath)!, "errors.log");
                Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                File.AppendAllText(log, $"{DateTime.Now:u} {e.Exception}\n\n");
            }
            catch (IOException) { }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // One island only: launching again (Start menu, startup) opens the running one instead.
        _single = new Mutex(true, @"Local\Atoll.Island", out bool first);
        if (!first)
        {
            var running = Native.FindWindow(null, "Atoll");
            var line = Environment.CommandLine;
            int at = line.IndexOf("--simulate ", StringComparison.Ordinal);
            if (running != 0 && at >= 0) SendSimulation(running, line[(at + "--simulate ".Length)..]); // test hook, see IslandWindow.Simulate
            else if (running != 0) Native.PostMessage(running, Tray.Message, 1, 0x202); // as a click on its tray icon
            Environment.Exit(0);
        }
        _island = new IslandWindow();
    }

    static void SendSimulation(nint window, string command)
    {
        var text = System.Runtime.InteropServices.Marshal.StringToHGlobalUni(command);
        var data = new IslandWindow.CopyData { Bytes = command.Length * 2, Data = text };
        var block = System.Runtime.InteropServices.Marshal.AllocHGlobal(System.Runtime.InteropServices.Marshal.SizeOf(data));
        System.Runtime.InteropServices.Marshal.StructureToPtr(data, block, false);
        SendMessage(window, IslandWindow.WM_COPYDATA, 0, block);
        System.Runtime.InteropServices.Marshal.FreeHGlobal(block);
        System.Runtime.InteropServices.Marshal.FreeHGlobal(text);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern nint SendMessage(nint hwnd, uint msg, nint wParam, nint lParam);
}
