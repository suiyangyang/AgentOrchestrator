using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.Chat;
using AgentOrchestrator.App.Models.Sidebar;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.DialogHost;
using AgentOrchestrator.App.Services.Sidebar;
using AgentOrchestrator.App.Services.TaskGraph;
using AgentOrchestrator.App.ViewModels;
using Xunit;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Tests;

public sealed class TaskGraphWorkspaceViewModelTemplateModeTests : IDisposable
{
    private readonly string _tempDir;

    public TaskGraphWorkspaceViewModelTemplateModeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WorkspaceVMTemplateTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static TaskGraphModel CreateTemplateGraph(string id, string name)
        => new()
        {
            Id = id,
            Name = name,
            DocumentKind = TaskGraphDocumentKind.Template,
            IsBuiltInTemplate = false,
        };

    private static TaskGraphModel CreateRuntimeGraph(string id, string name, TaskGraphExecutionState state = TaskGraphExecutionState.Draft)
        => new()
        {
            Id = id,
            Name = name,
            DocumentKind = TaskGraphDocumentKind.Runtime,
            ExecutionState = state,
        };

    private void AddNodes(TaskGraphModel graph, int count)
    {
        for (int i = 0; i < count; i++)
        {
            graph.Nodes.Add(new TaskNode
            {
                Id = $"{graph.Id}_n{i}",
                Title = $"节点 {i}",
                Status = TaskNodeStatus.Pending,
            });
        }
    }

    [Fact]
    public void CanExecute_IsFalse_WhenCurrentGraphIsTemplate()
    {
        // Arrange
        var template = CreateTemplateGraph("t_template", "测试模板");
        AddNodes(template, 2);

        // Act & Assert
        var vm = CreateWorkspaceVM();
        vm.CurrentGraph = template;

        Assert.True(vm.IsTemplateDocument);
        Assert.False(vm.IsRuntimeDocument);
        Assert.False(vm.CanExecute);
        Assert.False(vm.CanRetryFailed);
        Assert.False(vm.CanCancelExecution);
        Assert.False(vm.CanContinue);
    }

    [Fact]
    public void CanExecute_IsTrue_WhenCurrentGraphIsRuntime_WithValidState()
    {
        // Arrange
        var graph = CreateRuntimeGraph("r_runtime", "运行图", TaskGraphExecutionState.Draft);
        AddNodes(graph, 2);

        // Act & Assert
        var vm = CreateWorkspaceVM();
        vm.CurrentGraph = graph;

        Assert.False(vm.IsTemplateDocument);
        Assert.True(vm.IsRuntimeDocument);
        Assert.True(vm.CanExecute);
    }

    [Fact]
    public void IsTemplateDocument_AndIsRuntimeDocument_ReflectCurrentGraph()
    {
        var vm = CreateWorkspaceVM();

        // Set to template
        var template = CreateTemplateGraph("t_reflect", "模板");
        vm.CurrentGraph = template;
        Assert.True(vm.IsTemplateDocument);
        Assert.False(vm.IsRuntimeDocument);

        // Set to runtime
        var graph = CreateRuntimeGraph("r_reflect", "运行图");
        vm.CurrentGraph = graph;
        Assert.False(vm.IsTemplateDocument);
        Assert.True(vm.IsRuntimeDocument);
    }

    [Fact]
    public async Task OpenTemplateByIdAsync_LoadsTemplateWithoutReconcile()
    {
        // Arrange
        var store = new JsonTaskGraphStore(_tempDir);
        var template = CreateTemplateGraph("t_open", "打开测试");
        template.TemplateNotes = "test note";
        template.ExecutionState = TaskGraphExecutionState.Running; // Should be preserved (no reconcile)
        await store.SaveAsync(template);

        var vm = CreateWorkspaceVM(store);

        // Act
        await vm.OpenTemplateByIdAsync("t_open");

        // Assert
        Assert.NotNull(vm.CurrentGraph);
        Assert.Equal("t_open", vm.CurrentGraph.Id);
        Assert.True(vm.IsTemplateDocument);
        Assert.Equal("test note", vm.CurrentGraph.TemplateNotes);
        // No reconcile should have touched ExecutionState
        Assert.Equal(TaskGraphExecutionState.Running, vm.CurrentGraph.ExecutionState);
        Assert.Equal("已打开模板“打开测试”。", vm.StatusText);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private TaskGraphWorkspaceViewModel CreateWorkspaceVM(ITaskGraphStore? storeOverride = null)
    {
        var store = storeOverride ?? new JsonTaskGraphStore(_tempDir);
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
        public Task ExecuteAsync(TaskGraphModel graph, TaskGraphExecutionRequest request, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ContinueAsync(TaskGraphModel graph, TaskGraphExecutionRequest request, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RetryFailedAsync(TaskGraphModel graph, TaskGraphExecutionRequest request, CancellationToken ct = default)
            => Task.CompletedTask;

        public void RequestCancel(string graphId) { }

        public Task ReconcileAsync(TaskGraphModel graph, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
