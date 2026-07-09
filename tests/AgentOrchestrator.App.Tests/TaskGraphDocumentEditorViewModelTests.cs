using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.Sidebar;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.Sidebar;
using AgentOrchestrator.App.Services.TaskGraph;
using AgentOrchestrator.App.ViewModels;
using Xunit;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Tests;

/// <summary>
/// Minimal in-memory implementation of <see cref="ISidebarRepository"/> for tests.
/// </summary>
internal sealed class FakeSidebarRepository : ISidebarRepository
{
    public Task InitializeAsync(CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<ProjectRecord>> ListProjectsAsync(CancellationToken ct = default)
        => Task.FromResult((IReadOnlyList<ProjectRecord>)Array.Empty<ProjectRecord>());

    public Task<ProjectRecord?> GetProjectAsync(string id, CancellationToken ct = default)
        => Task.FromResult<ProjectRecord?>(null);

    public Task<ProjectRecord> CreateProjectAsync(string name, string directory, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task RenameProjectAsync(string id, string newName, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteProjectAsync(string id, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<SessionRecord>> ListSessionsAsync(CancellationToken ct = default)
        => Task.FromResult((IReadOnlyList<SessionRecord>)Array.Empty<SessionRecord>());

    public Task<SessionRecord?> GetSessionAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<SessionRecord?>(null);

    public Task<SessionRecord> CreateSessionAsync(SessionRecord record, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task UpdateSessionAsync(SessionRecord record, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteSessionAsync(string sessionId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task MoveSessionToProjectAsync(string sessionId, string? projectId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<SessionRecord>> SearchSessionsByTitleAsync(string titleQuery, CancellationToken ct = default)
        => Task.FromResult((IReadOnlyList<SessionRecord>)Array.Empty<SessionRecord>());
}

public sealed class TaskGraphDocumentEditorViewModelTests : IDisposable
{
    private readonly string _tempDir;

    public TaskGraphDocumentEditorViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "EditorVMTests_" + Guid.NewGuid().ToString("N"));
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

    private TaskGraphDocumentEditorViewModel CreateEditor()
    {
        var store = CreateStore();
        var sidebarRepo = new FakeSidebarRepository();
        var sidebar = new SidebarViewModel(sidebarRepo, store);
        return new TaskGraphDocumentEditorViewModel(store, sidebar);
    }

    private static TaskGraphModel CreateTemplateGraph(string id, string name)
        => new()
        {
            Id = id,
            Name = name,
            DocumentKind = TaskGraphDocumentKind.Template,
            IsBuiltInTemplate = false,
        };

    private static TaskGraphModel CreateRuntimeGraph(string id, string name)
        => new()
        {
            Id = id,
            Name = name,
            DocumentKind = TaskGraphDocumentKind.Runtime,
        };

    [Fact]
    public async Task LoadDocumentByIdAsync_TemplateDocument_SetsIsTemplateDocument()
    {
        // Arrange
        var store = CreateStore();
        var template = CreateTemplateGraph("t1", "测试模板");
        template.TemplateMetadata = new TaskGraphTemplateMetadata();
        await store.SaveAsync(template);

        var editor = CreateEditor();

        // Act
        await editor.LoadDocumentByIdAsync("t1");

        // Assert
        Assert.NotNull(editor.CurrentDocument);
        Assert.Equal(TaskGraphDocumentKind.Template, editor.CurrentDocument.DocumentKind);
        Assert.True(editor.IsTemplateDocument);
        Assert.False(editor.IsRuntimeDocument);
        Assert.True(editor.HasDocument);
        Assert.False(editor.IsEmpty);
        Assert.True(editor.CanEditTemplateRules);
        Assert.True(editor.CanInstantiate);
        Assert.True(editor.CanSave);
        Assert.Equal("测试模板", editor.Name);
    }

    [Fact]
    public async Task LoadDocumentByIdAsync_RuntimeDocument_SetsIsRuntimeDocument()
    {
        // Arrange
        var store = CreateStore();
        var graph = CreateRuntimeGraph("r1", "运行图");
        await store.SaveAsync(graph);

        var editor = CreateEditor();

        // Act
        await editor.LoadDocumentByIdAsync("r1");

        // Assert
        Assert.NotNull(editor.CurrentDocument);
        Assert.Equal(TaskGraphDocumentKind.Runtime, editor.CurrentDocument.DocumentKind);
        Assert.True(editor.IsRuntimeDocument);
        Assert.False(editor.IsTemplateDocument);
        Assert.True(editor.HasDocument);
        Assert.False(editor.CanInstantiate);
        Assert.True(editor.CanExecute);
    }

    [Fact]
    public async Task LoadDocumentByIdAsync_NonExistent_DoesNothing()
    {
        // Arrange
        var editor = CreateEditor();

        // Act
        await editor.LoadDocumentByIdAsync("nonexistent");

        // Assert
        Assert.Null(editor.CurrentDocument);
        Assert.False(editor.HasDocument);
        Assert.True(editor.IsEmpty);
    }

    [Fact]
    public async Task SaveAsync_PersistsTemplateNotesAndAllowDynamicExpansion()
    {
        // Arrange
        var store = CreateStore();
        var template = CreateTemplateGraph("t2", "模板");
        template.TemplateMetadata = new TaskGraphTemplateMetadata { AllowDynamicExpansion = false };
        template.TemplateNotes = "old note";
        await store.SaveAsync(template);

        var editor = CreateEditor();
        await editor.LoadDocumentByIdAsync("t2");

        // Act - update via editor properties
        // Two-way bindings set CurrentDocument properties directly
        editor.CurrentDocument!.TemplateNotes = "new note";
        editor.CurrentDocument.TemplateMetadata!.AllowDynamicExpansion = true;

        await editor.SaveCommand.ExecuteAsync(null);

        // Assert - reload and check persistence
        var reloaded = await store.LoadAsync("t2");
        Assert.NotNull(reloaded);
        Assert.Equal("new note", reloaded.TemplateNotes);
        Assert.True(reloaded.TemplateMetadata?.AllowDynamicExpansion);
    }

    [Fact]
    public async Task AddDynamicZone_AppendsToTemplateMetadata()
    {
        // Arrange
        var store = CreateStore();
        var template = CreateTemplateGraph("t3", "模板");
        template.TemplateMetadata = new TaskGraphTemplateMetadata();
        await store.SaveAsync(template);

        var editor = CreateEditor();
        await editor.LoadDocumentByIdAsync("t3");

        int initialCount = editor.DynamicZones.Count;

        // Act
        editor.AddDynamicZoneCommand.Execute(null);

        // Assert
        Assert.Equal(initialCount + 1, editor.DynamicZones.Count);
        Assert.Equal(initialCount + 1, editor.CurrentDocument!.TemplateMetadata!.DynamicZones.Count);

        // Persist and verify
        await editor.SaveCommand.ExecuteAsync(null);
        var reloaded = await store.LoadAsync("t3");
        Assert.Equal(initialCount + 1, reloaded!.TemplateMetadata!.DynamicZones.Count);
    }

    [Fact]
    public async Task RemoveDynamicZone_RemovesFromTemplateMetadata()
    {
        // Arrange
        var store = CreateStore();
        var template = CreateTemplateGraph("t4", "模板");
        var zone = new DynamicZoneDefinition { Id = "zone1", Name = "区域1" };
        template.TemplateMetadata = new TaskGraphTemplateMetadata
        {
            DynamicZones = new List<DynamicZoneDefinition> { zone },
        };
        await store.SaveAsync(template);

        var editor = CreateEditor();
        await editor.LoadDocumentByIdAsync("t4");
        Assert.Single(editor.DynamicZones);

        var zoneVm = editor.DynamicZones[0];

        // Act
        editor.RemoveDynamicZoneCommand.Execute(zoneVm);

        // Assert
        Assert.Empty(editor.DynamicZones);
        Assert.Empty(editor.CurrentDocument!.TemplateMetadata!.DynamicZones);
    }

    [Fact]
    public async Task InstantiateAsync_CreatesRuntimeGraphFromTemplate()
    {
        // Arrange
        var store = CreateStore();
        var template = CreateTemplateGraph("t5", "功能模板");
        template.TemplateMetadata = new TaskGraphTemplateMetadata();
        await store.SaveAsync(template);

        var editor = CreateEditor();
        await editor.LoadDocumentByIdAsync("t5");

        // Act
        var result = await editor.InstantiateAsync(
            new TemplateInstantiationOptions { RuntimeGraphName = "test-instance" });

        // Assert
        Assert.NotNull(result);
        Assert.Equal(TaskGraphDocumentKind.Runtime, result.DocumentKind);
        Assert.Equal("test-instance", result.Name);
        Assert.Equal("t5", result.BasedOnTemplateId);

        // Verify persistence
        var reloaded = await store.LoadAsync(result.Id);
        Assert.NotNull(reloaded);
    }

    [Fact]
    public async Task LoadDocumentAsync_NullDocument_ClearsEditor()
    {
        // Arrange
        var store = CreateStore();
        var template = CreateTemplateGraph("t6", "模板");
        await store.SaveAsync(template);

        var editor = CreateEditor();
        await editor.LoadDocumentByIdAsync("t6");
        Assert.True(editor.HasDocument);

        // Act
        await editor.LoadDocumentAsync(null);

        // Assert
        Assert.Null(editor.CurrentDocument);
        Assert.False(editor.HasDocument);
        Assert.True(editor.IsEmpty);
        Assert.Empty(editor.Name);
    }

    [Fact]
    public async Task IsBuiltInTemplate_PreventsEditingAndInstantiation()
    {
        // Arrange
        var store = CreateStore();
        var template = CreateTemplateGraph("t7", "内置模板");
        template.IsBuiltInTemplate = true;
        template.TemplateMetadata = new TaskGraphTemplateMetadata();
        await store.SaveAsync(template);

        var editor = CreateEditor();
        await editor.LoadDocumentByIdAsync("t7");

        // Assert
        Assert.True(editor.IsBuiltInTemplate);
        Assert.True(editor.IsReadOnly);
        Assert.False(editor.CanEditTemplateRules);
        Assert.False(editor.CanInstantiate);
        Assert.False(editor.CanSave);
    }
}
