using Avalonia.Controls;
using AgentOrchestrator.App.ViewModels;

namespace AgentOrchestrator.App.Views;

public partial class TaskGraphNodeDetailWindow : Window
{
    public TaskGraphNodeDetailWindow()
    {
        InitializeComponent();
    }

    public TaskGraphNodeDetailWindow(TaskGraphNodeDetailViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }
}
