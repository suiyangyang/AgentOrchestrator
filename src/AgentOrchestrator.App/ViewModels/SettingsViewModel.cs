using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace AgentOrchestrator.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    public Window? HostWindow { get; set; }

    [RelayCommand]
    private void Ok()
    {
        HostWindow?.Close(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        HostWindow?.Close(false);
    }
}
