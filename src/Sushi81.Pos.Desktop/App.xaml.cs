namespace Sushi81.Pos.Desktop;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        CompositionRoot.Start(this);
    }
}
