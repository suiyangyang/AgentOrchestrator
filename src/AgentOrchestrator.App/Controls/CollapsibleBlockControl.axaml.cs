using System;
using AgentOrchestrator.App.Services;
using AgentOrchestrator.App.ViewModels;
using Avalonia.Controls;

namespace AgentOrchestrator.App.Controls;

public partial class CollapsibleBlockControl : UserControl
{
    public CollapsibleBlockControl()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ChatBlockViewModel vm)
        {
            if (vm.IsThought)
            {
                Body.Content = MarkdownRenderer.RenderInline(vm.Text ?? string.Empty);
            }
            else if (vm.IsTool)
            {
                Body.Content = MarkdownRenderer.RenderInline(vm.ToolOutput ?? string.Empty);
            }
            else
            {
                Body.Content = null;
            }
        }
        else
        {
            Body.Content = null;
        }
    }
}
