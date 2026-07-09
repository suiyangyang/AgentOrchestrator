using AgentOrchestrator.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgentOrchestrator.App.Controls;

public sealed partial class TaskOrchestrationWorkspaceControl : UserControl
{
    public TaskOrchestrationWorkspaceControl()
    {
        InitializeComponent();
    }

    private void OnDeleteZoneClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DynamicZoneDefinitionViewModel zoneVm }
            && DataContext is TaskOrchestrationWorkspaceViewModel vm)
        {
            vm.Editor.RemoveDynamicZoneCommand.Execute(zoneVm);
        }
    }
}
