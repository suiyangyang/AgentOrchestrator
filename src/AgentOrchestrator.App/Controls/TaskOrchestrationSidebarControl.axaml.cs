using System;
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

    private void OnTemplateRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: TaskTemplateItemViewModel templateVm }
            && DataContext is TaskOrchestrationWorkspaceViewModel vm)
        {
            vm.SelectTemplateCommand.Execute(templateVm);
            vm.RequestTemplateFocus();
        }
    }

    private void OnTemplateRowMoreClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Control btn) return;
        if (btn.Tag is not TaskTemplateItemViewModel templateVm) return;
        ShowTemplateMenu(btn, templateVm);
    }

    private void ShowTemplateMenu(Control anchor, TaskTemplateItemViewModel templateVm)
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

        Add("重命名", () => vm.BeginRenameTemplateCommand.Execute(templateVm));
        Add("复制", () => vm.DuplicateTemplateCommand.Execute(templateVm));

        if (!templateVm.IsBuiltIn)
        {
            Add("删除", () => vm.DeleteTemplateCommand.Execute(templateVm));
        }

        RowActionPopup.PlacementTarget = anchor;
        RowActionPopup.IsOpen = true;
    }

    private void OnTemplateRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        if (sender is TextBox { Tag: TaskTemplateItemViewModel templateVm }
            && DataContext is TaskOrchestrationWorkspaceViewModel vm)
        {
            vm.CommitTemplateRenameCommand.Execute(templateVm);
        }
    }

    private void OnTemplateRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { Tag: TaskTemplateItemViewModel templateVm }
            && DataContext is TaskOrchestrationWorkspaceViewModel vm)
        {
            vm.CommitTemplateRenameCommand.Execute(templateVm);
        }
    }

    private void OnTaskGraphRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SidebarTaskGraphItemViewModel graphVm }
            && DataContext is TaskOrchestrationWorkspaceViewModel vm)
        {
            vm.SelectTaskGraphCommand.Execute(graphVm);
            vm.OpenSelectedTaskGraphInEditorCommand.Execute(null);
        }
    }

    private void OnTaskGraphRowMoreClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Control btn) return;
        if (btn.Tag is not SidebarTaskGraphItemViewModel graphVm) return;
        ShowTaskGraphMenu(btn, graphVm);
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
