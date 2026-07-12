using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.DialogHost;
using AgentOrchestrator.App.Services.Sidebar;
using AgentOrchestrator.App.Services.TaskGraph;
using AgentOrchestrator.App.ViewModels;
using Xunit;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Tests;

public sealed class TaskOrchestrationWorkspaceViewModelTests : IDisposable
{
    private readonly string _tempDir;

    public TaskOrchestrationWorkspaceViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "OrchVMTests_" + Guid.NewGuid().ToString("N"));
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

    private JsonTaskGraphStore CreateStore() => new(_tempDir);

    private TaskOrchestrationWorkspaceViewModel CreateVM(ITaskGraphStore? storeOverride = null)
    {
        var store = storeOverride ?? new JsonTaskGraphStore(_tempDir);
        var sidebarRepo = new FakeSidebarRepository();
        var sidebar = new SidebarViewModel(sidebarRepo, store);
        var dialogHost = new FakeDialogHost();
        var workspace = new TaskGraphWorkspaceViewModel(
            store,
            new FakeDirectParser(),
            new FakePlanner(),
            new FakeDocumentReader(),
            new FakeExecutor(),
            sidebar,
            dialogHost);
        var editor = new TaskGraphDocumentEditorViewModel(store, sidebar);
        return new TaskOrchestrationWorkspaceViewModel(store, workspace, editor, dialogHost);
    }

    // ── Test: NewTemplate creates a TaskGraph with DocumentKind=Template ──
    [Fact]
    public async Task NewTemplateAsync_CreatesTaskGraphTemplate()
    {
        var store = CreateStore();
        var vm = CreateVM(store);

        // Wait for async init to settle.
        await Task.Delay(100);

        await vm.NewTemplateCommand.ExecuteAsync(null);

        var templates = await store.ListTemplatesAsync();
        Assert.Single(templates);
        var template = await store.LoadTemplateAsync(templates[0].Id);
        Assert.NotNull(template);
        Assert.Equal(TaskGraphDocumentKind.Template, template.DocumentKind);
        Assert.False(template.IsBuiltInTemplate);
        Assert.Equal("新模板", template.Name);
    }

    // ── Test: DeleteTemplate removes from store, built-ins are protected ──
    [Fact]
    public async Task DeleteTemplateAsync_RemovesFromStore_BuiltInsProtected()
    {
        var store = CreateStore();

        // Pre-seed a user template.
        var userTemplate = new TaskGraphModel
        {
            Id = "user-tpl",
            Name = "用户模板",
            DocumentKind = TaskGraphDocumentKind.Template,
            IsBuiltInTemplate = false,
        };
        await store.SaveAsync(userTemplate);

        // Pre-seed a built-in template.
        var builtInTemplate = new TaskGraphModel
        {
            Id = "builtin-tpl",
            Name = "内置模板",
            DocumentKind = TaskGraphDocumentKind.Template,
            IsBuiltInTemplate = true,
        };
        await store.SaveAsync(builtInTemplate);

        var vm = CreateVM(store);
        await Task.Delay(100);
        await vm.RefreshCommand.ExecuteAsync(null);

        // Delete user template.
        Assert.NotEmpty(vm.Templates);
        var userVm = vm.Templates.First(t => t.Id == "user-tpl");
        await vm.DeleteTemplateCommand.ExecuteAsync(userVm);

        // User template should be gone.
        var remaining = await store.ListTemplatesAsync();
        Assert.DoesNotContain(remaining, t => t.Id == "user-tpl");

        // Built-in should still be there.
        var builtInVm = vm.Templates.FirstOrDefault(t => t.Id == "builtin-tpl");
        Assert.NotNull(builtInVm);
        Assert.True(builtInVm.IsBuiltIn);
    }

    // ── Test: DuplicateTemplate creates a copy with "副本" suffix ──
    [Fact]
    public async Task DuplicateTemplateAsync_CreatesNewTaskGraphTemplate_WithCopyName()
    {
        var store = CreateStore();

        var source = new TaskGraphModel
        {
            Id = "source-tpl",
            Name = "Source",
            DocumentKind = TaskGraphDocumentKind.Template,
            IsBuiltInTemplate = false,
        };
        await store.SaveAsync(source);

        var vm = CreateVM(store);
        await Task.Delay(100);
        await vm.RefreshCommand.ExecuteAsync(null);

        var sourceVm = vm.Templates.First(t => t.Id == "source-tpl");
        await vm.DuplicateTemplateCommand.ExecuteAsync(sourceVm);

        var templates = await store.ListTemplatesAsync();
        Assert.True(templates.Count >= 2);

        var copy = templates.FirstOrDefault(t => t.Name == "Source 副本");
        Assert.NotNull(copy);
        Assert.NotEqual("source-tpl", copy.Id);

        var loaded = await store.LoadTemplateAsync(copy.Id);
        Assert.NotNull(loaded);
        Assert.False(loaded.IsBuiltInTemplate);
    }

    // ── Test: CommitTemplateRename updates name ──
    [Fact]
    public async Task CommitTemplateRenameAsync_UpdatesName()
    {
        var store = CreateStore();

        var template = new TaskGraphModel
        {
            Id = "rename-tpl",
            Name = "Old Name",
            DocumentKind = TaskGraphDocumentKind.Template,
            IsBuiltInTemplate = false,
        };
        await store.SaveAsync(template);

        var vm = CreateVM(store);
        await Task.Delay(100);
        await vm.RefreshCommand.ExecuteAsync(null);

        var templateVm = vm.Templates.First(t => t.Id == "rename-tpl");
        templateVm.Name = "New Name";
        await vm.CommitTemplateRenameCommand.ExecuteAsync(templateVm);

        var loaded = await store.LoadTemplateAsync("rename-tpl");
        Assert.NotNull(loaded);
        Assert.Equal("New Name", loaded.Name);
    }

    // ── Test: Templates are loaded from TaskGraph store ──
    [Fact]
    public async Task Templates_AreLoadedFromTaskGraphStore()
    {
        var store = CreateStore();

        await store.SaveAsync(new TaskGraphModel
        {
            Id = "tpl-1",
            Name = "模板一",
            DocumentKind = TaskGraphDocumentKind.Template,
        });
        await store.SaveAsync(new TaskGraphModel
        {
            Id = "tpl-2",
            Name = "模板二",
            DocumentKind = TaskGraphDocumentKind.Template,
        });
        await store.SaveAsync(new TaskGraphModel
        {
            Id = "run-1",
            Name = "运行图",
            DocumentKind = TaskGraphDocumentKind.Runtime,
            // Runtime graphs must keep at least one node (SaveAsync guard).
            Nodes = { new TaskNode { Id = "run_1_seed", Title = "seed", Kind = TaskNodeKind.Execute } },
        });

        var vm = CreateVM(store);
        await Task.Delay(100);
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Templates.Count);
        Assert.Contains(vm.Templates, t => t.Id == "tpl-1");
        Assert.Contains(vm.Templates, t => t.Id == "tpl-2");
        Assert.DoesNotContain(vm.Templates, t => t.Id == "run-1");

        Assert.Single(vm.TaskGraphs);
        Assert.Contains(vm.TaskGraphs, t => t.Id == "run-1");
    }

    // ── Regression: Bug #4 — NewTemplateAsync must update the Templates collection ──
    [Fact]
    public async Task NewTemplateAsync_NewTemplateAppearsInTemplatesCollection()
    {
        var store = CreateStore();
        var vm = CreateVM(store);
        await Task.Delay(100);

        Assert.Empty(vm.Templates);
        await vm.NewTemplateCommand.ExecuteAsync(null);

        Assert.NotEmpty(vm.Templates);
        Assert.Contains(vm.Templates, t => t.Name == "新模板");
    }

    // ── Regression: Bug #2 — DeleteTemplate must remove from the Templates collection ──
    [Fact]
    public async Task DeleteTemplateAsync_TemplateRemovedFromCollection()
    {
        var store = CreateStore();

        var userTemplate = new TaskGraphModel
        {
            Id = "del-tpl",
            Name = "待删除模板",
            DocumentKind = TaskGraphDocumentKind.Template,
            IsBuiltInTemplate = false,
        };
        await store.SaveAsync(userTemplate);

        var vm = CreateVM(store);
        await Task.Delay(100);
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Contains(vm.Templates, t => t.Id == "del-tpl");

        var templateVm = vm.Templates.First(t => t.Id == "del-tpl");
        await vm.DeleteTemplateCommand.ExecuteAsync(templateVm);

        Assert.DoesNotContain(vm.Templates, t => t.Id == "del-tpl");
    }

    // ── Regression: Bug #3 — CommitTemplateRename must reflect in Templates and selection ──
    [Fact]
    public async Task CommitTemplateRenameAsync_NewNameReflectedInTemplatesAndSelection()
    {
        var store = CreateStore();

        var template = new TaskGraphModel
        {
            Id = "rename-tpl",
            Name = "Old Name",
            DocumentKind = TaskGraphDocumentKind.Template,
            IsBuiltInTemplate = false,
        };
        await store.SaveAsync(template);

        var vm = CreateVM(store);
        await Task.Delay(100);
        await vm.RefreshCommand.ExecuteAsync(null);

        var templateVm = vm.Templates.First(t => t.Id == "rename-tpl");
        // Select the template so we can verify selection is preserved after rename.
        vm.SelectTemplateCommand.Execute(templateVm);
        Assert.Equal("rename-tpl", vm.SelectedTemplate?.Id);

        templateVm.Name = "New Name";
        await vm.CommitTemplateRenameCommand.ExecuteAsync(templateVm);

        // Data persisted and reflected in the collection.
        Assert.Contains(vm.Templates, t => t.Id == "rename-tpl" && t.Name == "New Name");
        // Selection preserved with the new name.
        Assert.NotNull(vm.SelectedTemplate);
        Assert.Equal("rename-tpl", vm.SelectedTemplate!.Id);
        Assert.Equal("New Name", vm.SelectedTemplate!.Name);
    }

    // ── Regression: Bug #1 — RenameTaskGraph must update TaskOrchestration list ──
    [Fact]
    public async Task RenameTaskGraph_UpdatesTaskOrchestrationList()
    {
        var store = CreateStore();

        var graph = new TaskGraphModel
        {
            Id = "run-rename",
            Name = "Old Runtime",
            DocumentKind = TaskGraphDocumentKind.Runtime,
            Nodes = { new TaskNode { Id = "n1", Title = "seed", Kind = TaskNodeKind.Execute } },
        };
        await store.SaveAsync(graph);

        var sidebarRepo = new FakeSidebarRepository();
        var sidebar = new SidebarViewModel(sidebarRepo, store);
        var dialogHost1 = new FakeDialogHost();
        var workspace = new TaskGraphWorkspaceViewModel(
            store, new FakeDirectParser(), new FakePlanner(),
            new FakeDocumentReader(), new FakeExecutor(), sidebar,
            dialogHost1);
        var editor = new TaskGraphDocumentEditorViewModel(store, sidebar);
        var vm = new TaskOrchestrationWorkspaceViewModel(store, workspace, editor, dialogHost1);
        await Task.Delay(100);
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Contains(vm.TaskGraphs, g => g.Id == "run-rename" && g.Name == "Old Runtime");

        // Simulate what MainWindowViewModel does: rename via Sidebar, then refresh orchestration.
        await sidebar.RenameTaskGraphAsync("run-rename", "New Runtime");
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Contains(vm.TaskGraphs, g => g.Id == "run-rename" && g.Name == "New Runtime");
    }

    // ── Regression: Bug #1 — RemoveTaskGraph must update TaskOrchestration list ──
    [Fact]
    public async Task RemoveTaskGraph_UpdatesTaskOrchestrationList()
    {
        var store = CreateStore();

        var graph = new TaskGraphModel
        {
            Id = "run-remove",
            Name = "To Remove",
            DocumentKind = TaskGraphDocumentKind.Runtime,
            Nodes = { new TaskNode { Id = "n1", Title = "seed", Kind = TaskNodeKind.Execute } },
        };
        await store.SaveAsync(graph);

        var sidebarRepo = new FakeSidebarRepository();
        var sidebar = new SidebarViewModel(sidebarRepo, store);
        var dialogHost2 = new FakeDialogHost();
        var workspace = new TaskGraphWorkspaceViewModel(
            store, new FakeDirectParser(), new FakePlanner(),
            new FakeDocumentReader(), new FakeExecutor(), sidebar,
            dialogHost2);
        var editor = new TaskGraphDocumentEditorViewModel(store, sidebar);
        var vm = new TaskOrchestrationWorkspaceViewModel(store, workspace, editor, dialogHost2);
        await Task.Delay(100);
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Contains(vm.TaskGraphs, g => g.Id == "run-remove");

        // Simulate what MainWindowViewModel does: remove via Sidebar, then refresh orchestration.
        await sidebar.RemoveTaskGraphAsync("run-remove");
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.DoesNotContain(vm.TaskGraphs, g => g.Id == "run-remove");
    }

    // ── Test: DeleteTemplateAsync shows confirmation; cancel does NOT delete ──
    [Fact]
    public async Task DeleteTemplateAsync_UserCancels_DoesNotDelete()
    {
        var store = CreateStore();

        var userTemplate = new TaskGraphModel
        {
            Id = "cancel-del-tpl",
            Name = "不可删除",
            DocumentKind = TaskGraphDocumentKind.Template,
            IsBuiltInTemplate = false,
        };
        await store.SaveAsync(userTemplate);

        var cancelDialogHost = new FakeDialogHost { ConfirmResult = false };
        var sidebarRepo = new FakeSidebarRepository();
        var sidebar = new SidebarViewModel(sidebarRepo, store);
        var workspace = new TaskGraphWorkspaceViewModel(
            store, new FakeDirectParser(), new FakePlanner(),
            new FakeDocumentReader(), new FakeExecutor(), sidebar,
            cancelDialogHost);
        var editor = new TaskGraphDocumentEditorViewModel(store, sidebar);
        var vm = new TaskOrchestrationWorkspaceViewModel(store, workspace, editor, cancelDialogHost);
        await Task.Delay(100);
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Contains(vm.Templates, t => t.Id == "cancel-del-tpl");

        var templateVm = vm.Templates.First(t => t.Id == "cancel-del-tpl");
        await vm.DeleteTemplateCommand.ExecuteAsync(templateVm);

        // Template should still exist in the store.
        var templates = await store.ListTemplatesAsync();
        Assert.Contains(templates, t => t.Id == "cancel-del-tpl");

        // Template should still exist in the VM's Templates collection.
        Assert.Contains(vm.Templates, t => t.Id == "cancel-del-tpl");
    }

    // ── Fake implementations ─────────────────────────────────────────────
    // Must match those in TaskGraphWorkspaceViewModelTemplateModeTests.

    private sealed class FakeDialogHost : IDialogHost
    {
        public bool ConfirmResult { get; set; } = true;
        public Task<bool> ConfirmAsync(Avalonia.Controls.Window? owner, string title, string message)
            => Task.FromResult(ConfirmResult);
        public Task<string?> InputAsync(Avalonia.Controls.Window? owner, string title, string label, string initial)
            => Task.FromResult<string?>(initial);
        public Task<string?> SelectAsync(Avalonia.Controls.Window? owner, string title, string label, IReadOnlyList<string> options, string? selectedOption = null)
            => Task.FromResult<string?>(options.FirstOrDefault());
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
