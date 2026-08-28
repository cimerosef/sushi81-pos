namespace Sushi81.Pos.Desktop;

public partial class MainWindow : System.Windows.Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void OnLanguageSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DataContext is not ShellViewModel viewModel || e.AddedItems.OfType<LanguageOption>().SingleOrDefault() is not { } language)
        {
            return;
        }

        try
        {
            await viewModel.ChangeLanguageAsync(language);
        }
        catch
        {
            System.Windows.MessageBox.Show(
                this,
                viewModel.LanguageSaveFailure,
                viewModel.Title,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }
}
