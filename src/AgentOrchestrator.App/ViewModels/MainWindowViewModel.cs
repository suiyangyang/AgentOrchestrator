namespace AgentOrchestrator.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public ChatWorkspaceViewModel Chat { get; } = new();
    public SettingsViewModel Settings { get; } = new();
}