using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.TaskGraph;
using Xunit;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Tests;

public sealed class JsonTaskGraphStoreInstantiatorIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public JsonTaskGraphStoreInstantiatorIntegrationTests()
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

    private JsonTaskGraphStore CreateStore()
        => new(_tempDir, new TaskGraphTemplateInstantiator());

    // ── Test 18: Store delegates to instantiator and persists ──
    [Fact]
    public async Task JsonTaskGraphStore_InstantiateTemplateAsync_DelegatesToInstantiator_AndPersists()
    {
        var store = CreateStore();

        var template = new TaskGraphModel
        {
            Id = "tpl-delegate",
            Name = "Delegate Template",
            DocumentKind = TaskGraphDocumentKind.Template,
            SourceContent = "original",
            ProjectId = "proj-1",
        };
        var node = new TaskNode
        {
            Id = "n1",
            Title = "Node 1",
            Status = TaskNodeStatus.Failed,
            OutputSummary = "old output",
        };
        template.Nodes.Add(node);
        await store.SaveAsync(template);

        var runtime = await store.InstantiateTemplateAsync(
            "tpl-delegate",
            new TemplateInstantiationOptions
            {
                RuntimeGraphName = "Delegated Instance",
                UserInput = "user input",
            });

        // Verify returned graph
        Assert.Equal(TaskGraphDocumentKind.Runtime, runtime.DocumentKind);
        Assert.Equal("Delegated Instance", runtime.Name);
        Assert.Equal("user input", runtime.SourceContent);
        Assert.Equal("tpl-delegate", runtime.BasedOnTemplateId);

        // Verify persisted
        var persisted = await store.LoadAsync(runtime.Id);
        Assert.NotNull(persisted);
        Assert.Equal(runtime.Id, persisted.Id);
        Assert.Equal(TaskGraphDocumentKind.Runtime, persisted.DocumentKind);

        // Verify template unchanged on disk
        var original = await store.LoadTemplateAsync("tpl-delegate");
        Assert.NotNull(original);
        Assert.Equal(TaskGraphDocumentKind.Template, original.DocumentKind);
        Assert.Equal("original", original.SourceContent);
        Assert.Single(original.Nodes);
        Assert.Equal("n1", original.Nodes[0].Id);
        Assert.Equal(TaskNodeStatus.Failed, original.Nodes[0].Status);
    }

    // ── Test 19: Store instantiation with dynamic zones generates persisted runtime nodes ──
    [Fact]
    public async Task JsonTaskGraphStore_InstantiateTemplateAsync_WithDynamicZones_GeneratesPersistedRuntimeNodes()
    {
        var store = CreateStore();

        var template = new TaskGraphModel
        {
            Id = "tpl-dynamic",
            Name = "Dynamic Template",
            DocumentKind = TaskGraphDocumentKind.Template,
            TemplateMetadata = new TaskGraphTemplateMetadata
            {
                AllowDynamicExpansion = true,
                DynamicZones =
                [
                    new DynamicZoneDefinition
                    {
                        Id = "zone_1",
                        Name = "Tasks",
                        AnchorNodeId = "fixed-node",
                        MaxGeneratedNodeCount = 20,
                        GenerationInstruction = "Execute each task line",
                    },
                ],
            },
        };
        template.Nodes.Add(new TaskNode { Id = "fixed-node", Title = "Fixed", Kind = TaskNodeKind.Plan });
        await store.SaveAsync(template);

        var runtime = await store.InstantiateTemplateAsync(
            "tpl-dynamic",
            new TemplateInstantiationOptions { UserInput = "- task1\n- task2\n- task3" });

        // 1 fixed + 3 generated = 4 nodes persisted
        Assert.Equal(4, runtime.Nodes.Count);
        var generatedNodes = runtime.Nodes
            .Where(n => n.Id.StartsWith("dyn_zone_1_", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(3, generatedNodes.Count);
        Assert.Contains(generatedNodes, n => n.Title == "task1");
        Assert.Contains(generatedNodes, n => n.Title == "task2");
        Assert.Contains(generatedNodes, n => n.Title == "task3");

        // Verify persisted instance
        var persistedRuntime = await store.LoadAsync(runtime.Id);
        Assert.NotNull(persistedRuntime);
        Assert.Equal(4, persistedRuntime.Nodes.Count);
        Assert.Equal(TaskGraphDocumentKind.Runtime, persistedRuntime.DocumentKind);

        // Verify template on disk NOT polluted
        var originalTemplate = await store.LoadTemplateAsync("tpl-dynamic");
        Assert.NotNull(originalTemplate);
        Assert.Single(originalTemplate.Nodes);
        Assert.Equal("fixed-node", originalTemplate.Nodes[0].Id);
        Assert.Equal(TaskGraphDocumentKind.Template, originalTemplate.DocumentKind);
    }
}
