using System.Windows;

namespace GPTAutoResume;

public partial class MainWindow : Window
{
    private readonly MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(this);
        DataContext = _viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        DisposeViewModel();
        base.OnClosed(e);
    }

    public void DisposeViewModel() => _viewModel?.Dispose();
}
