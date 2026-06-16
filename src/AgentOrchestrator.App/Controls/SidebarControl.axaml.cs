using Avalonia.Controls;
using Avalonia.Interactivity;
using AgentOrchestrator.App.ViewModels;

namespace AgentOrchestrator.App.Controls;

public partial class SidebarControl : UserControl
{
    public SidebarControl()
    {
        InitializeComponent();
    }

    private void OnSessionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SidebarSessionViewModel vm } &&
            DataContext is SidebarViewModel sidebar)
        {
            sidebar.SelectSessionCommand.Execute(vm.SessionId);
        }
    }

    private void OnProjectHeaderClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SidebarProjectViewModel p })
        {
            p.IsExpanded = !p.IsExpanded;
        }
    }

    private void OnProjectsHeaderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SidebarViewModel vm)
        {
            // Toggle expand on all project nodes.
            foreach (var p in vm.Projects)
            {
                p.IsExpanded = !p.IsExpanded;
            }
        }
    }

    private void OnOrphansHeaderClick(object? sender, RoutedEventArgs e)
    {
        // Orphan group has no children of its own — header click is a no-op for v1.
    }
}
