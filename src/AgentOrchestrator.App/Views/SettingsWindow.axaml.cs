using Avalonia.Controls;
using AgentOrchestrator.App.ViewModels;

namespace AgentOrchestrator.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is SettingsViewModel vm)
        {
            vm.HostWindow = this;
            _ = vm.InitializeAsync();
        }
    }
}
