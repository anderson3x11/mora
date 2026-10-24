using Microsoft.UI.Xaml;

namespace Atoll;

public partial class App : Application
{
    IslandWindow? _island;

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

    protected override void OnLaunched(LaunchActivatedEventArgs args) => _island = new IslandWindow();
}
