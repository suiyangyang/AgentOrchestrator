using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.TaskGraph;
using Xunit;

namespace AgentOrchestrator.App.Tests;

public sealed class BuiltInTemplateSeederTests : IDisposable
{
    private readonly string _tempDir;

    public BuiltInTemplateSeederTests()
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

    // ── Test 11 ──
    [Fact]
    public async Task SeedIfMissingAsync_Creates4BuiltInTemplates_IfNoneExist()
    {
        var store = new JsonTaskGraphStore(_tempDir);
        var seeder = new BuiltInTemplateSeeder(store);

        await seeder.SeedIfMissingAsync();

        var templates = await store.ListTemplatesAsync();
        Assert.Equal(4, templates.Count);

        var ids = templates.Select(x => x.Id).ToHashSet();
        Assert.Contains("builtin.task-list", ids);
        Assert.Contains("builtin.feature-dev", ids);
        Assert.Contains("builtin.bug-list", ids);
        Assert.Contains("builtin.auto-orchestration", ids);

        // Verify each template has proper shape.
        foreach (var id in ids)
        {
            var graph = await store.LoadTemplateAsync(id);
            Assert.NotNull(graph);
            Assert.True(graph.IsBuiltInTemplate);
            Assert.Equal(TaskGraphDocumentKind.Template, graph.DocumentKind);
            Assert.NotNull(graph.TemplateMetadata);
            Assert.NotEmpty(graph.TemplateMetadata.DynamicZones);
        }
    }

    // ── Test 12 ──
    [Fact]
    public async Task SeedIfMissingAsync_IsIdempotent()
    {
        var store = new JsonTaskGraphStore(_tempDir);
        var seeder = new BuiltInTemplateSeeder(store);

        await seeder.SeedIfMissingAsync();
        var firstCount = (await store.ListTemplatesAsync()).Count;

        await seeder.SeedIfMissingAsync();
        var secondCount = (await store.ListTemplatesAsync()).Count;

        Assert.Equal(firstCount, secondCount);
        Assert.Equal(4, secondCount);
    }
}
