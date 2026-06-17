using System;
using System.ComponentModel;
using AgentOrchestrator.App.Services;
using AgentOrchestrator.App.ViewModels;
using Avalonia.Controls;

namespace AgentOrchestrator.App.Controls;

public partial class CollapsibleBlockControl : UserControl
{
    private ChatBlockViewModel? _viewModel;

    public CollapsibleBlockControl()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as ChatBlockViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        RenderBody();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChatBlockViewModel.Text)
            or nameof(ChatBlockViewModel.ToolOutput)
            or nameof(ChatBlockViewModel.Kind))
        {
            RenderBody();
        }
    }

    private void RenderBody()
    {
        if (_viewModel is null)
        {
            Body.Content = null;
            return;
        }

        if (_viewModel.IsThought)
        {
            Body.Content = MarkdownRenderer.RenderInline(_viewModel.Text ?? string.Empty);
        }
        else if (_viewModel.IsTool || _viewModel.IsTask)
        {
            Body.Content = MarkdownRenderer.RenderInline(_viewModel.ToolOutput ?? string.Empty);
        }
        else
        {
            Body.Content = null;
        }
    }
}
