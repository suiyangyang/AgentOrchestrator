using System;
using AgentOrchestrator.App.Services;
using AgentOrchestrator.App.ViewModels;
using Avalonia.Controls;

namespace AgentOrchestrator.App.Controls;

public partial class MarkdownBlockControl : UserControl
{
    public MarkdownBlockControl()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ChatBlockViewModel vm && vm.IsText)
        {
            Body.Content = MarkdownRenderer.Render(vm.Text ?? string.Empty);
        }
        else
        {
            Body.Content = null;
        }
    }
}
