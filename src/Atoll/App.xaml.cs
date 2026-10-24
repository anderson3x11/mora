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
            if (running != 0) Native.PostMessage(running, Tray.Message, 1, 0x202); // as a click on its tray icon
            Environment.Exit(0);
        }
        _island = new IslandWindow();
    }
}
