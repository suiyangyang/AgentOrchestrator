using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.Sidebar;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.DialogHost;
using AgentOrchestrator.App.Services.Sidebar;
using AgentOrchestrator.App.Services.TaskGraph;
using AgentOrchestrator.App.ViewModels;
using Xunit;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Tests;

public sealed class TaskGraphWorkspaceViewModelNamingAndNodeConstraintTests : IDisposable
{
    private readonly string _tempDir;

    public TaskGraphWorkspaceViewModelNamingAndNodeConstraintTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NamingConstraint_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    // ── Naming dialog behavior ─────────────────────────────────────

    [Fact]
    public void BeginNamingDialog_OpensAndPrefillsFromNewGraphNameDraft()
    {
        var vm = CreateWorkspaceVM();
        vm.NewGraphNameDraft = "草稿名";

        vm.BeginNamingDialog();

        Assert.True(vm.IsNamingDialogVisible);
        Assert.Equal("草稿名", vm.NamingDialogNameDraft);
        Assert.True(vm.CanConfirmNamingDialog);
    }

    [Fact]
    public void BeginNamingDialog_PrePopulatesPlaceholderWhenDraftIsEmpty()
    {
        var vm = CreateWorkspaceVM();

        vm.BeginNamingDialog();

        Assert.True(vm.IsNamingDialogVisible);
        Assert.False(string.IsNullOrWhiteSpace(vm.NamingDialogNameDraft));
        Assert.True(vm.CanConfirmNamingDialog);
    }

    [Fact]
    public async Task ConfirmNamingDialogAsync_RejectsEmptyName()
    {
        var vm = CreateWorkspaceVM();
        vm.BeginNamingDialog();
        vm.NamingDialogNameDraft = "   ";

        await vm.ConfirmNamingDialogAsync();

        Assert.True(vm.IsNamingDialogVisible); // dialog stays open
        Assert.Null(vm.CurrentGraph);
    }

    [Fact]
    public async Task ConfirmNamingDialogAsync_CreatesGraphAndDefaultNode()
    {
        var vm = CreateWorkspaceVM();
        vm.BeginNamingDialog();
        vm.NamingDialogNameDraft = "我的新编排";

        await vm.ConfirmNamingDialogAsync();

        Assert.False(vm.IsNamingDialogVisible);
        Assert.NotNull(vm.CurrentGraph);
        Assert.Equal("我的新编排", vm.CurrentGraph!.Name);
        Assert.Equal(TaskGraphDocumentKind.Runtime, vm.CurrentGraph.DocumentKind);
        // Default node bootstrapped.
        Assert.Single(vm.CurrentGraph.Nodes);
        Assert.Equal(TaskNodeKind.Execute, vm.CurrentGraph.Nodes[0].Kind);
    }

    [Fact]
    public void CancelNamingDialog_HidesDialogWithoutCreatingGraph()
    {
        var vm = CreateWorkspaceVM();
        vm.BeginNamingDialog();

        vm.CancelNamingDialog();

        Assert.False(vm.IsNamingDialogVisible);
        Assert.Null(vm.CurrentGraph);
    }

    // ── Last-node-delete guard ─────────────────────────────────────

    [Fact]
    public async Task DeleteSelectedNode_RefusesToRemoveLastNodeOfRuntimeGraph()
    {
        var vm = CreateWorkspaceVM();
        var graph = new TaskGraphModel
        {
            Name = "单节点图",
            DocumentKind = TaskGraphDocumentKind.Runtime,
        };
        graph.Nodes.Add(new TaskNode { Id = "single", Title = "唯一节点", Kind = TaskNodeKind.Execute });
        vm.CurrentGraph = graph;
        vm.SelectedNode = graph.Nodes[0];

        // Runtime graph with exactly 1 node — delete is disallowed.
        Assert.False(vm.CanDeleteNode);

        await vm.DeleteSelectedNodeCommand.ExecuteAsync(null);

        Assert.Single(vm.CurrentGraph.Nodes); // not removed
        Assert.NotNull(vm.SelectedNode);
    }

    [Fact]
    public void CanDeleteNode_IsFalseWhenRuntimeGraphHasExactlyOneNode()
    {
        var vm = CreateWorkspaceVM();
        var graph = new TaskGraphModel
        {
            Name = "单节点图",
            DocumentKind = TaskGraphDocumentKind.Runtime,
        };
        graph.Nodes.Add(new TaskNode { Id = "single", Title = "唯一节点", Kind = TaskNodeKind.Execute });
        vm.CurrentGraph = graph;
        vm.SelectedNode = graph.Nodes[0];

        // After current node, the delete button is correctly disabled.
        Assert.False(vm.CanDeleteNode);
    }

    [Fact]
    public void CanDeleteNode_IsTrueWhenRuntimeGraphHasMoreThanOneNode()
    {
        var vm = CreateWorkspaceVM();
        var graph = new TaskGraphModel
        {
            Name = "多节点图",
            DocumentKind = TaskGraphDocumentKind.Runtime,
        };
        graph.Nodes.Add(new TaskNode { Id = "n1", Title = "1", Kind = TaskNodeKind.Execute });
        graph.Nodes.Add(new TaskNode { Id = "n2", Title = "2", Kind = TaskNodeKind.Execute });
        vm.CurrentGraph = graph;
        vm.SelectedNode = graph.Nodes[0];

        Assert.True(vm.CanDeleteNode);
    }

    // ── Helper ────────────────────────────────────────────────────

    private TaskGraphWorkspaceViewModel CreateWorkspaceVM()
    {
        var store = new JsonTaskGraphStore(_tempDir);
        var sidebarRepo = new FakeSidebarRepository();
        var sidebar = new SidebarViewModel(sidebarRepo, store);
        return new TaskGraphWorkspaceViewModel(
            store,
            new FakeDirectParser(),
            new FakePlanner(),
            new FakeDocumentReader(),
            new FakeExecutor(),
            sidebar,
            new AvaloniaDialogHost());
    }

    private sealed class FakeDirectParser : ITaskGraphDirectParser
    {
        public TaskGraphModel Parse(string input) => throw new NotSupportedException();
    }

    private sealed class FakePlanner : ITaskGraphPlanner
    {
        public Task<TaskGraphModel> CreateFromIntentAsync(string intent, string workingDirectory, string permissionKey, string model, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<TaskGraphModel> CreateFromDocumentAsync(string filePath, string content, string workingDirectory, string permissionKey, string model, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeDocumentReader : IDocumentReader
    {
        public Task<string> ReadAsync(string filePath, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeExecutor : ITaskGraphExecutor
    {
        public Task ExecuteAsync(TaskGraphModel graph, TaskGraphExecutionRequest request, CancellationToken ct = default) => Task.CompletedTask;
        public Task ContinueAsync(TaskGraphModel graph, TaskGraphExecutionRequest request, CancellationToken ct = default) => Task.CompletedTask;
        public Task RetryFailedAsync(TaskGraphModel graph, TaskGraphExecutionRequest request, CancellationToken ct = default) => Task.CompletedTask;
        public void RequestCancel(string graphId) { }
        public Task ReconcileAsync(TaskGraphModel graph, CancellationToken ct = default) => Task.CompletedTask;
    }
}
