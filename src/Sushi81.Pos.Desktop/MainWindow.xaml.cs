namespace Sushi81.Pos.Desktop;

public partial class MainWindow : System.Windows.Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
