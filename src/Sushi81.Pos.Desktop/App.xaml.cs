namespace Sushi81.Pos.Desktop;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        await CompositionRoot.StartAsync(this);
    }
}
