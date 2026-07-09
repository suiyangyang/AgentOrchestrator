using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.TaskGraph;
using Xunit;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Tests;

public sealed class TaskGraphStoreUnificationTests : IDisposable
{
    private readonly string _tempDir;

    public TaskGraphStoreUnificationTests()
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

    private static JsonSerializerOptions CreateOptions()
        => new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            Converters = { new JsonStringEnumConverter() },
        };

    private JsonTaskGraphStore CreateStore() => new(_tempDir);

    private static TaskGraphModel CreateGraph(string id, string name, TaskGraphDocumentKind kind)
        => new()
        {
            Id = id,
            Name = name,
            DocumentKind = kind,
        };

    // ── Test 1 ──
    [Fact]
    public async Task ListTemplatesAsync_ReturnsOnlyTemplateDocuments()
    {
        var store = CreateStore();
        await store.SaveAsync(CreateGraph("t1", "Template A", TaskGraphDocumentKind.Template));
        await store.SaveAsync(CreateGraph("t2", "Template B", TaskGraphDocumentKind.Template));
        // Runtime graphs must keep at least one node (SaveAsync guard) — seed
        // the test fixtures with a placeholder so the list test still exercises
        // the store-level filter rather than the validation guard.
        var r1 = CreateGraph("r1", "Runtime A", TaskGraphDocumentKind.Runtime);
        r1.Nodes.Add(new TaskNode { Id = "r1_seed", Title = "seed", Kind = TaskNodeKind.Execute });
        await store.SaveAsync(r1);
        var r2 = CreateGraph("r2", "Runtime B", TaskGraphDocumentKind.Runtime);
        r2.Nodes.Add(new TaskNode { Id = "r2_seed", Title = "seed", Kind = TaskNodeKind.Execute });
        await store.SaveAsync(r2);

        var templates = await store.ListTemplatesAsync();
        var runtimes = await store.ListRuntimeGraphsAsync();

        Assert.Equal(2, templates.Count);
        Assert.Equal(2, runtimes.Count);
        Assert.Contains(templates, x => x.Id == "t1");
        Assert.Contains(templates, x => x.Id == "t2");
        Assert.DoesNotContain(templates, x => x.Id == "r1");
        Assert.DoesNotContain(templates, x => x.Id == "r2");
        Assert.Contains(runtimes, x => x.Id == "r1");
        Assert.Contains(runtimes, x => x.Id == "r2");
        Assert.DoesNotContain(runtimes, x => x.Id == "t1");
        Assert.DoesNotContain(runtimes, x => x.Id == "t2");
    }

    // ── Test 2 ──
    [Fact]
    public async Task LoadTemplateAsync_FindsPrefixedAndLegacyFiles()
    {
        var fileName1 = Path.Combine(_tempDir, "template.abc.json");
        var fileName2 = Path.Combine(_tempDir, "def.json"); // Legacy unprefixed file.

        var options = CreateOptions();
        var templateGraph = new TaskGraphModel { Id = "abc", Name = "X", DocumentKind = TaskGraphDocumentKind.Template };
        var legacyGraph = new TaskGraphModel { Id = "def", Name = "Y" }; // No DocumentKind → defaults to Runtime.

        await File.WriteAllTextAsync(fileName1, JsonSerializer.Serialize(templateGraph, options));
        await File.WriteAllTextAsync(fileName2, JsonSerializer.Serialize(legacyGraph, options));

        var store = CreateStore();
        var loaded1 = await store.LoadTemplateAsync("abc");
        var loaded2 = await store.LoadTemplateAsync("def");

        Assert.NotNull(loaded1);
        Assert.Equal(TaskGraphDocumentKind.Template, loaded1.DocumentKind);
        Assert.Equal("X", loaded1.Name);

        Assert.NotNull(loaded2);
        Assert.Equal("Y", loaded2.Name);
    }

    // ── Test 3 ──
    [Fact]
    public async Task DeleteAsync_RemovesPrefixedAndLegacyFiles()
    {
        var options = CreateOptions();
        var templateFile = Path.Combine(_tempDir, "template.x.json");
        var runtimeFile = Path.Combine(_tempDir, "runtime.x.json");
        var legacyFile = Path.Combine(_tempDir, "x.json");

        var graph = new TaskGraphModel { Id = "x", Name = "DeleteMe" };
        var json = JsonSerializer.Serialize(graph, options);

        await File.WriteAllTextAsync(templateFile, json);
        await File.WriteAllTextAsync(runtimeFile, json);
        await File.WriteAllTextAsync(legacyFile, json);

        var store = CreateStore();
        await store.DeleteAsync("x");

        Assert.False(File.Exists(templateFile));
        Assert.False(File.Exists(runtimeFile));
        Assert.False(File.Exists(legacyFile));
    }

    // ── Test 4 ──
    [Fact]
    public async Task SaveAsync_UsesPrefixedFileName()
    {
        var store = CreateStore();

        var template = CreateGraph("tpl1", "MyTemplate", TaskGraphDocumentKind.Template);
        await store.SaveAsync(template);
        Assert.True(File.Exists(Path.Combine(_tempDir, "template.tpl1.json")));

        var runtime = CreateGraph("run1", "MyRuntime", TaskGraphDocumentKind.Runtime);
        // Runtime graphs must keep at least one node (SaveAsync guard).
        runtime.Nodes.Add(new TaskNode { Id = "run1_seed", Title = "seed", Kind = TaskNodeKind.Execute });
        await store.SaveAsync(runtime);
        Assert.True(File.Exists(Path.Combine(_tempDir, "runtime.run1.json")));
    }

    // ── Test 5 ──
    [Fact]
    public async Task InstantiateTemplateAsync_ProducesFreshRuntimeWithClearedState()
    {
        var store = CreateStore();

        var template = new TaskGraphModel
        {
            Id = "tpl-inst",
            Name = "TemplateWithState",
            DocumentKind = TaskGraphDocumentKind.Template,
            ProjectId = "proj-1",
            ProjectName = "Test Project",
            SourceContent = "original content",
            ExecutionState = TaskGraphExecutionState.Draft,
        };

        var node1 = new TaskNode
        {
            Id = "n1",
            Title = "Node 1",
            Status = TaskNodeStatus.Failed,
            AttemptCount = 2,
            AgentSessionId = "s1",
            OutputSummary = "out",
            RawOutput = "raw",
        };
        node1.TouchedFiles.Add("file1.cs");
        node1.ResultTags.Add("tag1");
        node1.DependsOn.Add("n0"); // dependency on non-existent node — should be preserved id-wise

        var node2 = new TaskNode
        {
            Id = "n2",
            Title = "Node 2",
            Status = TaskNodeStatus.Completed,
        };
        node2.DependsOn.Add("n1");

        template.Nodes.Add(node1);
        template.Nodes.Add(node2);
        await store.SaveAsync(template);

        // Instantiate.
        var runtime = await store.InstantiateTemplateAsync(
            "tpl-inst",
            new TemplateInstantiationOptions
            {
                RuntimeGraphName = "新实例",
                UserInput = "用户输入",
            });

        // Graph-level assertions.
        Assert.Equal(TaskGraphDocumentKind.Runtime, runtime.DocumentKind);
        Assert.Equal("tpl-inst", runtime.BasedOnTemplateId);
        Assert.Equal("新实例", runtime.Name);
        Assert.Equal("用户输入", runtime.SourceContent);
        Assert.Equal(TaskGraphExecutionState.Draft, runtime.ExecutionState);
        Assert.Null(runtime.ExecutionStartedAt);
        Assert.Null(runtime.ExecutionCompletedAt);
        Assert.Null(runtime.ConversationSessionId);
        Assert.False(runtime.IsCheckpointPending);
        Assert.Null(runtime.ActiveCheckpointNodeId);
        Assert.NotEqual("tpl-inst", runtime.Id);

        // Node-level: fresh ids.
        var oldIds = new HashSet<string> { "n1", "n2" };
        Assert.Equal(2, runtime.Nodes.Count);
        foreach (var node in runtime.Nodes)
        {
            Assert.DoesNotContain(node.Id, oldIds);
        }

        // Node-level: cleared state.
        foreach (var node in runtime.Nodes)
        {
            Assert.Equal(TaskNodeStatus.Pending, node.Status);
            Assert.Equal(0, node.AttemptCount);
            Assert.Null(node.AgentSessionId);
            Assert.Null(node.OutputSummary);
            Assert.Null(node.RawOutput);
            Assert.Null(node.StartedAt);
            Assert.Null(node.CompletedAt);
            Assert.Null(node.StructuredSummary);
            Assert.Empty(node.TouchedFiles);
            Assert.Empty(node.ResultTags);
        }

        // Verify persisted.
        var persisted = await store.LoadAsync(runtime.Id);
        Assert.NotNull(persisted);
        Assert.Equal(runtime.Id, persisted.Id);

        // Verify original template unchanged.
        var original = await store.LoadTemplateAsync("tpl-inst");
        Assert.NotNull(original);
        Assert.Equal(TaskGraphDocumentKind.Template, original.DocumentKind);
        Assert.Equal("original content", original.SourceContent);
        Assert.Contains(original.Nodes, n => n.Id == "n1" && n.Status == TaskNodeStatus.Failed);
    }

    // ── Test 6 ──
    [Fact]
    public async Task InstantiateTemplateAsync_ThrowsIfTemplateNotFound()
    {
        var store = CreateStore();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.InstantiateTemplateAsync("nonexistent", new TemplateInstantiationOptions()));
    }

    // ── Test 7 ──
    [Fact]
    public async Task InstantiateTemplateAsync_ThrowsIfDocumentIsNotTemplate()
    {
        var store = CreateStore();
        var runtime = CreateGraph("run-x", "Runtime", TaskGraphDocumentKind.Runtime);
        // Runtime graphs must keep at least one node (SaveAsync guard).
        runtime.Nodes.Add(new TaskNode { Id = "run_x_seed", Title = "seed", Kind = TaskNodeKind.Execute });
        await store.SaveAsync(runtime);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.InstantiateTemplateAsync("run-x", new TemplateInstantiationOptions()));
    }
}
