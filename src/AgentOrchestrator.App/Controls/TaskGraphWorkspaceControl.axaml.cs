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
    private Avalonia.Controls.Shapes.Ellipse? _linkHandleEllipse;
    private TaskNode? _linkSourceNode;
    private TaskNode? _linkTargetNode;
    private Path? _linkPreviewPath;
    private Polygon? _linkPreviewArrow;
    private Border? _linkPreviewTargetHighlight;

    // Pan (drag on empty canvas → scrolls the underlying ScrollViewer).
    // We track the pointer + scroll offset at pan-start and apply the
    // pointer delta to the offset on every move so the canvas content
    // "follows" the cursor.
    private bool _isPanning;
    private Point _panStartPointer;
    private Vector _panStartScrollOffset;

    // Visual children synced manually with the ViewModel because Avalonia's
    // ItemsControl + Canvas ItemsPanel does not size / arrange its items
    // correctly in this scenario. We maintain a small lookup so we can
    // remove the right Border when a node is removed from the collection.
    private readonly Dictionary<string, Border> _nodeVisuals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EdgeVisual> _edgeVisuals = new(StringComparer.Ordinal);
    private TaskGraphWorkspaceViewModel? _vm;

    /// <summary>
    /// Per-edge visual bundle: the cubic Bezier curve (Path), the
    /// direction-indicating arrowhead (Polygon), and the mid-edge hit-test
    /// dot (Ellipse). All three share a single Z-band so they can be
    /// removed/inserted together and stay aligned when endpoints move.
    /// </summary>
    private sealed class EdgeVisual
    {
        public Path Path { get; init; } = null!;
        public Polygon Arrow { get; init; } = null!;
        public Ellipse Mid { get; init; } = null!;
    }

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
            _vm.SurfaceRebuilt -= OnVmSurfaceRebuilt;
        }

        _vm = DataContext as TaskGraphWorkspaceViewModel;
        if (_vm is null)
        {
            return;
        }

        _vm.GraphNodes.CollectionChanged += OnGraphNodesChanged;
        _vm.GraphEdges.CollectionChanged += OnGraphEdgesChanged;
        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.SurfaceRebuilt += OnVmSurfaceRebuilt;

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
        // While the VM is bulk-replacing GraphNodes (Clear + N Adds inside
        // RefreshGraphSurface), suppress the per-item sync. Without this
        // guard, an N-node graph triggers N+1 calls to SyncNodeVisuals,
        // each itself O(N), giving an O(N²) rebuild that hangs the UI on
        // graphs with 30+ nodes — the symptom is "clicking a node on
        // the canvas (or a task graph card in the sidebar) freezes the
        // window for several seconds". OnVmSurfaceRebuilt performs the
        // single O(N) sync once the bulk replace is done.
        if (_vm?.IsBulkRefreshingSurface == true)
        {
            return;
        }

        SyncNodeVisuals();
    }

    private void OnGraphEdgesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // See OnGraphNodesChanged for the rationale — same O(N²) trap
        // applies to edges.
        if (_vm?.IsBulkRefreshingSurface == true)
        {
            return;
        }

        SyncEdgeVisuals();
    }

    /// <summary>
    /// Fires once after the VM finishes a bulk replace of
    /// <c>GraphNodes</c> / <c>GraphEdges</c> (see
    /// <see cref="TaskGraphWorkspaceViewModel.RefreshGraphSurface"/>).
    /// Re-sync visuals from the final collection state in a single pass
    /// instead of letting <c>CollectionChanged</c> trigger one O(N) sync
    /// per added item.
    /// </summary>
    private void OnVmSurfaceRebuilt(object? sender, EventArgs e)
    {
        SyncNodeVisuals();
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

        // Action row — left INPUT port + detail button + right OUTPUT port.
        // Layout: [Auto port-in][* spacer][Auto detail-btn][Auto port-out]
        // The right port is the drag source for new edges (matches the
        // existing OnLinkHandlePointerPressed flow). The left port is the
        // matching visual sink so each card shows both endpoints of its
        // data-flow contract — the requirement driving this layout is
        // "卡片两侧需要各有一个点来连接曲线; 左侧的点代表输入, 右侧的点代表输出"
        // from the 2026-06-20 fix doc.
        var actions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
        };

        var portKindClass = $"port-port-{KindToCssClass(node.Kind)}";

        var inputPort = new Ellipse
        {
            Classes = { "node-connection-point", portKindClass },
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        ToolTip.SetTip(inputPort, $"输入端口 · {TaskNodePortStyle.For(node.Kind).Label}");
        inputPort.Tag = node;
        Grid.SetColumn(inputPort, 0);
        actions.Children.Add(inputPort);

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
        Grid.SetColumn(detailBtn, 2);
        actions.Children.Add(detailBtn);

        var outputPort = new Ellipse
        {
            Classes = { "node-connection-point", portKindClass },
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        ToolTip.SetTip(outputPort, "输出端口 — 从此处拖动以连接其他节点");
        outputPort.PointerPressed += OnLinkHandlePointerPressed;
        outputPort.Tag = node;
        Grid.SetColumn(outputPort, 3);
        actions.Children.Add(outputPort);

        Grid.SetRow(actions, 4);
        root.Children.Add(actions);
        return root;
    }

    /// <summary>
    /// Map a <see cref="TaskNodeKind"/> to the lowercase CSS-class
    /// suffix used by the per-kind port styles in <c>App.axaml</c>
    /// (e.g. <c>port-port-execute</c> for <see cref="TaskNodeKind.Execute"/>).
    /// </summary>
    private static string KindToCssClass(TaskNodeKind kind) => kind switch
    {
        TaskNodeKind.Plan => "plan",
        TaskNodeKind.Execute => "execute",
        TaskNodeKind.Verify => "verify",
        TaskNodeKind.Decision => "decision",
        TaskNodeKind.Parallel => "parallel",
        TaskNodeKind.HumanInput => "humaninput",
        _ => "execute",
    };

    private void SyncEdgeVisuals()
    {
        if (_vm is null || GraphCanvas is null)
        {
            return;
        }

        // Edges first so they sit behind the nodes. Keyed by
        // SourceId -> TargetId so a node drag (which mutates X1/Y1 each
        // mouse-move) updates the existing visual in place instead of
        // tearing down and recreating three children per frame.
        var presentIds = new HashSet<string>(StringComparer.Ordinal);
        var selectedId = _vm.SelectedNode?.Id;
        foreach (var edge in _vm.GraphEdges)
        {
            var id = BuildEdgeKey(edge);
            presentIds.Add(id);

            // Highlight if either endpoint is the selected node.
            var isHighlighted = selectedId is not null
                && (edge.SourceId == selectedId || edge.TargetId == selectedId);

            if (_edgeVisuals.TryGetValue(id, out var existing))
            {
                UpdateEdgeVisual(existing, edge, isHighlighted);
            }
            else
            {
                var visual = CreateEdgeVisual(edge, isHighlighted);
                _edgeVisuals[id] = visual;
                // Insert order: Path (back) → Arrow (mid) → Mid dot (front).
                GraphCanvas.Children.Insert(0, visual.Path);
                GraphCanvas.Children.Insert(1, visual.Arrow);
                GraphCanvas.Children.Insert(2, visual.Mid);
            }
        }

        var toRemove = _edgeVisuals.Keys.Where(id => !presentIds.Contains(id)).ToList();
        foreach (var id in toRemove)
        {
            if (_edgeVisuals.TryGetValue(id, out var visual))
            {
                GraphCanvas.Children.Remove(visual.Path);
                GraphCanvas.Children.Remove(visual.Arrow);
                GraphCanvas.Children.Remove(visual.Mid);
            }
            _edgeVisuals.Remove(id);
        }
    }

    /// <summary>Stable key for an edge across drag-induced endpoint updates.</summary>
    private static string BuildEdgeKey(TaskGraphEdgeViewModel e)
        => $"{e.SourceId}->{e.TargetId}";

    /// <summary>
    /// Build the three visuals (Bezier Path, arrowhead Polygon, midpoint
    /// Ellipse) for an edge. The stroke / fill colors come from the source
    /// node's <see cref="Models.TaskGraph.TaskNodePortStyle"/> palette so
    /// the line color matches the output port color.
    /// </summary>
    private EdgeVisual CreateEdgeVisual(
        TaskGraphEdgeViewModel edge,
        bool isHighlighted)
    {
        var path = new Path
        {
            Classes = { "graph-edge" },
            Data = BuildBezierGeometry(edge.StartPoint, edge.EndPoint),
        };
        var arrow = new Polygon
        {
            Classes = { "graph-edge-arrow" },
            Points = BuildArrowheadPoints(edge.EndPoint, edge.StartPoint, ArrowSize),
        };
        var mid = new Ellipse
        {
            Classes = { "graph-edge-midpoint" },
            Width = MidpointSize,
            Height = MidpointSize,
        };
        var visual = new EdgeVisual { Path = path, Arrow = arrow, Mid = mid };
        ApplyEdgeStyle(visual, edge, isHighlighted);
        return visual;
    }

    /// <summary>
    /// Re-target the existing visuals at the edge's new endpoints. Called
    /// on every drag-induced <c>RefreshGraphSurface</c> pass — must be
    /// allocation-free beyond what <c>Path.Data</c> already requires.
    /// </summary>
    private void UpdateEdgeVisual(
        EdgeVisual visual,
        TaskGraphEdgeViewModel edge,
        bool isHighlighted)
    {
        visual.Path.Data = BuildBezierGeometry(edge.StartPoint, edge.EndPoint);
        visual.Arrow.Points = BuildArrowheadPoints(edge.EndPoint, edge.StartPoint, ArrowSize);

        // Mid-edge hit-test dot at the Bezier midpoint. Must use the
        // 4-arg EvaluateBezier overload — the 3-arg one falls back to
        // BezierEndFallback = (0, 0) as the endpoint, which puts the
        // dot near the canvas origin instead of on the line. Symptom
        // was: the mid-edge dot appeared to "track" the card-center
        // interpolation rather than the actual line geometry.
        var controls = ComputeBezierControlPoints(edge.StartPoint, edge.EndPoint);
        var mid = EvaluateBezier(edge.StartPoint, controls, edge.EndPoint, 0.5);
        Canvas.SetLeft(visual.Mid, mid.X - MidpointSize / 2);
        Canvas.SetTop(visual.Mid, mid.Y - MidpointSize / 2);

        ApplyEdgeStyle(visual, edge, isHighlighted);
    }

    private static void ApplyEdgeStyle(
        EdgeVisual visual,
        TaskGraphEdgeViewModel edge,
        bool isHighlighted)
    {
        SyncClass(visual.Path.Classes, "graph-edge", !isHighlighted);
        SyncClass(visual.Path.Classes, "graph-edge-highlighted", isHighlighted);
        SyncClass(visual.Arrow.Classes, "graph-edge-arrow-highlighted", isHighlighted);

        // Stroke / fill are set programmatically (the styles in App.axaml
        // only carry the thickness / dash defaults). Source-kind color
        // flows from TaskNodePortStyle on the edge view model. Highlight
        // uses the same accent the toolbar selection border uses.
        var strokeBrush = isHighlighted
            ? new SolidColorBrush(Color.Parse("#FF6A00"))
            : new SolidColorBrush(Color.Parse(edge.SourcePortColors.FillHex));
        visual.Path.Stroke = strokeBrush;
        visual.Arrow.Fill = strokeBrush;
        visual.Mid.Fill = strokeBrush;
    }

    // ── Bezier geometry helpers ─────────────────────────────────────

    private const double ArrowSize = 9.0;
    private const double MidpointSize = 9.0;
    private const double BezierHandleScale = 0.55; // ComfyUI-style horizontal handles

    /// <summary>
    /// Compute the two control points for a cubic Bezier with horizontal
    /// tangents at both endpoints (matches ComfyUI / Reaflow look). The
    /// handle length scales with the horizontal distance between endpoints
    /// but is clamped so even short edges get a visible S-curve.
    /// </summary>
    private static (Point c1, Point c2) ComputeBezierControlPoints(Point start, Point end)
    {
        var dx = Math.Abs(end.X - start.X);
        var handle = Math.Max(40.0, dx * BezierHandleScale);
        var c1 = new Point(start.X + handle, start.Y);
        var c2 = new Point(end.X - handle, end.Y);
        return (c1, c2);
    }

    /// <summary>
    /// Build a <see cref="PathGeometry"/> containing a single cubic Bezier
    /// from <paramref name="start"/> to <paramref name="end"/> with
    /// horizontal-tangent control points.
    /// </summary>
    private static PathGeometry BuildBezierGeometry(Point start, Point end)
    {
        var (c1, c2) = ComputeBezierControlPoints(start, end);
        var bezier = new BezierSegment { Point1 = c1, Point2 = c2, Point3 = end };
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments!.Add(bezier);
        var geometry = new PathGeometry();
        geometry.Figures!.Add(figure);
        return geometry;
    }

    /// <summary>
    /// Evaluate the cubic Bezier at parameter t in [0, 1].
    /// Formula: B(t) = (1−t)³P0 + 3(1−t)²t·P1 + 3(1−t)t²·P2 + t³P3.
    /// </summary>
    private static Point EvaluateBezier(Point start, (Point c1, Point c2) controls, Point end, double t)
    {
        var u = 1.0 - t;
        var tt = t * t;
        var uu = u * u;
        var uuu = uu * u;
        var ttt = tt * t;
        return new Point(
            uuu * start.X + 3 * uu * t * controls.c1.X + 3 * u * tt * controls.c2.X + ttt * end.X,
            uuu * start.Y + 3 * uu * t * controls.c1.Y + 3 * u * tt * controls.c2.Y + ttt * end.Y);
    }

    private static Point EvaluateBezier(Point start, (Point c1, Point c2) controls, double t)
        => EvaluateBezier(start, controls, BezierEndFallback, t);

    // Sentinel "end" used only when callers want a midpoint (t < 1) and
    // don't have the real end point handy. Not used in the current code
    // but kept so future callers can hit a midpoint without computing
    // c1/c2 twice.
    private static readonly Point BezierEndFallback = new(0, 0);

    /// <summary>
    /// Build a triangular arrowhead at <paramref name="tip"/> pointing
    /// away from <paramref name="from"/>. The triangle is symmetric about
    /// the (tip→from) axis with width <paramref name="size"/>. Returned
    /// as a <see cref="Points"/> collection suitable for
    /// <see cref="Polygon.Points"/>.
    /// </summary>
    private static List<Point> BuildArrowheadPoints(Point tip, Point from, double size)
    {
        var dx = tip.X - from.X;
        var dy = tip.Y - from.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.0001)
        {
            // Degenerate (zero-length) edge — collapse to a tiny triangle
            // around the tip so we still render something rather than
            // throwing.
            return new List<Point>
            {
                new(tip.X - size / 2, tip.Y - size / 2),
                new(tip.X + size / 2, tip.Y - size / 2),
                tip,
            };
        }

        // Unit vector tip → from (i.e. backward along the curve).
        var ux = -dx / len;
        var uy = -dy / len;
        // Perpendicular for the base width.
        var px = -uy;
        var py = ux;

        var baseX = tip.X + ux * size;
        var baseY = tip.Y + uy * size;
        var halfW = size * 0.55;
        return new List<Point>
        {
            tip,
            new(baseX + px * halfW, baseY + py * halfW),
            new(baseX - px * halfW, baseY - py * halfW),
        };
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

        _dragBorder = border;
        _dragNode = node;
        _vm.SelectNodeCommand.Execute(node);
        var point = e.GetPosition(GraphCanvas);
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

        var point = e.GetPosition(GraphCanvas);
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
            || sender is not Avalonia.Controls.Shapes.Ellipse ellipse
            || ellipse.Tag is not TaskNode node)
        {
            return;
        }

        _linkHandleEllipse = ellipse;
        _linkSourceNode = node;
        _linkTargetNode = null;
        e.Pointer.Capture(ellipse);
        e.Handled = true;
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_vm is null || GraphCanvas is null)
        {
            return;
        }

        // Pan takes priority over the link-drag preview — once the user
        // starts panning, no link drag can be in flight (the link handle
        // would have captured the pointer first).
        if (_isPanning)
        {
            ApplyPan(e);
            return;
        }

        // e.GetPosition(GraphCanvas) returns canvas-local pre-RenderTransform
        // coords — the same coord system the edges/nodes use, so no extra
        // scaling or origin offset is needed.
        var point = e.GetPosition(GraphCanvas);

        if (_linkHandleEllipse is not null && _linkSourceNode is not null)
        {
            EnsureLinkPreviewVisuals();
            var srcX = _linkSourceNode.Position.X + NodeWidth;
            // Anchor the preview's start point on the OUTPUT port's Y
            // (PortAnchorOffsetY from TaskNodePortStyle) so the line lands
            // exactly on the port dot, not the card's geometric center.
            var srcY = _linkSourceNode.Position.Y + TaskNodePortStyle.PortAnchorOffsetY;
            var srcPoint = new Point(srcX, srcY);
            _linkPreviewPath!.Data = BuildBezierGeometry(srcPoint, point);
            _linkPreviewArrow!.Points = BuildArrowheadPoints(point, srcPoint, ArrowSize);
            // Preview stroke / fill inherit the source port color so the
            // user can predict what the committed edge will look like.
            var previewBrush = new SolidColorBrush(Color.Parse(
                TaskNodePortStyle.For(_linkSourceNode.Kind).FillHex));
            _linkPreviewPath.Stroke = previewBrush;
            _linkPreviewArrow.Fill = previewBrush;

            _linkTargetNode = FindNodeAt(point);
            UpdateLinkTargetHighlight(_linkTargetNode);
            e.Handled = true;
            return;
        }
    }

    /// <summary>
    /// Lazily create the Bezier <see cref="Path"/> + arrowhead
    /// <see cref="Polygon"/> used as the link-drag preview. Inserted
    /// behind the nodes (index 0/1) so the preview line never occludes
    /// the cards.
    /// </summary>
    private void EnsureLinkPreviewVisuals()
    {
        if (GraphCanvas is null)
        {
            return;
        }

        if (_linkPreviewPath is null)
        {
            _linkPreviewPath = new Path
            {
                Classes = { "graph-link-preview" },
            };
            GraphCanvas.Children.Insert(0, _linkPreviewPath);
        }

        if (_linkPreviewArrow is null)
        {
            _linkPreviewArrow = new Polygon
            {
                Classes = { "graph-edge-arrow" },
            };
            GraphCanvas.Children.Insert(1, _linkPreviewArrow);
        }
    }

    private async void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_vm is null || GraphCanvas is null)
        {
            return;
        }

        if (_isPanning)
        {
            e.Pointer.Capture(null);
            _isPanning = false;
            e.Handled = true;
            return;
        }

        if (_linkHandleEllipse is not null)
        {
            e.Pointer.Capture(null);
            var canvasPoint = e.GetPosition(GraphCanvas);
            var target = _linkTargetNode ?? FindNodeAt(canvasPoint);
            if (target is null && _linkSourceNode is not null)
            {
                // Drop on empty canvas: create a real (non-pending) node at
                // the drop point and use it as the link target.
                target = await _vm.CreateNodeAtAsync(canvasPoint.X, canvasPoint.Y).ConfigureAwait(true);
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
    }

    private void ClearLinkPreviewState()
    {
        if (_linkPreviewPath is not null && GraphCanvas is not null)
        {
            GraphCanvas.Children.Remove(_linkPreviewPath);
        }
        if (_linkPreviewArrow is not null && GraphCanvas is not null)
        {
            GraphCanvas.Children.Remove(_linkPreviewArrow);
        }
        _linkPreviewPath = null;
        _linkPreviewArrow = null;
        _linkHandleEllipse = null;
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
    // Pan on empty canvas (drag → scroll)
    // ============================================================

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (_vm is null || GraphCanvas is null || GraphScrollViewer is null)
        {
            return;
        }

        var props = e.GetCurrentPoint(this);
        if (!props.Properties.IsLeftButtonPressed)
        {
            return;
        }

        // Skip if a node or link handle is handling the event — those
        // start their own gestures (node-drag or link-drag) and the
        // pointer is captured to the originating child, so this method
        // typically wouldn't fire for those clicks anyway. The hit-test
        // is a belt-and-suspenders check for clicks that bubble up
        // through the canvas's empty children.
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

        // Skip if a link drag is already in progress (the link handle
        // already captured the pointer).
        if (_linkHandleEllipse is not null)
        {
            return;
        }

        var canvasPoint = e.GetPosition(GraphCanvas);
        if (!IsInsideCanvas(canvasPoint))
        {
            return;
        }

        // Start panning. The pointer + scroll offset are recorded here;
        // OnCanvasPointerMoved applies the delta and OnCanvasPointerReleased
        // ends the gesture.
        _isPanning = true;
        _panStartPointer = e.GetPosition(GraphScrollViewer);
        _panStartScrollOffset = GraphScrollViewer.Offset;
        e.Pointer.Capture(GraphCanvas);
        e.Handled = true;
    }

    /// <summary>
    /// Apply the pointer delta (from <c>_panStartPointer</c>) to the
    /// ScrollViewer's offset, clamping to the scrollable range. Called
    /// every mouse-move while <c>_isPanning</c> is set so the canvas
    /// content follows the cursor.
    /// </summary>
    private void ApplyPan(PointerEventArgs e)
    {
        if (GraphScrollViewer is null)
        {
            return;
        }

        var currentPointer = e.GetPosition(GraphScrollViewer);
        var delta = currentPointer - _panStartPointer;
        // Content "follows" the pointer — dragging right by N moves the
        // visible window left by N (which is what makes the content
        // appear to slide right with the cursor).
        var newX = _panStartScrollOffset.X - delta.X;
        var newY = _panStartScrollOffset.Y - delta.Y;
        // Clamp to the scrollable range. Avalonia's ScrollViewer exposes
        // this as Extent - Viewport (ScrollableWidth/Height are
        // platform-availability-dependent, so compute it directly).
        var maxX = Math.Max(0, GraphScrollViewer.Extent.Width - GraphScrollViewer.Viewport.Width);
        var maxY = Math.Max(0, GraphScrollViewer.Extent.Height - GraphScrollViewer.Viewport.Height);
        GraphScrollViewer.Offset = new Vector(
            Math.Clamp(newX, 0, maxX),
            Math.Clamp(newY, 0, maxY));
        e.Handled = true;
    }

    // ============================================================
    // Wheel zoom (Ctrl + scroll → zoom in / out)
    // ============================================================

    /// <summary>
    /// Wheel handler on the canvas. When Ctrl is held, each wheel notch
    /// zooms the graph by one step via the VM's <c>ZoomInCommand</c> /
    /// <c>ZoomOutCommand</c>. Without Ctrl, the event is left unhandled
    /// so the ScrollViewer's default scroll behavior takes over.
    /// </summary>
    private void OnCanvasPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (e.Delta.Y > 0)
        {
            _vm.ZoomInCommand.Execute(null);
        }
        else if (e.Delta.Y < 0)
        {
            _vm.ZoomOutCommand.Execute(null);
        }

        e.Handled = true;
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

    /// <summary>
    /// Toolbar "delete node" button click handler. Forwards to the same
    /// RelayCommand the old <c>Command="..."</c> binding drove, so the
    /// delete flow + confirmation dialog stays in one place.
    /// </summary>
    private void OnDeleteNodeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TaskGraphWorkspaceViewModel vm)
        {
            vm.DeleteSelectedNodeCommand.Execute(null);
        }
    }

    /// <summary>
    /// Edge midpoint popup "delete edge" click handler. Stub: the real
    /// wiring lives in the control's <c>OnEdgeMidpointPointerPressed</c>
    /// path which drives the VM directly. Forwarding here too would race
    /// on the selected edge, so the click handler is intentionally a
    /// no-op (the popup is dismissed by the caller after the VM action).
    /// </summary>
    private void OnDeleteEdgeMenuItemClick(object? sender, RoutedEventArgs e)
    {
        // Intentionally no-op.
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
