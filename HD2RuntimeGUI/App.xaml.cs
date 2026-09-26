namespace HD2RuntimeGUI;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Dark;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new MainPage()) { Title = "HD2Runtime Mod Builder", Width = 1360, Height = 900, MinimumWidth = 820, MinimumHeight = 600 };
    }
}
