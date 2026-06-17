using System;
using System.ComponentModel;
using AgentOrchestrator.App.Services;
using Avalonia;
using AgentOrchestrator.App.ViewModels;
using Avalonia.Controls;

namespace AgentOrchestrator.App.Controls;

public partial class MarkdownBlockControl : UserControl
{
    public static readonly StyledProperty<string?> MarkdownTextProperty =
        AvaloniaProperty.Register<MarkdownBlockControl, string?>(nameof(MarkdownText));

    private ChatBlockViewModel? _viewModel;

    public MarkdownBlockControl()
    {
        InitializeComponent();
    }

    public string? MarkdownText
    {
        get => GetValue(MarkdownTextProperty);
        set => SetValue(MarkdownTextProperty, value);
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MarkdownTextProperty)
        {
            RenderBody();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChatBlockViewModel.Text)
            or nameof(ChatBlockViewModel.Kind))
        {
            RenderBody();
        }
    }

    private void RenderBody()
    {
        if (MarkdownText is not null)
        {
            Body.Content = MarkdownRenderer.Render(MarkdownText);
        }
        else if (_viewModel is not null && _viewModel.IsText)
        {
            Body.Content = MarkdownRenderer.Render(_viewModel.Text ?? string.Empty);
        }
        else
        {
            Body.Content = null;
        }
    }
}
