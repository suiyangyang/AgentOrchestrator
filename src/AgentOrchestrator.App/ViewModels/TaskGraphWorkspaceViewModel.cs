using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.Chat;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.TaskGraph;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentOrchestrator.App.ViewModels;

public sealed partial class TaskGraphWorkspaceViewModel : ViewModelBase
{
    private const double NodeWidth = 240;
    private const double NodeHeight = 132;
    private const double CanvasPadding = 80;

    private readonly ITaskGraphStore _store;
    private readonly ITaskGraphDirectParser _directParser;
    private readonly ITaskGraphPlanner _planner;
    private readonly IDocumentReader _documentReader;
    private readonly ITaskGraphExecutor _executor;
    private readonly SidebarViewModel _sidebar;
    private TaskGraph? _currentGraph;

    public TaskGraphWorkspaceViewModel(
        ITaskGraphStore store,
        ITaskGraphDirectParser directParser,
        ITaskGraphPlanner planner,
        IDocumentReader documentReader,
        ITaskGraphExecutor executor,
        SidebarViewModel sidebar)
    {
        _store = store;
        _directParser = directParser;
        _planner = planner;
        _documentReader = documentReader;
        _executor = executor;
        _sidebar = sidebar;

        SelectedPermission = Permissions[2];
        SelectedTemplate = Templates[0];
        _ = InitializeAsync();
    }

    public string Title => "任务编排";

    public string HeaderTitle => CurrentGraph is null
        ? Title
        : $"{Title} · {CurrentGraph.Name}";

    public ObservableCollection<TaskNode> GraphNodes { get; } = [];

    public ObservableCollection<TaskGraphEdgeViewModel> GraphEdges { get; } = [];

    public ObservableCollection<TaskGraphNodeReferenceViewModel> LinkableTargetNodes { get; } = [];

    public ObservableCollection<BugReportRowViewModel> BugReportRows { get; } = [];

    public ObservableCollection<PermissionOption> Permissions { get; } =
    [
        new("ask", "请求批准", "编辑外部文件和使用互联网时始终询问", "✋"),
        new("replace", "替代批准", "仅对检测到的风险操作请求批准", "◔"),
        new("full", "完全访问权限", "可不受限制地访问互联网和您电脑上的任何文件", "🛡")
    ];

    public ObservableCollection<TaskGraphTemplateOptionViewModel> Templates { get; } =
    [
        new("task-list", "任务列表", "适合 1-20 这种长链任务，按顺序逐项执行。"),
        new("feature-dev", "复杂功能开发", "先生成方案，等待确认，再动态注入开发计划并执行。"),
        new("bug-list", "Bug 列表", "逐个分析 bug，自动区分可修复项与待补充项，最后输出报告。"),
    ];

    public IReadOnlyList<string> Models { get; } = ["codex", "gpt-5", "claude-compatible"];

    [ObservableProperty]
    private PermissionOption _selectedPermission;

    [ObservableProperty]
    private string _selectedModel = "codex";

    [ObservableProperty]
    private TaskGraphTemplateOptionViewModel _selectedTemplate;

    [ObservableProperty]
    private string _templateInputText = "- 任务 1\n- 任务 2\n- 任务 3";

    [ObservableProperty]
    private string _templateGraphName = string.Empty;

    [ObservableProperty]
    private string _directInputText = "- 需求分析\n- 实现功能\n  depends on: 需求分析\n- 测试验证\n  depends on: 实现功能";

    [ObservableProperty]
    private string _intentInputText = string.Empty;

    [ObservableProperty]
    private string? _documentFilePath;

    [ObservableProperty]
    private string _documentPreviewText = string.Empty;

    [ObservableProperty]
    private string _statusText = "准备就绪";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private TaskNode? _selectedNode;

    [ObservableProperty]
    private TaskGraphInputMode _selectedInputMode = TaskGraphInputMode.Template;

    [ObservableProperty]
    private string _userConfirmationText = string.Empty;

    [ObservableProperty]
    private double _graphCanvasWidth = 1200;

    [ObservableProperty]
    private double _graphCanvasHeight = 720;

    [ObservableProperty]
    private bool _isLinkMode;

    [ObservableProperty]
    private TaskGraphNodeReferenceViewModel? _selectedLinkTarget;

    [ObservableProperty]
    private bool _isCanvasLinking;

    [ObservableProperty]
    private TaskNode? _linkSourceNode;

    [ObservableProperty]
    private double _linkPreviewX1;

    [ObservableProperty]
    private double _linkPreviewY1;

    [ObservableProperty]
    private double _linkPreviewX2;

    [ObservableProperty]
    private double _linkPreviewY2;

    [ObservableProperty]
    private string _bugReportMarkdown = string.Empty;

    [ObservableProperty]
    private string _bugReportJson = string.Empty;

    [ObservableProperty]
    private double _graphZoom = 1.0;

    [ObservableProperty]
    private string _newNodeTitle = "新任务";

    [ObservableProperty]
    private string _newNodeDescription = string.Empty;

    [ObservableProperty]
    private TaskNodeKind _newNodeKind = TaskNodeKind.Execute;

    [ObservableProperty]
    private string _selectedNodeTitleDraft = string.Empty;

    [ObservableProperty]
    private string _selectedNodeDescriptionDraft = string.Empty;

    [ObservableProperty]
    private bool _isGraphMaximized;

    [ObservableProperty]
    private bool _isCreateDialogVisible;

    public event EventHandler<TaskGraphNodeDetailRequest>? NodeDetailRequested;

    public IReadOnlyList<TaskNodeKind> NodeKinds { get; } =
    [
        TaskNodeKind.Plan,
        TaskNodeKind.Execute,
        TaskNodeKind.Verify,
        TaskNodeKind.Decision,
        TaskNodeKind.Parallel,
        TaskNodeKind.HumanInput,
    ];

    public TaskGraph? CurrentGraph
    {
        get => _currentGraph;
        private set
        {
            if (ReferenceEquals(_currentGraph, value))
            {
                return;
            }

            DetachGraphSubscriptions(_currentGraph);
            _currentGraph = value;
            DetachSelection();
            AttachGraphSubscriptions(_currentGraph);
            OnPropertyChanged();
            OnPropertyChanged(nameof(HeaderTitle));
            OnPropertyChanged(nameof(HasCurrentGraph));
            OnPropertyChanged(nameof(HasNoCurrentGraph));
            OnPropertyChanged(nameof(CurrentGraphName));
            OnPropertyChanged(nameof(CurrentGraphExecutionText));
            OnPropertyChanged(nameof(CurrentGraphSummaryText));
            OnPropertyChanged(nameof(CurrentWorkingDirectory));
            OnPropertyChanged(nameof(CanExecute));
            OnPropertyChanged(nameof(CanRetryFailed));
            OnPropertyChanged(nameof(CanCancelExecution));
            OnPropertyChanged(nameof(CanContinue));
            OnPropertyChanged(nameof(SelectedTemplateDisplayText));
            RefreshGraphSurface();
        }
    }

    public bool HasCurrentGraph => CurrentGraph is not null;

    public bool HasNoCurrentGraph => CurrentGraph is null;

    public string CurrentGraphName => CurrentGraph?.Name ?? "未命名编排";

    public string CurrentGraphExecutionText => CurrentGraph?.ExecutionState switch
    {
        TaskGraphExecutionState.Draft => "草稿",
        TaskGraphExecutionState.Running => "执行中",
        TaskGraphExecutionState.WaitingForInput => "等待确认",
        TaskGraphExecutionState.Completed => "已完成",
        TaskGraphExecutionState.Failed => "失败",
        TaskGraphExecutionState.Cancelled => "已取消",
        _ => "草稿",
    };

    public string CurrentGraphSummaryText => CurrentGraph is null
        ? "尚未生成编排"
        : $"{CurrentGraph.Nodes.Count} 个节点 · {CurrentGraph.Nodes.Count(x => x.Status == TaskNodeStatus.Completed)} 已完成 · {CurrentGraph.Nodes.Count(x => x.Status == TaskNodeStatus.Failed)} 失败";

    public string CurrentWorkingDirectory
        => _sidebar.CurrentWorkingDirectory
           ?? ProjectsTracker.CurrentWorkingDirectory
           ?? Environment.CurrentDirectory;

    public bool CanExecute => CurrentGraph is not null
        && CurrentGraph.Nodes.Count > 0
        && !IsBusy
        && CurrentGraph.ExecutionState is not TaskGraphExecutionState.Running and not TaskGraphExecutionState.WaitingForInput;

    public bool CanRetryFailed => CurrentGraph is not null
        && CurrentGraph.Nodes.Any(x => x.Status == TaskNodeStatus.Failed)
        && !IsBusy;

    public bool CanCancelExecution => CurrentGraph is not null
        && CurrentGraph.ExecutionState == TaskGraphExecutionState.Running;

    public bool CanContinue => CurrentGraph is not null
        && CurrentGraph.ExecutionState == TaskGraphExecutionState.WaitingForInput
        && !IsBusy;

    public bool IsTemplateMode => SelectedInputMode == TaskGraphInputMode.Template;

    public bool IsDirectMode => SelectedInputMode == TaskGraphInputMode.Direct;

    public bool IsIntentMode => SelectedInputMode == TaskGraphInputMode.Intent;

    public bool IsDocumentMode => SelectedInputMode == TaskGraphInputMode.Document;

    public bool IsTemplateModeSelected => IsTemplateMode;

    public bool IsDirectModeSelected => IsDirectMode;

    public bool IsIntentModeSelected => IsIntentMode;

    public bool IsDocumentModeSelected => IsDocumentMode;

    public bool HasSelectedNode => SelectedNode is not null;

    public bool HasNoSelectedNode => SelectedNode is null;

    public bool SelectedNodeHasFiles => SelectedNode is not null && SelectedNode.TouchedFiles.Count > 0;

    public bool SelectedNodeHasTags => SelectedNode is not null && SelectedNode.ResultTags.Count > 0;

    public string LinkModeButtonText => IsLinkMode ? "取消连线" : "添加连线";

    public bool CanStartLink => SelectedNode is not null && CurrentGraph is not null;

    public bool CanCreateLink => SelectedNode is not null
        && SelectedLinkTarget is not null
        && CurrentGraph is not null
        && !string.Equals(SelectedNode.Id, SelectedLinkTarget.Id, StringComparison.Ordinal);

    public bool CanRemoveSelectedNodeLinks => SelectedNode is not null
        && CurrentGraph is not null
        && (SelectedNode.DependsOn.Count > 0 || CurrentGraph.Nodes.Any(x => x.DependsOn.Contains(SelectedNode.Id)));

    public bool IsWaitingForUserConfirmation => CurrentGraph?.ExecutionState == TaskGraphExecutionState.WaitingForInput;

    public bool HasBugReport => BugReportRows.Count > 0;

    public bool CanAddNode => CurrentGraph is not null && !IsBusy;

    public bool CanDeleteNode => SelectedNode is not null && CurrentGraph is not null && !IsBusy;

    public bool IsGraphHeaderVisible => !IsGraphMaximized;

    public bool IsGraphSidebarVisible => !IsGraphMaximized;

    public bool IsGraphConfigurationVisible => !IsGraphMaximized;

    public Point LinkPreviewStartPoint => new(LinkPreviewX1, LinkPreviewY1);

    public Point LinkPreviewEndPoint => new(LinkPreviewX2, LinkPreviewY2);

    public string ZoomText => $"{GraphZoom:P0}";

    public string GraphMaximizeButtonText => IsGraphMaximized ? "退出铺满" : "铺满窗口";

    public Thickness WorkspaceMargin => IsGraphMaximized
        ? new Thickness(0)
        : new Thickness(20, 16, 20, 20);

    public CornerRadius GraphSurfaceCornerRadius => IsGraphMaximized
        ? new CornerRadius(0)
        : new CornerRadius(8);

    public Thickness GraphSurfaceBorderThickness => IsGraphMaximized
        ? new Thickness(0)
        : new Thickness(1);

    public string SelectedTemplateDisplayText => CurrentGraph?.TemplateKind switch
    {
        TaskGraphTemplateKind.TaskList => "任务列表",
        TaskGraphTemplateKind.FeatureDevelopment => "复杂功能开发",
        TaskGraphTemplateKind.BugList => "Bug 列表",
        _ => "自定义",
    };

    private async Task InitializeAsync()
    {
        await _sidebar.RefreshTaskGraphsAsync().ConfigureAwait(true);
    }

    public async Task SetDocumentFileAsync(string filePath, CancellationToken ct = default)
    {
        DocumentFilePath = filePath;
        DocumentPreviewText = await _documentReader.ReadAsync(filePath, ct).ConfigureAwait(true);
        SelectedInputMode = TaskGraphInputMode.Document;
    }

    // ============================================================
    // Create dialog (popup) flow
    // ============================================================

    public void BeginCreateDialog(TaskGraphInputMode mode)
    {
        SelectedInputMode = mode;
        IsCreateDialogVisible = true;
    }

    public void CancelCreateDialog()
    {
        IsCreateDialogVisible = false;
    }

    public async Task ConfirmCreateDialogAsync()
    {
        var mode = SelectedInputMode;
        IsCreateDialogVisible = false;
        try
        {
            switch (mode)
            {
                case TaskGraphInputMode.Template:
                    await CreateTemplateGraphAsync().ConfigureAwait(true);
                    break;
                case TaskGraphInputMode.Direct:
                    await GenerateFromDirectAsync().ConfigureAwait(true);
                    break;
                case TaskGraphInputMode.Intent:
                    await GenerateFromIntentAsync().ConfigureAwait(true);
                    break;
                case TaskGraphInputMode.Document:
                    await GenerateFromDocumentAsync().ConfigureAwait(true);
                    break;
            }
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    public void SelectInputModeByName(string name)
    {
        SelectedInputMode = name switch
        {
            "Template" => TaskGraphInputMode.Template,
            "Direct" => TaskGraphInputMode.Direct,
            "Intent" => TaskGraphInputMode.Intent,
            "Document" => TaskGraphInputMode.Document,
            _ => SelectedInputMode,
        };
    }

    // ============================================================
    // Pending node flow (drag on empty canvas to spawn a node)
    // ============================================================

    /// <summary>
    /// Creates a new "pending" node at the given canvas position. The node
    /// lives in the graph but is not selectable as a real one until the user
    /// promotes it (by clicking). Used by the drag-on-empty-canvas gesture.
    /// </summary>
    public Task<TaskNode?> BeginPendingNodeDragAsync(double x, double y)
    {
        return CreatePendingNodeInternalAsync(x, y, transient: true);
    }

    /// <summary>
    /// Creates a real (non-pending) node at the given canvas position. Used
    /// when the user releases a connection-drag on empty canvas — the
    /// resulting node becomes the link target.
    /// </summary>
    public Task<TaskNode?> CreatePendingNodeAsync(double x, double y)
    {
        return CreatePendingNodeInternalAsync(x, y, transient: false);
    }

    private async Task<TaskNode?> CreatePendingNodeInternalAsync(double x, double y, bool transient)
    {
        if (CurrentGraph is null)
        {
            // No graph yet — auto-create a blank one so the user has a target.
            var blank = new TaskGraph
            {
                Name = "未命名编排",
            };
            blank.Nodes.Add(MakePendingNode(x, y, transient));
            await ActivateGraphAsync(blank, "已创建新编排。").ConfigureAwait(true);
            return CurrentGraph?.Nodes.FirstOrDefault();
        }

        var node = MakePendingNode(x, y, transient);
        CurrentGraph.Nodes.Add(node);
        CurrentGraph.RebuildEdges();
        await _store.SaveAsync(CurrentGraph).ConfigureAwait(true);
        RefreshGraphSurface();
        return node;
    }

    private static TaskNode MakePendingNode(double x, double y, bool transient)
    {
        return new TaskNode
        {
            Id = $"pending_{Guid.NewGuid():N}",
            Title = "新节点",
            Description = "点击此处直接编辑节点内容。",
            Kind = TaskNodeKind.Execute,
            Status = TaskNodeStatus.Pending,
            Position = new NodePosition(Math.Max(20, x), Math.Max(20, y)),
            IsPending = transient,
        };
    }

    public void PromotePendingNode(TaskNode node)
    {
        if (!node.IsPending)
        {
            return;
        }

        node.IsPending = false;
        // Reassign to a "manual_N" id so it doesn't keep the temporary
        // "pending_*" namespace — but only if the user hasn't edited it.
        if (CurrentGraph is not null)
        {
            node.Id = BuildNextNodeId(CurrentGraph);
            _ = _store.SaveAsync(CurrentGraph);
        }
        StatusText = $"已提升节点 “{node.Title}”。在右侧栏或图上直接编辑其内容。";
    }

    public async Task FinalizePendingNodeAsync(TaskNode? node, double x, double y)
    {
        if (node is null || CurrentGraph is null)
        {
            return;
        }

        // Update position to where the user dragged to.
        node.Position = new NodePosition(Math.Max(20, x), Math.Max(20, y));
        await _store.SaveAsync(CurrentGraph).ConfigureAwait(true);
        RefreshGraphSurface();
    }

    public async Task ConnectNodesAsync(TaskNode source, TaskNode target)
    {
        if (CurrentGraph is null || ReferenceEquals(source, target))
        {
            return;
        }

        if (source.DependsOn.Contains(target.Id))
        {
            StatusText = "该依赖已存在。";
            return;
        }

        source.DependsOn.Add(target.Id);
        CurrentGraph.RebuildEdges();
        await _store.SaveAsync(CurrentGraph).ConfigureAwait(true);
        RefreshGraphSurface();
        StatusText = $"已为节点 “{source.Title}” 添加依赖 “{target.Title}”。";
    }

    [RelayCommand]
    private async Task NewGraphAsync()
    {
        CurrentGraph = null;
        SelectedNode = null;
        UserConfirmationText = string.Empty;
        StatusText = "已清空当前编排。";
        await _sidebar.RefreshTaskGraphsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CreateTemplateGraphAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            var graph = SelectedTemplate.Key switch
            {
                "task-list" => TaskGraphTemplateBuilder.BuildTaskListGraph(TemplateInputText),
                "feature-dev" => TaskGraphTemplateBuilder.BuildFeatureDevelopmentGraph(TemplateInputText),
                "bug-list" => TaskGraphTemplateBuilder.BuildBugListGraph(TemplateInputText),
                _ => throw new TaskGraphValidationException("未知模板类型。"),
            };

            if (!string.IsNullOrWhiteSpace(TemplateGraphName))
            {
                graph.Name = TemplateGraphName.Trim();
            }

            await ActivateGraphAsync(graph, $"已根据“{SelectedTemplate.Title}”模板创建任务图。").ConfigureAwait(true);
            SelectedInputMode = TaskGraphInputMode.Template;
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task GenerateFromDirectAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            var graph = _directParser.Parse(DirectInputText);
            await ActivateGraphAsync(graph, "已根据直接输入生成编排。").ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task GenerateFromIntentAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            var graph = await _planner.CreateFromIntentAsync(
                IntentInputText,
                CurrentWorkingDirectory,
                SelectedPermission.Key,
                SelectedModel).ConfigureAwait(true);
            await ActivateGraphAsync(graph, "已根据智能编排生成任务图。").ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task GenerateFromDocumentAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(DocumentFilePath))
            {
                throw new TaskGraphValidationException("请先选择要编排的文档。");
            }

            if (string.IsNullOrWhiteSpace(DocumentPreviewText))
            {
                DocumentPreviewText = await _documentReader.ReadAsync(DocumentFilePath).ConfigureAwait(true);
            }

            var graph = await _planner.CreateFromDocumentAsync(
                DocumentFilePath,
                DocumentPreviewText,
                CurrentWorkingDirectory,
                SelectedPermission.Key,
                SelectedModel).ConfigureAwait(true);
            await ActivateGraphAsync(graph, "已根据文档生成任务图。").ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SaveCurrentAsync()
    {
        if (CurrentGraph is null)
        {
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            await _store.SaveAsync(CurrentGraph).ConfigureAwait(true);
            await _sidebar.RefreshTaskGraphsAsync().ConfigureAwait(true);
            StatusText = "编排已保存。";
        }).ConfigureAwait(true);
    }

    public async Task OpenGraphByIdAsync(string id)
    {
        var graph = await _store.LoadAsync(id).ConfigureAwait(true);
        if (graph is null)
        {
            return;
        }

        await _executor.ReconcileAsync(graph).ConfigureAwait(true);
        CurrentGraph = graph;
        SelectNode(graph.Nodes.FirstOrDefault());
        StatusText = $"已打开编排“{CurrentGraph.Name}”。";
    }

    [RelayCommand]
    private async Task ExecuteCurrentAsync()
    {
        if (CurrentGraph is null)
        {
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            await _executor.ExecuteAsync(
                CurrentGraph,
                new TaskGraphExecutionRequest(CurrentWorkingDirectory, SelectedPermission.Key, SelectedModel)).ConfigureAwait(true);
            StatusText = $"编排执行结束: {CurrentGraphExecutionText}";
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ContinueExecutionAsync()
    {
        if (CurrentGraph is null)
        {
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            ApplyUserConfirmation();
            await _executor.ContinueAsync(
                CurrentGraph,
                new TaskGraphExecutionRequest(CurrentWorkingDirectory, SelectedPermission.Key, SelectedModel)).ConfigureAwait(true);
            StatusText = $"编排继续执行结束: {CurrentGraphExecutionText}";
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RetryFailedAsync()
    {
        if (CurrentGraph is null)
        {
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            await _executor.RetryFailedAsync(
                CurrentGraph,
                new TaskGraphExecutionRequest(CurrentWorkingDirectory, SelectedPermission.Key, SelectedModel)).ConfigureAwait(true);
            StatusText = $"失败节点重试结束: {CurrentGraphExecutionText}";
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private void CancelExecution()
    {
        if (CurrentGraph is null)
        {
            return;
        }

        _executor.RequestCancel(CurrentGraph.Id);
        StatusText = "已请求取消执行。当前运行中的节点会自然结束，其余节点将被跳过。";
    }

    [RelayCommand]
    private void SelectNode(TaskNode? node)
    {
        if (CurrentGraph is null)
        {
            return;
        }

        foreach (var current in CurrentGraph.Nodes)
        {
            current.IsSelected = ReferenceEquals(current, node);
        }

        SelectedNode = node;
        SelectedNodeTitleDraft = node?.Title ?? string.Empty;
        SelectedNodeDescriptionDraft = node?.Description ?? string.Empty;
        RefreshLinkableTargets();
        OnPropertyChanged(nameof(HasSelectedNode));
        OnPropertyChanged(nameof(SelectedNodeHasFiles));
        OnPropertyChanged(nameof(SelectedNodeHasTags));
        OnPropertyChanged(nameof(CanStartLink));
        OnPropertyChanged(nameof(CanCreateLink));
        OnPropertyChanged(nameof(CanRemoveSelectedNodeLinks));
        OnPropertyChanged(nameof(CanDeleteNode));
    }

    [RelayCommand]
    private void ToggleLinkMode()
    {
        IsLinkMode = !IsLinkMode;
        if (!IsLinkMode)
        {
            SelectedLinkTarget = null;
        }
    }

    [RelayCommand]
    private async Task CreateLinkAsync()
    {
        if (CurrentGraph is null || SelectedNode is null || SelectedLinkTarget is null)
        {
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            if (SelectedNode.DependsOn.Contains(SelectedLinkTarget.Id))
            {
                throw new TaskGraphValidationException("当前节点已经依赖该目标节点。");
            }

            SelectedNode.DependsOn.Add(SelectedLinkTarget.Id);
            TaskGraphTopology.TopologicalSort(CurrentGraph);
            await _store.SaveAsync(CurrentGraph).ConfigureAwait(true);
            RefreshGraphSurface();
            StatusText = $"已为节点“{SelectedNode.Title}”添加前置依赖。";
            IsLinkMode = false;
            SelectedLinkTarget = null;
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RemoveSelectedNodeLinksAsync()
    {
        if (CurrentGraph is null || SelectedNode is null)
        {
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            SelectedNode.DependsOn.Clear();
            foreach (var node in CurrentGraph.Nodes.Where(x => x.DependsOn.Contains(SelectedNode.Id)).ToList())
            {
                node.DependsOn.Remove(SelectedNode.Id);
            }

            CurrentGraph.RebuildEdges();
            await _store.SaveAsync(CurrentGraph).ConfigureAwait(true);
            RefreshGraphSurface();
            StatusText = $"已移除节点“{SelectedNode.Title}”的所有连线。";
        }).ConfigureAwait(true);
    }

    public void PreviewNodeMove(TaskNode node, double x, double y)
    {
        if (CurrentGraph is null)
        {
            return;
        }

        var boundedX = Math.Max(20, x);
        var boundedY = Math.Max(20, y);
        node.Position = new NodePosition(boundedX, boundedY);
        RefreshGraphSurface();
    }

    public async Task CommitNodeMoveAsync()
    {
        if (CurrentGraph is null)
        {
            return;
        }

        await _store.SaveAsync(CurrentGraph).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task AddNodeAsync()
    {
        if (CurrentGraph is null)
        {
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            var node = new TaskNode
            {
                Id = BuildNextNodeId(CurrentGraph),
                Title = string.IsNullOrWhiteSpace(NewNodeTitle) ? "新任务" : NewNodeTitle.Trim(),
                Description = NewNodeDescription?.Trim() ?? string.Empty,
                Kind = NewNodeKind,
                Prompt = TaskGraphFactory.BuildPrompt(NewNodeTitle, NewNodeDescription, NewNodeKind),
                Position = BuildNextNodePosition(CurrentGraph),
                Status = TaskNodeStatus.Pending,
            };

            CurrentGraph.Nodes.Add(node);
            CurrentGraph.RebuildEdges();
            await _store.SaveAsync(CurrentGraph).ConfigureAwait(true);
            SelectNode(node);
            StatusText = $"已新增节点“{node.Title}”。";
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteSelectedNodeAsync()
    {
        if (CurrentGraph is null || SelectedNode is null)
        {
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            var nodeId = SelectedNode.Id;
            var title = SelectedNode.Title;
            foreach (var node in CurrentGraph.Nodes.Where(x => x.DependsOn.Contains(nodeId)).ToList())
            {
                node.DependsOn.Remove(nodeId);
            }

            CurrentGraph.Nodes.Remove(SelectedNode);
            CurrentGraph.RebuildEdges();
            await _store.SaveAsync(CurrentGraph).ConfigureAwait(true);
            SelectNode(CurrentGraph.Nodes.FirstOrDefault());
            StatusText = $"已删除节点“{title}”。";
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SaveSelectedNodeEditsAsync()
    {
        if (CurrentGraph is null || SelectedNode is null)
        {
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            SelectedNode.Title = string.IsNullOrWhiteSpace(SelectedNodeTitleDraft)
                ? SelectedNode.Title
                : SelectedNodeTitleDraft.Trim();
            SelectedNode.Description = SelectedNodeDescriptionDraft?.Trim() ?? string.Empty;
            SelectedNode.Prompt = TaskGraphFactory.BuildPrompt(SelectedNode.Title, SelectedNode.Description, SelectedNode.Kind);
            await _store.SaveAsync(CurrentGraph).ConfigureAwait(true);
            RefreshGraphSurface();
            StatusText = $"已保存节点“{SelectedNode.Title}”的编辑内容。";
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private void ZoomIn()
    {
        GraphZoom = Math.Min(1.8, GraphZoom + 0.1);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        GraphZoom = Math.Max(0.5, GraphZoom - 0.1);
    }

    [RelayCommand]
    private void ResetZoom()
    {
        GraphZoom = 1.0;
    }

    [RelayCommand]
    private void ToggleGraphMaximize()
    {
        IsGraphMaximized = !IsGraphMaximized;
    }

    [RelayCommand]
    private async Task AutoLayoutAsync()
    {
        if (CurrentGraph is null)
        {
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            TaskGraphFactory.ApplyLayeredPositions(CurrentGraph);
            await _store.SaveAsync(CurrentGraph).ConfigureAwait(true);
            RefreshGraphSurface();
            StatusText = "已自动整理当前图布局。";
        }).ConfigureAwait(true);
    }

    public void BeginCanvasLink(TaskNode sourceNode)
    {
        LinkSourceNode = sourceNode;
        IsCanvasLinking = true;
        LinkPreviewX1 = sourceNode.Position.X + NodeWidth;
        LinkPreviewY1 = sourceNode.Position.Y + (NodeHeight / 2);
        LinkPreviewX2 = LinkPreviewX1;
        LinkPreviewY2 = LinkPreviewY1;
    }

    public void UpdateCanvasLinkPreview(double x, double y)
    {
        if (!IsCanvasLinking)
        {
            return;
        }

        LinkPreviewX2 = x;
        LinkPreviewY2 = y;
        OnPropertyChanged(nameof(LinkPreviewEndPoint));
    }

    public async Task CompleteCanvasLinkAsync(TaskNode? targetNode)
    {
        if (CurrentGraph is null || LinkSourceNode is null)
        {
            CancelCanvasLink();
            return;
        }

        try
        {
            if (targetNode is null || string.Equals(targetNode.Id, LinkSourceNode.Id, StringComparison.Ordinal))
            {
                return;
            }

            if (LinkSourceNode.DependsOn.Contains(targetNode.Id))
            {
                StatusText = "该依赖已存在。";
                return;
            }

            LinkSourceNode.DependsOn.Add(targetNode.Id);
            TaskGraphTopology.TopologicalSort(CurrentGraph);
            await _store.SaveAsync(CurrentGraph).ConfigureAwait(true);
            RefreshGraphSurface();
            StatusText = $"已通过画布为节点“{LinkSourceNode.Title}”添加依赖。";
        }
        catch (Exception ex)
        {
            if (targetNode is not null)
            {
                LinkSourceNode.DependsOn.Remove(targetNode.Id);
            }

            StatusText = ex.Message;
        }
        finally
        {
            CancelCanvasLink();
        }
    }

    public void CancelCanvasLink()
    {
        IsCanvasLinking = false;
        LinkSourceNode = null;
        OnPropertyChanged(nameof(LinkPreviewStartPoint));
        OnPropertyChanged(nameof(LinkPreviewEndPoint));
    }

    public Point NormalizeCanvasPoint(Point point)
    {
        if (GraphZoom <= 0)
        {
            return point;
        }

        return new Point(point.X / GraphZoom, point.Y / GraphZoom);
    }

    [RelayCommand]
    private void OpenNodeDetail(TaskNode? node)
    {
        if (CurrentGraph is null || node is null || string.IsNullOrWhiteSpace(node.AgentSessionId))
        {
            return;
        }

        NodeDetailRequested?.Invoke(this, new TaskGraphNodeDetailRequest(CurrentGraph.Id, node, CurrentWorkingDirectory));
    }

    [RelayCommand]
    private void SelectMode(TaskGraphInputMode mode)
    {
        SelectedInputMode = mode;
    }

    partial void OnSelectedNodeChanged(TaskNode? value)
    {
        OnPropertyChanged(nameof(HasSelectedNode));
        OnPropertyChanged(nameof(HasNoSelectedNode));
        OnPropertyChanged(nameof(SelectedNodeHasFiles));
        OnPropertyChanged(nameof(SelectedNodeHasTags));
        OnPropertyChanged(nameof(CanStartLink));
        OnPropertyChanged(nameof(CanCreateLink));
        OnPropertyChanged(nameof(CanRemoveSelectedNodeLinks));
        OnPropertyChanged(nameof(CanDeleteNode));
    }

    partial void OnSelectedLinkTargetChanged(TaskGraphNodeReferenceViewModel? value)
    {
        OnPropertyChanged(nameof(CanCreateLink));
    }

    partial void OnIsLinkModeChanged(bool value)
    {
        if (value)
        {
            RefreshLinkableTargets();
        }

        OnPropertyChanged(nameof(LinkModeButtonText));
        OnPropertyChanged(nameof(CanCreateLink));
    }

    partial void OnLinkPreviewX1Changed(double value)
        => OnPropertyChanged(nameof(LinkPreviewStartPoint));

    partial void OnLinkPreviewY1Changed(double value)
        => OnPropertyChanged(nameof(LinkPreviewStartPoint));

    partial void OnLinkPreviewX2Changed(double value)
        => OnPropertyChanged(nameof(LinkPreviewEndPoint));

    partial void OnLinkPreviewY2Changed(double value)
        => OnPropertyChanged(nameof(LinkPreviewEndPoint));

    partial void OnGraphZoomChanged(double value)
        => OnPropertyChanged(nameof(ZoomText));

    partial void OnIsGraphMaximizedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsGraphHeaderVisible));
        OnPropertyChanged(nameof(IsGraphSidebarVisible));
        OnPropertyChanged(nameof(IsGraphConfigurationVisible));
        OnPropertyChanged(nameof(GraphMaximizeButtonText));
        OnPropertyChanged(nameof(WorkspaceMargin));
        OnPropertyChanged(nameof(GraphSurfaceCornerRadius));
        OnPropertyChanged(nameof(GraphSurfaceBorderThickness));
    }

    partial void OnSelectedInputModeChanged(TaskGraphInputMode value)
    {
        OnPropertyChanged(nameof(IsTemplateMode));
        OnPropertyChanged(nameof(IsDirectMode));
        OnPropertyChanged(nameof(IsIntentMode));
        OnPropertyChanged(nameof(IsDocumentMode));
        OnPropertyChanged(nameof(IsTemplateModeSelected));
        OnPropertyChanged(nameof(IsDirectModeSelected));
        OnPropertyChanged(nameof(IsIntentModeSelected));
        OnPropertyChanged(nameof(IsDocumentModeSelected));
    }

    partial void OnIsCreateDialogVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCreateDialogVisible));
    }

    private async Task ActivateGraphAsync(TaskGraph graph, string statusMessage)
    {
        CurrentGraph = graph;
        SelectNode(graph.Nodes.FirstOrDefault());
        RefreshBugReport();
        await _store.SaveAsync(graph).ConfigureAwait(true);
        await _sidebar.RefreshTaskGraphsAsync().ConfigureAwait(true);
        StatusText = statusMessage;
    }

    private async Task ExecuteBusyAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await action().ConfigureAwait(true);
        }
        catch (PlannerParseException ex)
        {
            StatusText = ex.Message;
            DirectInputText = ex.RawText;
            SelectedInputMode = TaskGraphInputMode.Direct;
        }
        catch (TaskGraphValidationException ex)
        {
            StatusText = ex.Message;
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanExecute));
            OnPropertyChanged(nameof(CanRetryFailed));
            OnPropertyChanged(nameof(CanCancelExecution));
            OnPropertyChanged(nameof(CanContinue));
            OnPropertyChanged(nameof(IsWaitingForUserConfirmation));
            OnPropertyChanged(nameof(CanAddNode));
            OnPropertyChanged(nameof(CanDeleteNode));
        }
    }

    private void AttachGraphSubscriptions(TaskGraph? graph)
    {
        if (graph is null)
        {
            return;
        }

        graph.PropertyChanged += OnGraphPropertyChanged;
        graph.Nodes.CollectionChanged += OnNodesCollectionChanged;
        foreach (var node in graph.Nodes)
        {
            node.PropertyChanged += OnNodePropertyChanged;
        }
    }

    private void DetachGraphSubscriptions(TaskGraph? graph)
    {
        if (graph is null)
        {
            return;
        }

        graph.PropertyChanged -= OnGraphPropertyChanged;
        graph.Nodes.CollectionChanged -= OnNodesCollectionChanged;
        foreach (var node in graph.Nodes)
        {
            node.PropertyChanged -= OnNodePropertyChanged;
        }
    }

    private void OnGraphPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TaskGraph.Name) or nameof(TaskGraph.ExecutionState))
        {
            OnPropertyChanged(nameof(HeaderTitle));
            OnPropertyChanged(nameof(CurrentGraphName));
            OnPropertyChanged(nameof(CurrentGraphExecutionText));
            OnPropertyChanged(nameof(CanExecute));
            OnPropertyChanged(nameof(CanContinue));
            OnPropertyChanged(nameof(CanCancelExecution));
            OnPropertyChanged(nameof(IsWaitingForUserConfirmation));
        }
    }

    private void OnNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (TaskNode node in e.NewItems)
            {
                node.PropertyChanged += OnNodePropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (TaskNode node in e.OldItems)
            {
                node.PropertyChanged -= OnNodePropertyChanged;
            }
        }

        RefreshGraphSurface();
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TaskNode.Status)
            or nameof(TaskNode.LastError)
            or nameof(TaskNode.OutputSummary)
            or nameof(TaskNode.Title)
            or nameof(TaskNode.Position)
            or nameof(TaskNode.IsSelected))
        {
            OnPropertyChanged(nameof(CurrentGraphSummaryText));
            OnPropertyChanged(nameof(CanRetryFailed));
            OnPropertyChanged(nameof(IsWaitingForUserConfirmation));
            OnPropertyChanged(nameof(HasSelectedNode));
            OnPropertyChanged(nameof(HasNoSelectedNode));
            OnPropertyChanged(nameof(SelectedNodeHasFiles));
            OnPropertyChanged(nameof(SelectedNodeHasTags));
            OnPropertyChanged(nameof(CanStartLink));
            OnPropertyChanged(nameof(CanCreateLink));
            OnPropertyChanged(nameof(CanRemoveSelectedNodeLinks));
            RefreshBugReport();
            RefreshGraphSurface();
        }
    }

    private void RefreshGraphSurface()
    {
        GraphNodes.Clear();
        GraphEdges.Clear();

        if (CurrentGraph is null || CurrentGraph.Nodes.Count == 0)
        {
            GraphCanvasWidth = 1200;
            GraphCanvasHeight = 720;
            return;
        }

        foreach (var node in CurrentGraph.Nodes)
        {
            GraphNodes.Add(node);
        }

        var byId = CurrentGraph.Nodes.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var edge in CurrentGraph.Edges)
        {
            if (!byId.TryGetValue(edge.SourceId, out var source) || !byId.TryGetValue(edge.TargetId, out var target))
            {
                continue;
            }

            GraphEdges.Add(new TaskGraphEdgeViewModel(
                edge.SourceId,
                edge.TargetId,
                source.Position.X + NodeWidth,
                source.Position.Y + (NodeHeight / 2),
                target.Position.X,
                target.Position.Y + (NodeHeight / 2)));
        }

        GraphCanvasWidth = Math.Max(1200, CurrentGraph.Nodes.Max(x => x.Position.X) + NodeWidth + CanvasPadding);
        GraphCanvasHeight = Math.Max(720, CurrentGraph.Nodes.Max(x => x.Position.Y) + NodeHeight + CanvasPadding);
        RefreshLinkableTargets();
    }

    private void DetachSelection()
    {
        foreach (var node in GraphNodes)
        {
            node.IsSelected = false;
        }
    }

    private void ApplyUserConfirmation()
    {
        if (CurrentGraph is null || CurrentGraph.ExecutionState != TaskGraphExecutionState.WaitingForInput)
        {
            return;
        }

        var confirmNode = CurrentGraph.Nodes.FirstOrDefault(x => x.Kind == TaskNodeKind.HumanInput && x.Tags.Contains("AwaitUserConfirmation"));
        if (confirmNode is null)
        {
            return;
        }

        var confirmation = string.IsNullOrWhiteSpace(UserConfirmationText)
            ? "用户已确认当前方案，可以继续。"
            : $"用户确认意见：{UserConfirmationText.Trim()}";

        confirmNode.OutputSummary = confirmation;
        confirmNode.RawOutput = confirmation;
        confirmNode.LastError = null;
    }

    private void RefreshLinkableTargets()
    {
        LinkableTargetNodes.Clear();
        if (CurrentGraph is null || SelectedNode is null)
        {
            return;
        }

        foreach (var node in CurrentGraph.Nodes.Where(x => !string.Equals(x.Id, SelectedNode.Id, StringComparison.Ordinal)))
        {
            LinkableTargetNodes.Add(new TaskGraphNodeReferenceViewModel(node.Id, node.Title));
        }

        if (SelectedLinkTarget is not null && !LinkableTargetNodes.Any(x => x.Id == SelectedLinkTarget.Id))
        {
            SelectedLinkTarget = null;
        }
    }

    [RelayCommand]
    private async Task ExportBugReportMarkdownAsync()
    {
        if (string.IsNullOrWhiteSpace(BugReportMarkdown))
        {
            StatusText = "当前没有可导出的 Bug 报告。";
            return;
        }

        await ExportTextAsync("bug-report.md", BugReportMarkdown).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ExportBugReportJsonAsync()
    {
        if (string.IsNullOrWhiteSpace(BugReportJson))
        {
            StatusText = "当前没有可导出的 Bug 报告 JSON。";
            return;
        }

        await ExportTextAsync("bug-report.json", BugReportJson).ConfigureAwait(true);
    }

    private async Task ExportTextAsync(string suggestedFileName, string content)
    {
        try
        {
            var exportDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AgentOrchestrator",
                "Exports");
            Directory.CreateDirectory(exportDirectory);
            var filePath = Path.Combine(exportDirectory, suggestedFileName);
            await File.WriteAllTextAsync(filePath, content).ConfigureAwait(true);
            StatusText = $"已导出到 {filePath}";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    private void RefreshBugReport()
    {
        BugReportRows.Clear();
        BugReportMarkdown = string.Empty;
        BugReportJson = string.Empty;
        OnPropertyChanged(nameof(HasBugReport));

        if (CurrentGraph?.TemplateKind != TaskGraphTemplateKind.BugList)
        {
            return;
        }

        if (!TaskGraphBugStructuredParser.TryBuildReport(CurrentGraph, out var rows))
        {
            return;
        }

        foreach (var row in rows)
        {
            BugReportRows.Add(new BugReportRowViewModel(
                row.ProblemDescription,
                row.Resolved ? "是" : "否",
                row.Reliability?.ToString() ?? "-",
                row.Reason,
                row.Verification));
        }

        BugReportMarkdown = TaskGraphBugStructuredParser.RenderReportMarkdown(rows);
        BugReportJson = TaskGraphBugStructuredParser.RenderReportJson(rows);
        OnPropertyChanged(nameof(HasBugReport));
    }

    private static string BuildNextNodeId(TaskGraph graph)
    {
        var index = 1;
        while (graph.Nodes.Any(x => string.Equals(x.Id, $"manual_{index}", StringComparison.Ordinal)))
        {
            index++;
        }

        return $"manual_{index}";
    }

    private static NodePosition BuildNextNodePosition(TaskGraph graph)
    {
        if (graph.Nodes.Count == 0)
        {
            return new NodePosition(120, 120);
        }

        var maxX = graph.Nodes.Max(x => x.Position.X);
        var maxY = graph.Nodes.Max(x => x.Position.Y);
        return new NodePosition(maxX + 120, Math.Max(80, maxY));
    }
}

public enum TaskGraphInputMode
{
    Template,
    Direct,
    Intent,
    Document,
}

public sealed record TaskGraphNodeDetailRequest(
    string GraphId,
    TaskNode Node,
    string? WorkingDirectory
);
