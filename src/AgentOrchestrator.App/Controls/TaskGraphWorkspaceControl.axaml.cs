using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.ViewModels;

namespace AgentOrchestrator.App.Controls;

public partial class TaskGraphWorkspaceControl : UserControl
{
    private Border? _dragBorder;
    private TaskNode? _dragNode;
    private Point _dragOffset;
    private Border? _linkHandleBorder;
    private TaskNode? _linkTargetNode;

    public TaskGraphWorkspaceControl()
    {
        InitializeComponent();
    }

    private async void OnBrowseDocumentClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TaskGraphWorkspaceViewModel vm)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "选择任务文档",
            FileTypeFilter =
            [
                new FilePickerFileType("Task documents")
                {
                    Patterns = ["*.md", "*.txt"],
                },
            ],
        });

        if (files.Count == 0)
        {
            return;
        }

        var path = files[0].Path.LocalPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            await vm.SetDocumentFileAsync(path).ConfigureAwait(true);
        }
    }

    private void OnNodePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not TaskGraphWorkspaceViewModel vm
            || sender is not Border border
            || border.DataContext is not TaskNode node)
        {
            return;
        }

        _dragBorder = border;
        _dragNode = node;
        vm.SelectNodeCommand.Execute(node);
        var point = vm.NormalizeCanvasPoint(e.GetPosition(this));
        _dragOffset = new Point(point.X - node.Position.X, point.Y - node.Position.Y);
        e.Pointer.Capture(border);
        e.Handled = true;
    }

    private void OnNodePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragBorder is null
            || _dragNode is null
            || !ReferenceEquals(sender, _dragBorder)
            || !e.GetCurrentPoint(_dragBorder).Properties.IsLeftButtonPressed
            || DataContext is not TaskGraphWorkspaceViewModel vm)
        {
            return;
        }

        var point = vm.NormalizeCanvasPoint(e.GetPosition(this));
        vm.PreviewNodeMove(_dragNode, point.X - _dragOffset.X, point.Y - _dragOffset.Y);
        e.Handled = true;
    }

    private async void OnNodePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_linkHandleBorder is not null && DataContext is TaskGraphWorkspaceViewModel linkVm)
        {
            await linkVm.CompleteCanvasLinkAsync(_linkTargetNode).ConfigureAwait(true);
            _linkHandleBorder = null;
            _linkTargetNode = null;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_dragBorder is null || !ReferenceEquals(sender, _dragBorder) || DataContext is not TaskGraphWorkspaceViewModel vm)
        {
            return;
        }

        e.Pointer.Capture(null);
        await vm.CommitNodeMoveAsync().ConfigureAwait(true);
        _dragBorder = null;
        _dragNode = null;
        e.Handled = true;
    }

    private void OnLinkHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not TaskGraphWorkspaceViewModel vm
            || sender is not Border border
            || border.DataContext is not TaskNode node)
        {
            return;
        }

        _linkHandleBorder = border;
        _linkTargetNode = null;
        vm.BeginCanvasLink(node);
        e.Pointer.Capture(border);
        e.Handled = true;
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_linkHandleBorder is null || DataContext is not TaskGraphWorkspaceViewModel vm)
        {
            return;
        }

        var point = vm.NormalizeCanvasPoint(e.GetPosition(this));
        vm.UpdateCanvasLinkPreview(point.X, point.Y);
        e.Handled = true;
    }

    private async void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_linkHandleBorder is null || DataContext is not TaskGraphWorkspaceViewModel vm)
        {
            return;
        }

        e.Pointer.Capture(null);
        await vm.CompleteCanvasLinkAsync(_linkTargetNode).ConfigureAwait(true);
        _linkHandleBorder = null;
        _linkTargetNode = null;
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_linkHandleBorder is null)
        {
            return;
        }

        var source = e.Source as StyledElement;
        _linkTargetNode = null;
        while (source is not null)
        {
            if (source.DataContext is TaskNode node)
            {
                _linkTargetNode = node;
                break;
            }

            source = source.Parent as StyledElement;
        }
    }
}
