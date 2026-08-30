using Microsoft.UI.Xaml;

namespace OsageLagtrain.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow();
        window.Activate();
    }
}

public sealed class MainWindow : Window
{
    public MainWindow()
    {
        Title = "Osage Lagtrain";
        Content = new Microsoft.UI.Xaml.Controls.TextBlock
        {
            Text = "Osage Lagtrain — WinUI3 PerMonitorV2 bootstrap OK",
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
        };
    }
}
