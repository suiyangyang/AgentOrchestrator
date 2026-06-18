using System;
using System.ComponentModel;
using AgentOrchestrator.App.Models.Sidebar;
using AgentOrchestrator.App.Services;
using AgentOrchestrator.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;

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
            var thoughtContent = MarkdownRenderer.RenderInline(_viewModel.Text ?? string.Empty);
            Body.Content = new ScrollViewer
            {
                MaxHeight = 300,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = thoughtContent
            };
        }
        else if ((_viewModel.IsTool || _viewModel.IsTask) && _viewModel.ToolQuestion is not null)
        {
            Body.Content = BuildQuestionBody(_viewModel.ToolQuestion);
        }
        else if (_viewModel.IsTool || _viewModel.IsTask)
        {
            Body.Content = BuildToolBody(_viewModel);
        }
        else
        {
            Body.Content = null;
        }
    }

    private static Control BuildToolBody(ChatBlockViewModel viewModel)
    {
        var display = viewModel.ToolDisplay;
        if (!string.IsNullOrWhiteSpace(display.CodeText))
        {
            return new ReadOnlyCodeBlock
            {
                Text = display.CodeText,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
        }

        return MarkdownRenderer.RenderInline(viewModel.ToolOutput ?? string.Empty);
    }

    private Control BuildQuestionBody(RemoteQuestion question)
    {
        var layout = new StackPanel { Spacing = 10 };

        foreach (var item in question.Questions)
        {
            layout.Children.Add(new TextBlock { Text = item.Header, Classes = { "question-item-header" } });
            if (!string.IsNullOrWhiteSpace(item.Question))
            {
                layout.Children.Add(new TextBlock { Text = item.Question, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "question-item-question" } });
            }

            foreach (var option in item.Options)
            {
                var text = string.IsNullOrWhiteSpace(option.Description)
                    ? option.Label
                    : $"{option.Label} - {option.Description}";
                layout.Children.Add(new TextBlock { Text = $"• {text}", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "question-option-description" } });
            }

            if (item.Custom)
            {
                layout.Children.Add(new TextBlock { Text = "• 自定义答案", Classes = { "question-option-description" } });
            }
        }

        var button = new Button
        {
            Content = "继续回答",
            Classes = { "question-confirm-button" },
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };
        button.Click += OnContinueQuestionClick;
        button.Tag = question;
        layout.Children.Add(button);

        return layout;
    }

    private void OnContinueQuestionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RemoteQuestion question })
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.DataContext is MainWindowViewModel mainWindowViewModel)
        {
            mainWindowViewModel.Chat.RestorePendingQuestion(question);
        }
    }
}
