using System;
using System.IO;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.TaskGraph;
using Xunit;

namespace AgentOrchestrator.App.Tests;

public sealed class TaskTemplateMigrationServiceTests : IDisposable
{
    private readonly string _legacyDir;
    private readonly string _newDir;

    public TaskTemplateMigrationServiceTests()
    {
        var root = Path.Combine(Path.GetTempPath(), "AgentOrchestratorTests_" + Guid.NewGuid().ToString("N"));
        _legacyDir = Path.Combine(root, "legacy");
        _newDir = Path.Combine(root, "new");
        Directory.CreateDirectory(_legacyDir);
        Directory.CreateDirectory(_newDir);
    }

    public void Dispose()
    {
        try
        {
            var root = Path.GetDirectoryName(_legacyDir)!;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private TaskTemplateMigrationService CreateMigrator()
    {
        var newStore = new JsonTaskGraphStore(_newDir);
        return new TaskTemplateMigrationService(newStore, _legacyDir);
    }

    // ── Test: Reads old JSON via JsonDocument directly, no TaskTemplate model ──
    [Fact]
    public async Task MigrateAsync_ReadsOldJsonDirectly_AfterTaskTemplateModelDeleted()
    {
        var oldJson = """
            {
              "id": "test-template-1",
              "name": "Test Template",
              "description": "Test description",
              "baseKind": "TaskList",
              "defaultInput": "- task1\n- task2",
              "isBuiltIn": false,
              "createdAt": "2026-01-01T00:00:00Z",
              "updatedAt": "2026-01-01T00:00:00Z"
            }
            """;
        File.WriteAllText(Path.Combine(_legacyDir, "test-template-1.json"), oldJson);

        var migrator = CreateMigrator();
        var count = await migrator.MigrateAsync();

        Assert.Equal(1, count);

        var newStore = new JsonTaskGraphStore(_newDir);
        var graph = await newStore.LoadTemplateAsync("test-template-1");
        Assert.NotNull(graph);
        Assert.Equal(TaskGraphDocumentKind.Template, graph.DocumentKind);
        Assert.Equal("test-template-1", graph.Id);
        Assert.Equal("Test Template", graph.Name);
        Assert.Equal("Test description", graph.TemplateNotes);
        Assert.Equal("- task1\n- task2", graph.TemplatePlannerPrompt);
        Assert.Equal(TaskGraphTemplateKind.TaskList, graph.TemplateKind);
        Assert.False(graph.IsBuiltInTemplate);

        // Old file should be moved to _migrated/.
        Assert.False(File.Exists(Path.Combine(_legacyDir, "test-template-1.json")));
        Assert.True(File.Exists(Path.Combine(_legacyDir, "_migrated", "test-template-1.json")));
    }

    // ── Test: Handles malformed JSON — skips file ──
    [Fact]
    public async Task MigrateAsync_HandlesMalformedJson_SkipsFile()
    {
        // Valid file
        var validJson = """
            {
              "id": "valid-1",
              "name": "Valid",
              "description": "Desc",
              "baseKind": "Custom",
              "defaultInput": "",
              "isBuiltIn": false,
              "createdAt": "2026-01-01T00:00:00Z",
              "updatedAt": "2026-01-01T00:00:00Z"
            }
            """;
        File.WriteAllText(Path.Combine(_legacyDir, "valid-1.json"), validJson);
        // Malformed file
        File.WriteAllText(Path.Combine(_legacyDir, "malformed.json"), "{ not valid json ***");

        var migrator = CreateMigrator();
        var count = await migrator.MigrateAsync();

        Assert.Equal(1, count); // Only the valid one migrated.

        // Malformed file should NOT be moved.
        Assert.True(File.Exists(Path.Combine(_legacyDir, "malformed.json")));
        Assert.False(File.Exists(Path.Combine(_legacyDir, "_migrated", "malformed.json")));
    }

    // ── Test: Handles missing fields — defaults applied ──
    [Fact]
    public async Task MigrateAsync_HandlesMissingFields_DefaultsAreApplied()
    {
        var minimalJson = """{"id": "minimal-1", "name": "Minimal"}""";
        File.WriteAllText(Path.Combine(_legacyDir, "minimal-1.json"), minimalJson);

        var migrator = CreateMigrator();
        var count = await migrator.MigrateAsync();

        Assert.Equal(1, count);

        var newStore = new JsonTaskGraphStore(_newDir);
        var graph = await newStore.LoadTemplateAsync("minimal-1");
        Assert.NotNull(graph);
        Assert.Equal("minimal-1", graph.Id);
        Assert.Equal("Minimal", graph.Name);
        Assert.Equal(TaskGraphTemplateKind.Custom, graph.TemplateKind);
        Assert.False(graph.IsBuiltInTemplate);
        Assert.Equal(string.Empty, graph.TemplateNotes);
        Assert.Equal(string.Empty, graph.TemplatePlannerPrompt);
    }

    // ── Test: Idempotent — second run is a no-op ──
    [Fact]
    public async Task MigrateAsync_IsIdempotent()
    {
        var oldJson = """
            {
              "id": "tpl-x",
              "name": "Template X",
              "description": "Desc X",
              "baseKind": "TaskList",
              "defaultInput": "input X",
              "isBuiltIn": false,
              "createdAt": "2026-01-01T00:00:00Z",
              "updatedAt": "2026-01-01T00:00:00Z"
            }
            """;
        File.WriteAllText(Path.Combine(_legacyDir, "tpl-x.json"), oldJson);

        var migrator = CreateMigrator();

        var first = await migrator.MigrateAsync();
        Assert.Equal(1, first);

        var second = await migrator.MigrateAsync();
        Assert.Equal(0, second); // Nothing new to migrate.

        var newStore = new JsonTaskGraphStore(_newDir);
        var templates = await newStore.ListTemplatesAsync();
        Assert.Single(templates);
    }
}
