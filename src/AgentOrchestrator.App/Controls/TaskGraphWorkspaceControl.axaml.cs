using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.ViewModels;

namespace AgentOrchestrator.App.Controls;

public partial class TaskGraphWorkspaceControl : UserControl
{
    private const double NodeWidth = 240;
    private const double NodeHeight = 132;

    private Border? _dragBorder;
    private TaskNode? _dragNode;
    private Point _dragOffset;

    // Connection drag (node A's right-edge dot → node B → adds an edge).
    private Border? _linkHandleBorder;
    private TaskNode? _linkSourceNode;
    private TaskNode? _linkTargetNode;
    private Line? _linkPreviewLine;
    private Border? _linkPreviewTargetHighlight;

    // Pan-to-create (drag on empty canvas → drops a "Pending" node).
    private Point _pendingDragStart;
    private bool _pendingDragging;
    private TaskNode? _pendingDragNode;

    // Visual children synced manually with the ViewModel because Avalonia's
    // ItemsControl + Canvas ItemsPanel does not size / arrange its items
    // correctly in this scenario. We maintain a small lookup so we can
    // remove the right Border when a node is removed from the collection.
    private readonly Dictionary<string, Border> _nodeVisuals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Line> _edgeVisuals = new(StringComparer.Ordinal);
    private TaskGraphWorkspaceViewModel? _vm;

    public TaskGraphWorkspaceControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.GraphNodes.CollectionChanged -= OnGraphNodesChanged;
            _vm.GraphEdges.CollectionChanged -= OnGraphEdgesChanged;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = DataContext as TaskGraphWorkspaceViewModel;
        if (_vm is null)
        {
            return;
        }

        _vm.GraphNodes.CollectionChanged += OnGraphNodesChanged;
        _vm.GraphEdges.CollectionChanged += OnGraphEdgesChanged;
        _vm.PropertyChanged += OnVmPropertyChanged;

        RebuildGraphSurface();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TaskGraphWorkspaceViewModel.CurrentGraph)
            or nameof(TaskGraphWorkspaceViewModel.GraphZoom)
            or nameof(TaskGraphWorkspaceViewModel.SelectedNode))
        {
            RebuildGraphSurface();
        }
    }

    private void OnGraphNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncNodeVisuals();
    }

    private void OnGraphEdgesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncEdgeVisuals();
    }

    private void RebuildGraphSurface()
    {
        if (_vm is null || GraphCanvas is null)
        {
            return;
        }

        SyncNodeVisuals();
        SyncEdgeVisuals();

        if (GraphCanvas.Parent is not null)
        {
            // Reset scroll to (0, 0) whenever a fresh graph is loaded so users
            // see the top-left of the canvas, not whatever the last focused
            // node happened to be at.
            GraphCanvas.LayoutUpdated += OnFirstLayoutResetScroll;
        }
    }

    private void OnFirstLayoutResetScroll(object? sender, EventArgs e)
    {
        GraphCanvas.LayoutUpdated -= OnFirstLayoutResetScroll;
        GraphScrollViewer?.Offset = new Vector(0, 0);
    }

    // ============================================================
    // Node + edge visual sync
    // ============================================================

    private void SyncNodeVisuals()
    {
        if (_vm is null || GraphCanvas is null)
        {
            return;
        }

        var presentIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in _vm.GraphNodes)
        {
            presentIds.Add(node.Id);
            if (_nodeVisuals.TryGetValue(node.Id, out var existing))
            {
                UpdateNodeVisual(existing, node);
            }
            else
            {
                var border = BuildNodeVisual(node);
                _nodeVisuals[node.Id] = border;
                GraphCanvas.Children.Add(border);
            }
        }

        // Remove visuals for nodes that no longer exist.
        var toRemove = _nodeVisuals.Keys.Where(id => !presentIds.Contains(id)).ToList();
        foreach (var id in toRemove)
        {
            GraphCanvas.Children.Remove(_nodeVisuals[id]);
            _nodeVisuals.Remove(id);
        }
    }

    private Border BuildNodeVisual(TaskNode node)
    {
        var border = new Border
        {
            Classes = { "graph-node" },
            Width = NodeWidth,
            MinHeight = NodeHeight,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(12),
            DataContext = node,
            Tag = node,
        };
        UpdateNodeVisual(border, node);

        border.PointerPressed += OnNodePointerPressed;
        border.PointerMoved += OnNodePointerMoved;
        border.PointerReleased += OnNodePointerReleased;
        return border;
    }

    private void UpdateNodeVisual(Border border, TaskNode node)
    {
        Canvas.SetLeft(border, node.Position.X);
        Canvas.SetTop(border, node.Position.Y);
        border.Background = new SolidColorBrush(Color.Parse(node.NodeBackground));
        border.BorderBrush = new SolidColorBrush(Color.Parse(node.NodeBorderBrush));

        // Apply selected / pending style classes.
        SyncClass(border.Classes, "graph-node-selected", node.IsSelected);
        SyncClass(border.Classes, "graph-node-pending", node.IsPending);

        border.Child = BuildNodeContent(node);
    }

    private static void SyncClass(Avalonia.Controls.Classes classes, string name, bool present)
    {
        var has = classes.Contains(name);
        if (present && !has) classes.Add(name);
        else if (!present && has) classes.Remove(name);
    }

    private Control BuildNodeContent(TaskNode node)
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
            RowSpacing = 6,
        };

        // Title row.
        var titleRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 8,
        };
        titleRow.Children.Add(new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = new SolidColorBrush(Color.Parse("#D6DEEA")),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var titleText = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            [!TextBlock.TextProperty] = new Binding("Title"),
        };
        Grid.SetColumn(titleText, 1);
        titleRow.Children.Add(titleText);
        var statusText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#6E727A")),
            [!TextBlock.TextProperty] = new Binding("StatusText"),
        };
        Grid.SetColumn(statusText, 2);
        titleRow.Children.Add(statusText);
        Grid.SetRow(titleRow, 0);
        root.Children.Add(titleRow);

        // Kind row.
        var kindRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var kindText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#2459B8")),
            [!TextBlock.TextProperty] = new Binding("Kind"),
        };
        kindRow.Children.Add(kindText);
        var kindDot = new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = new SolidColorBrush(Color.Parse("#2459B8")),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(kindDot, 1);
        kindRow.Children.Add(kindDot);
        Grid.SetRow(kindRow, 1);
        root.Children.Add(kindRow);

        // Description.
        var desc = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#40444B")),
            MaxHeight = 48,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            [!TextBlock.TextProperty] = new Binding("Description"),
        };
        Grid.SetRow(desc, 2);
        root.Children.Add(desc);

        // Output summary.
        var output = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#6E727A")),
            MaxHeight = 36,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            [!TextBlock.TextProperty] = new Binding("OutputSummary"),
        };
        output.Bind(TextBlock.IsVisibleProperty, new Binding("OutputSummary")
        {
            Converter = new StringNotEmptyConverter(),
        });
        Grid.SetRow(output, 3);
        root.Children.Add(output);

        // Action row — connection point on the right + a detail button.
        var actions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };
        var linkHandle = new Border
        {
            Classes = { "node-connection-point" },
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        ToolTip.SetTip(linkHandle, "从此处拖动以连接其他节点");
        linkHandle.PointerPressed += OnLinkHandlePointerPressed;
        linkHandle.Tag = node;
        Grid.SetColumn(linkHandle, 0);
        actions.Children.Add(linkHandle);

        var detailBtn = new Button
        {
            Classes = { "task-toolbar-btn" },
            [!Button.CommandParameterProperty] = new Binding(),
        };
        ToolTip.SetTip(detailBtn, "打开执行详情");
        detailBtn.Bind(Button.CommandProperty, new Binding("DataContext.OpenNodeDetailCommand")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor)
            {
                AncestorType = typeof(UserControl),
            },
        });
        detailBtn.Bind(Button.IsEnabledProperty, new Binding("CanOpenDetail"));
        Grid.SetColumn(detailBtn, 1);
        actions.Children.Add(detailBtn);

        Grid.SetRow(actions, 4);
        root.Children.Add(actions);
        return root;
    }

    private void SyncEdgeVisuals()
    {
        if (_vm is null || GraphCanvas is null)
        {
            return;
        }

        // Edges first so they sit behind the nodes.
        var presentIds = new HashSet<string>(StringComparer.Ordinal);
        var selectedId = _vm.SelectedNode?.Id;
        foreach (var edge in _vm.GraphEdges)
        {
            var id = $"{edge.SourceId}:{edge.X1:F0},{edge.Y1:F0}->{edge.X2:F0},{edge.Y2:F0}";
            presentIds.Add(id);
            // Highlight if either endpoint is the selected node.
            var isHighlighted = selectedId is not null
                && (edge.SourceId == selectedId || edge.TargetId == selectedId);
            if (_edgeVisuals.TryGetValue(id, out var existing))
            {
                existing.StartPoint = edge.StartPoint;
                existing.EndPoint = edge.EndPoint;
                ApplyEdgeStyle(existing, isHighlighted);
            }
            else
            {
                var line = new Line
                {
                    StartPoint = edge.StartPoint,
                    EndPoint = edge.EndPoint,
                };
                ApplyEdgeStyle(line, isHighlighted);
                _edgeVisuals[id] = line;
                GraphCanvas.Children.Insert(0, line);
            }
        }

        var toRemove = _edgeVisuals.Keys.Where(id => !presentIds.Contains(id)).ToList();
        foreach (var id in toRemove)
        {
            GraphCanvas.Children.Remove(_edgeVisuals[id]);
            _edgeVisuals.Remove(id);
        }
    }

    private static void ApplyEdgeStyle(Line line, bool isHighlighted)
    {
        SyncClass(line.Classes, "graph-edge", !isHighlighted);
        SyncClass(line.Classes, "graph-edge-highlighted", isHighlighted);
    }

    // ============================================================
    // Document picker (for the create-from-document dialog body)
    // ============================================================

    private async void OnBrowseDocumentClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
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
            await _vm.SetDocumentFileAsync(path).ConfigureAwait(true);
        }
    }

    // ============================================================
    // Drag-to-move
    // ============================================================

    private void OnNodePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null
            || sender is not Border border
            || border.DataContext is not TaskNode node)
        {
            return;
        }

        // If we are in "pending" mode (user just dropped a pending node), treat
        // the first click as "promote to real" + select. Subsequent clicks drag.
        if (node.IsPending)
        {
            _vm.PromotePendingNode(node);
        }

        _dragBorder = border;
        _dragNode = node;
        _vm.SelectNodeCommand.Execute(node);
        var point = _vm.NormalizeCanvasPoint(e.GetPosition(this));
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
            || _vm is null)
        {
            return;
        }

        var point = _vm.NormalizeCanvasPoint(e.GetPosition(this));
        _vm.PreviewNodeMove(_dragNode, point.X - _dragOffset.X, point.Y - _dragOffset.Y);
        e.Handled = true;
    }

    private async void OnNodePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragBorder is null || !ReferenceEquals(sender, _dragBorder) || _vm is null)
        {
            return;
        }

        e.Pointer.Capture(null);
        await _vm.CommitNodeMoveAsync().ConfigureAwait(true);
        _dragBorder = null;
        _dragNode = null;
        e.Handled = true;
    }

    // ============================================================
    // Drag-to-link
    // ============================================================

    private void OnLinkHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null
            || sender is not Border border
            || border.Tag is not TaskNode node)
        {
            return;
        }

        _linkHandleBorder = border;
        _linkSourceNode = node;
        _linkTargetNode = null;
        e.Pointer.Capture(border);
        e.Handled = true;
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_vm is null || GraphCanvas is null)
        {
            return;
        }

        var point = _vm.NormalizeCanvasPoint(e.GetPosition(this));

        if (_linkHandleBorder is not null && _linkSourceNode is not null)
        {
            if (_linkPreviewLine is null)
            {
                _linkPreviewLine = new Line
                {
                    Classes = { "graph-link-preview" },
                };
                GraphCanvas.Children.Add(_linkPreviewLine);
            }
            var srcX = _linkSourceNode.Position.X + NodeWidth;
            var srcY = _linkSourceNode.Position.Y + (NodeHeight / 2);
            _linkPreviewLine.StartPoint = new Point(srcX, srcY);
            _linkPreviewLine.EndPoint = new Point(point.X, point.Y);

            _linkTargetNode = FindNodeAt(point);
            UpdateLinkTargetHighlight(_linkTargetNode);
            e.Handled = true;
            return;
        }

        if (_pendingDragging)
        {
            UpdatePendingDragPreview(point);
            e.Handled = true;
        }
    }

    private async void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_vm is null || GraphCanvas is null)
        {
            return;
        }

        if (_linkHandleBorder is not null)
        {
            e.Pointer.Capture(null);
            var canvasPoint = _vm.NormalizeCanvasPoint(e.GetPosition(this));
            var target = _linkTargetNode ?? FindNodeAt(canvasPoint);
            if (target is null && _linkSourceNode is not null)
            {
                target = await _vm.CreatePendingNodeAsync(canvasPoint.X, canvasPoint.Y).ConfigureAwait(true);
            }

            if (_linkSourceNode is not null && target is not null
                && !string.Equals(_linkSourceNode.Id, target.Id, StringComparison.Ordinal))
            {
                await _vm.ConnectNodesAsync(_linkSourceNode, target).ConfigureAwait(true);
            }
            ClearLinkPreviewState();
            e.Handled = true;
            return;
        }

        if (_pendingDragging)
        {
            var canvasPoint = _vm.NormalizeCanvasPoint(e.GetPosition(this));
            await _vm.FinalizePendingNodeAsync(_pendingDragNode, canvasPoint.X, canvasPoint.Y).ConfigureAwait(true);
            _pendingDragging = false;
            _pendingDragStart = default;
            _pendingDragNode = null;
            e.Handled = true;
        }
    }

    private void ClearLinkPreviewState()
    {
        if (_linkPreviewLine is not null && GraphCanvas is not null)
        {
            GraphCanvas.Children.Remove(_linkPreviewLine);
        }
        _linkPreviewLine = null;
        _linkHandleBorder = null;
        _linkSourceNode = null;
        _linkTargetNode = null;
        UpdateLinkTargetHighlight(null);
    }

    private void UpdateLinkTargetHighlight(TaskNode? node)
    {
        if (GraphCanvas is null)
        {
            return;
        }

        if (_linkPreviewTargetHighlight is not null)
        {
            GraphCanvas.Children.Remove(_linkPreviewTargetHighlight);
            _linkPreviewTargetHighlight = null;
        }

        if (node is not null && _nodeVisuals.TryGetValue(node.Id, out var nodeBorder))
        {
            _linkPreviewTargetHighlight = new Border
            {
                Classes = { "graph-link-preview-target" },
                Width = NodeWidth + 8,
                Height = NodeHeight + 8,
            };
            Canvas.SetLeft(_linkPreviewTargetHighlight, node.Position.X - 4);
            Canvas.SetTop(_linkPreviewTargetHighlight, node.Position.Y - 4);
            var idx = GraphCanvas.Children.IndexOf(nodeBorder);
            GraphCanvas.Children.Insert(idx + 1, _linkPreviewTargetHighlight);
        }
    }

    private TaskNode? FindNodeAt(Point canvasPoint)
    {
        if (_vm is null)
        {
            return null;
        }

        foreach (var node in _vm.GraphNodes)
        {
            if (canvasPoint.X >= node.Position.X
                && canvasPoint.X <= node.Position.X + NodeWidth
                && canvasPoint.Y >= node.Position.Y
                && canvasPoint.Y <= node.Position.Y + NodeHeight)
            {
                return node;
            }
        }
        return null;
    }

    // ============================================================
    // Click-on-empty-canvas to create a pending node
    // ============================================================

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (_vm is null || GraphCanvas is null)
        {
            return;
        }

        var props = e.GetCurrentPoint(this);
        if (!props.Properties.IsLeftButtonPressed)
        {
            return;
        }

        // If a node or link handle is handling the event, skip.
        var source = e.Source as StyledElement;
        var hitNode = false;
        while (source is not null)
        {
            if (source.DataContext is TaskNode)
            {
                hitNode = true;
                break;
            }
            source = source.Parent as StyledElement;
        }
        if (hitNode)
        {
            return;
        }

        var canvasPoint = _vm.NormalizeCanvasPoint(e.GetPosition(this));
        if (!IsInsideCanvas(canvasPoint))
        {
            return;
        }

        _pendingDragging = true;
        _pendingDragStart = canvasPoint;
        _vm.BeginPendingNodeDragAsync(canvasPoint.X, canvasPoint.Y).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully && t.Result is { } preview)
            {
                _pendingDragNode = preview;
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
        e.Pointer.Capture(GraphCanvas);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_pendingDragging || _vm is null || GraphCanvas is null)
        {
            return;
        }

        var point = _vm.NormalizeCanvasPoint(e.GetPosition(this));
        UpdatePendingDragPreview(point);
    }

    private void UpdatePendingDragPreview(Point canvasPoint)
    {
        if (!_pendingDragging || _pendingDragNode is null)
        {
            return;
        }

        if (!_nodeVisuals.TryGetValue(_pendingDragNode.Id, out var border))
        {
            return;
        }

        var start = _pendingDragStart;
        var left = Math.Min(start.X, canvasPoint.X);
        var top = Math.Min(start.Y, canvasPoint.Y);
        var right = Math.Max(start.X, canvasPoint.X);
        var bottom = Math.Max(start.Y, canvasPoint.Y);
        var width = Math.Max(NodeWidth, right - left);
        var height = Math.Max(NodeHeight, bottom - top);

        Canvas.SetLeft(border, left);
        Canvas.SetTop(border, top);
        border.Width = width;
        border.MinHeight = height;
        _pendingDragNode.Position = new NodePosition(left, top);
    }

    private bool IsInsideCanvas(Point canvasPoint)
    {
        if (GraphCanvas is null)
        {
            return false;
        }

        return canvasPoint.X >= 0
            && canvasPoint.Y >= 0
            && canvasPoint.X <= GraphCanvas.Width
            && canvasPoint.Y <= GraphCanvas.Height;
    }

    // ============================================================
    // "More" popup + creation dialog wiring
    // ============================================================

    private void OnMoreButtonClick(object? sender, RoutedEventArgs e)
    {
        if (MorePopup is null)
        {
            return;
        }
        MorePopup.IsOpen = !MorePopup.IsOpen;
    }

    private void OnCreateFromTemplateClick(object? sender, RoutedEventArgs e)
    {
        if (MorePopup is not null) MorePopup.IsOpen = false;
        _vm?.BeginCreateDialog(TaskGraphInputMode.Template);
    }

    private void OnCreateFromDirectClick(object? sender, RoutedEventArgs e)
    {
        if (MorePopup is not null) MorePopup.IsOpen = false;
        _vm?.BeginCreateDialog(TaskGraphInputMode.Direct);
    }

    private void OnCreateFromIntentClick(object? sender, RoutedEventArgs e)
    {
        if (MorePopup is not null) MorePopup.IsOpen = false;
        _vm?.BeginCreateDialog(TaskGraphInputMode.Intent);
    }

    private void OnCreateFromDocumentClick(object? sender, RoutedEventArgs e)
    {
        if (MorePopup is not null) MorePopup.IsOpen = false;
        _vm?.BeginCreateDialog(TaskGraphInputMode.Document);
    }

    private void OnCreateModeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton btn && btn.Tag is string tag && _vm is not null)
        {
            _vm.SelectInputModeByName(tag);
        }
    }

    private void OnCreateDialogClose(object? sender, RoutedEventArgs e)
    {
        _vm?.CancelCreateDialog();
    }

    private void OnCreateDialogBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source == sender)
        {
            _vm?.CancelCreateDialog();
        }
    }

    private async void OnCreateDialogConfirm(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        await _vm.ConfirmCreateDialogAsync().ConfigureAwait(true);
    }
}

/// <summary>Visible when the bound string is non-null and non-empty.</summary>
internal sealed class StringNotEmptyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string s && !string.IsNullOrEmpty(s);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
