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

    private void OnProjectsHeaderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SidebarViewModel vm)
        {
            vm.ToggleProjectsExpanded();
        }
    }

    private void OnTaskGraphsHeaderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SidebarViewModel vm)
        {
            vm.ToggleTaskGraphsExpanded();
        }
    }

    private void OnProjectSectionAddClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SidebarViewModel vm)
        {
            vm.RequestAddProjectCommand.Execute(null);
        }
    }

    private void OnProjectSectionMoreClick(object? sender, RoutedEventArgs e)
    {
        // No section-level "..." menu; clicking the "..." on the header is
        // a no-op in v1 to keep the icon present.
    }

    private void OnTaskGraphClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SidebarTaskGraphItemViewModel vm }
            && DataContext is SidebarViewModel sidebar)
        {
            sidebar.SelectTaskGraphCommand.Execute(vm.Id);
            sidebar.RequestTaskGraphCommand.Execute(null);
            sidebar.OpenTaskGraphCommand.Execute(vm.Id);
        }
    }

    private void OnTaskGraphRowMoreClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Control btn) return;
        if (btn.Tag is not string taskGraphId) return;
        ShowTaskGraphMenu(btn, taskGraphId);
    }

    private void OnProjectRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SidebarProjectViewModel p }
            && DataContext is SidebarViewModel vm)
        {
            p.IsExpanded = !p.IsExpanded;
            vm.SyncProjectsExpandedState();
            vm.SelectProjectCommand.Execute(p.Id);
        }
    }

    private void OnProjectRowAddClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Control { Tag: string projectId }
            && DataContext is SidebarViewModel vm)
        {
            vm.RequestNewSessionInProjectCommand.Execute(projectId);
        }
    }

    private void OnProjectRowMoreClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Control btn) return;
        if (btn.Tag is not string projectId) return;
        ShowProjectMenu(btn, projectId);
    }

    private void ShowProjectMenu(Control anchor, string projectId)
    {
        RowActionPanel.Children.Clear();

        void Add(string label, ProjectActionKind kind)
        {
            var b = new Button
            {
                Content = BuildMenuRow(label, kind),
                Classes = { "sidebar-menu-item" },
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            };
            b.Click += (_, _) =>
            {
                RowActionPopup.IsOpen = false;
                if (DataContext is SidebarViewModel vm)
                {
                    vm.RequestProjectActionCommand.Execute(new ProjectActionRequest(projectId, kind));
                }
            };
            RowActionPanel.Children.Add(b);
        }

        Add("置顶项目", ProjectActionKind.TogglePin);
        Add("在资源管理器中打开", ProjectActionKind.OpenInExplorer);
        Add("重命名项目", ProjectActionKind.Rename);
        Add("移除", ProjectActionKind.Remove);

        RowActionPopup.PlacementTarget = anchor;
        RowActionPopup.IsOpen = true;
    }

    private static StackPanel BuildMenuRow(string label, object kind)
    {
        var icon = kind switch
        {
            ProjectActionKind.TogglePin => "📌",
            ProjectActionKind.OpenInExplorer => "📁",
            ProjectActionKind.Rename => "✎",
            ProjectActionKind.Remove => "🗑",
            _ => "•",
        };
        return new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = icon, FontSize = 12 },
                new TextBlock { Text = label, FontSize = 12 },
            }
        };
    }

    private void OnSessionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SidebarSessionViewModel vm }
            && DataContext is SidebarViewModel sidebar)
        {
            sidebar.SelectSessionCommand.Execute(vm.SessionId);
        }
    }

    private void OnSessionRowMoreClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Control btn) return;
        if (btn.Tag is not string sessionId) return;
        ShowSessionMenu(btn, sessionId);
    }

    private void OnProjectSessionExpandClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Control { DataContext: SidebarProjectViewModel project })
        {
            project.ShowMoreSessions();
        }
    }

    private void OnProjectSessionCollapseClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Control { DataContext: SidebarProjectViewModel project })
        {
            project.CollapseSessions();
        }
    }

    private void ShowSessionMenu(Control anchor, string sessionId)
    {
        RowActionPanel.Children.Clear();

        void Add(string label, SessionActionKind kind)
        {
            var b = new Button
            {
                Content = BuildSessionMenuRow(label, kind),
                Classes = { "sidebar-menu-item" },
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            };
            b.Click += (_, _) =>
            {
                RowActionPopup.IsOpen = false;
                if (DataContext is SidebarViewModel vm)
                {
                    vm.RequestSessionActionCommand.Execute(new SessionActionRequest(sessionId, kind));
                }
            };
            RowActionPanel.Children.Add(b);
        }

        Add("重命名对话", SessionActionKind.Rename);
        Add("移除", SessionActionKind.Remove);

        RowActionPopup.PlacementTarget = anchor;
        RowActionPopup.IsOpen = true;
    }

    private void ShowTaskGraphMenu(Control anchor, string taskGraphId)
    {
        RowActionPanel.Children.Clear();

        void Add(string label, TaskGraphActionKind kind)
        {
            var b = new Button
            {
                Content = BuildTaskGraphMenuRow(label, kind),
                Classes = { "sidebar-menu-item" },
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            };
            b.Click += (_, _) =>
            {
                RowActionPopup.IsOpen = false;
                if (DataContext is SidebarViewModel vm)
                {
                    vm.RequestTaskGraphActionCommand.Execute(new TaskGraphActionRequest(taskGraphId, kind));
                }
            };
            RowActionPanel.Children.Add(b);
        }

        Add("重命名任务编排", TaskGraphActionKind.Rename);
        Add("移除", TaskGraphActionKind.Remove);

        RowActionPopup.PlacementTarget = anchor;
        RowActionPopup.IsOpen = true;
    }

    private static StackPanel BuildSessionMenuRow(string label, SessionActionKind kind)
    {
        var icon = kind switch
        {
            SessionActionKind.Rename => "✎",
            SessionActionKind.Remove => "🗑",
            _ => "•",
        };
        return new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = icon, FontSize = 12 },
                new TextBlock { Text = label, FontSize = 12 },
            }
        };
    }

    private static StackPanel BuildTaskGraphMenuRow(string label, TaskGraphActionKind kind)
    {
        var icon = kind switch
        {
            TaskGraphActionKind.Rename => "✎",
            TaskGraphActionKind.Remove => "🗑",
            _ => "•",
        };
        return new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = icon, FontSize = 12 },
                new TextBlock { Text = label, FontSize = 12 },
            }
        };
    }

    private void OnOrphansHeaderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SidebarViewModel vm)
        {
            vm.ToggleOrphansExpanded();
        }
    }
}
