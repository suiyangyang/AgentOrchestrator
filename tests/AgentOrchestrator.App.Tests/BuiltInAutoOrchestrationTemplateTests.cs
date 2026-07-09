using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.TaskGraph;
using Xunit;

namespace AgentOrchestrator.App.Tests;

public sealed class BuiltInAutoOrchestrationTemplateTests : IDisposable
{
    private readonly string _tempDir;

    public BuiltInAutoOrchestrationTemplateTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AgentOrchestratorTests_" + Guid.NewGuid().ToString("N"));
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

    // ── Test: Auto-orchestration template is seeded with correct shape ──
    [Fact]
    public async Task BuiltInTemplateSeeder_SeedsBuiltinAutoOrchestrationTemplate_IfMissing()
    {
        var store = new JsonTaskGraphStore(_tempDir);
        var seeder = new BuiltInTemplateSeeder(store);

        await seeder.SeedIfMissingAsync();

        var templates = await store.ListTemplatesAsync();
        var template = templates.FirstOrDefault(t => t.Id == "builtin.auto-orchestration");

        Assert.NotNull(template);

        var graph = await store.LoadTemplateAsync("builtin.auto-orchestration");
        Assert.NotNull(graph);
        Assert.True(graph.IsBuiltInTemplate);
        Assert.Equal(TaskGraphDocumentKind.Template, graph.DocumentKind);
        Assert.NotNull(graph.TemplateMetadata);
        Assert.True(graph.TemplateMetadata.AllowDynamicExpansion);
        Assert.NotEmpty(graph.TemplateMetadata.DynamicZones);
        Assert.Contains(graph.TemplateMetadata.FixedNodeIds, id => id == "auto_input");
    }

    // ── Test: Auto-orchestration seeder is idempotent ──
    [Fact]
    public async Task BuiltInTemplateSeeder_AutoOrchestrationIsIdempotent()
    {
        var store = new JsonTaskGraphStore(_tempDir);
        var seeder = new BuiltInTemplateSeeder(store);

        await seeder.SeedIfMissingAsync();
        var firstCount = (await store.ListTemplatesAsync()).Count(t => t.Id == "builtin.auto-orchestration");

        await seeder.SeedIfMissingAsync();
        var secondCount = (await store.ListTemplatesAsync()).Count(t => t.Id == "builtin.auto-orchestration");

        Assert.Equal(1, firstCount);
        Assert.Equal(1, secondCount);
    }

    // ── Test: User modification of auto-orch template survives re-seed ──
    [Fact]
    public async Task BuiltInTemplateSeeder_AutoOrchestrationSurvivesUserModification()
    {
        var store = new JsonTaskGraphStore(_tempDir);
        var seeder = new BuiltInTemplateSeeder(store);

        await seeder.SeedIfMissingAsync();

        // Modify the seeded template.
        var graph = await store.LoadTemplateAsync("builtin.auto-orchestration");
        Assert.NotNull(graph);
        var originalName = graph.Name;
        graph.Name = "用户修改的名称";
        await store.SaveAsync(graph);

        // Re-seed — should skip because the template already exists.
        await seeder.SeedIfMissingAsync();

        var reloaded = await store.LoadTemplateAsync("builtin.auto-orchestration");
        Assert.NotNull(reloaded);
        Assert.Equal("用户修改的名称", reloaded.Name);
        Assert.NotEqual(originalName, reloaded.Name);
    }
}
