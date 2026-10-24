using Microsoft.UI.Xaml;

namespace Atoll;

public partial class App : Application
{
    IslandWindow? _island;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args) => _island = new IslandWindow();
}
