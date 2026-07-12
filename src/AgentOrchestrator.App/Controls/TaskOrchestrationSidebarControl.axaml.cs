using System;
using System.Threading.Tasks;
using AgentOrchestrator.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace AgentOrchestrator.App.Controls;

public sealed partial class TaskOrchestrationSidebarControl : UserControl
{
    public TaskOrchestrationSidebarControl()
    {
        InitializeComponent();
    }

    private new TaskOrchestrationWorkspaceViewModel? DataContext
        => base.DataContext as TaskOrchestrationWorkspaceViewModel;

    private void OnNewTemplateClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        DataContext?.NewTemplateCommand.Execute(null);
    }

    private void OnNewTaskGraphClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        DataContext?.NewTaskGraphCommand.Execute(null);
    }

    private void OnTemplateRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SidebarTaskGraphItemViewModel templateVm }
            && DataContext is TaskOrchestrationWorkspaceViewModel vm)
        {
            vm.SelectTemplateCommand.Execute(templateVm);
        }
    }

    private void OnTemplateRowMoreClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Control btn) return;
        if (btn.Tag is not SidebarTaskGraphItemViewModel templateVm) return;
        ShowTemplateMenu(btn, templateVm);
    }

    private void ShowTemplateMenu(Control anchor, SidebarTaskGraphItemViewModel templateVm)
    {
        var vm = DataContext;
        if (vm is null) return;

        RowActionPanel.Children.Clear();

        void Add(string label, Func<Task> action)
        {
            var b = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "•", FontSize = 12 },
                        new TextBlock { Text = label, FontSize = 12 },
                    }
                },
                Classes = { "sidebar-menu-item" },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            b.Click += async (_, _) =>
            {
                RowActionPopup.IsOpen = false;
                try
                {
                    await action();
                }
                catch
                {
                    // Suppress exceptions from template actions to keep the UI stable.
                }
            };
            RowActionPanel.Children.Add(b);
        }

        Add("重命名", () => ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand<SidebarTaskGraphItemViewModel?>)vm.BeginRenameTemplateCommand).ExecuteAsync(templateVm));
        Add("复制", () => ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand<SidebarTaskGraphItemViewModel?>)vm.DuplicateTemplateCommand).ExecuteAsync(templateVm));

        if (!templateVm.IsBuiltIn)
        {
            Add("删除", () => ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand<SidebarTaskGraphItemViewModel?>)vm.DeleteTemplateCommand).ExecuteAsync(templateVm));
        }

        RowActionPopup.PlacementTarget = anchor;
        RowActionPopup.IsOpen = true;
    }

    private void OnTemplateRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        if (sender is TextBox { Tag: SidebarTaskGraphItemViewModel templateVm }
            && DataContext is TaskOrchestrationWorkspaceViewModel vm)
        {
            vm.CommitTemplateRenameCommand.Execute(templateVm);
        }
    }

    private void OnTemplateRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { Tag: SidebarTaskGraphItemViewModel templateVm }
            && DataContext is TaskOrchestrationWorkspaceViewModel vm)
        {
            vm.CommitTemplateRenameCommand.Execute(templateVm);
        }
    }

    private void OnTaskGraphRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SidebarTaskGraphItemViewModel itemVm }
            && DataContext is TaskOrchestrationWorkspaceViewModel vm)
        {
            if (itemVm.IsTemplate)
            {
                vm.SelectTemplateCommand.Execute(itemVm);
            }
            else
            {
                vm.SelectTaskGraphCommand.Execute(itemVm);
                vm.OpenSelectedTaskGraphInEditorCommand.Execute(null);
            }
        }
    }

    private void OnTaskGraphRowMoreClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Control btn) return;
        if (btn.Tag is not SidebarTaskGraphItemViewModel itemVm) return;
        if (itemVm.IsTemplate)
        {
            ShowTemplateMenu(btn, itemVm);
        }
        else
        {
            ShowTaskGraphMenu(btn, itemVm);
        }
    }

    private void ShowTaskGraphMenu(Control anchor, SidebarTaskGraphItemViewModel graphVm)
    {
        var vm = DataContext;
        if (vm is null) return;

        RowActionPanel.Children.Clear();

        void Add(string label, Action action)
        {
            var b = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "•", FontSize = 12 },
                        new TextBlock { Text = label, FontSize = 12 },
                    }
                },
                Classes = { "sidebar-menu-item" },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            b.Click += (_, _) =>
            {
                RowActionPopup.IsOpen = false;
                action();
            };
            RowActionPanel.Children.Add(b);
        }

        Add("重命名", () => vm.RenameTaskGraphCommand.Execute(graphVm));
        Add("修改所属项目", () => vm.ChangeTaskGraphProjectCommand.Execute(graphVm));
        Add("删除", () => vm.DeleteTaskGraphCommand.Execute(graphVm));

        RowActionPopup.PlacementTarget = anchor;
        RowActionPopup.IsOpen = true;
    }
}
