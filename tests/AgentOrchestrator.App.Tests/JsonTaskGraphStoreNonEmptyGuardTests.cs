using System;
using System.IO;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.TaskGraph;
using Xunit;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Tests;

public sealed class JsonTaskGraphStoreNonEmptyGuardTests : IDisposable
{
    private readonly string _tempDir;
    public JsonTaskGraphStoreNonEmptyGuardTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NonEmptyGuard_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }
    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    [Fact]
    public async Task SaveAsync_RuntimeGraphWithZeroNodes_ThrowsTaskGraphValidationException()
    {
        var store = new JsonTaskGraphStore(_tempDir);
        var graph = new TaskGraphModel
        {
            Name = "测试空白图",
            DocumentKind = TaskGraphDocumentKind.Runtime,
        };
        // No nodes added.

        await Assert.ThrowsAsync<TaskGraphValidationException>(
            () => store.SaveAsync(graph));
    }

    [Fact]
    public async Task SaveAsync_TemplateGraphWithZeroNodes_DoesNotThrow()
    {
        var store = new JsonTaskGraphStore(_tempDir);
        var graph = new TaskGraphModel
        {
            Name = "内置空白模板",
            DocumentKind = TaskGraphDocumentKind.Template,
        };
        // Templates can legitimately have 0 nodes — they're inert skeletons.

        await store.SaveAsync(graph); // must NOT throw
    }

    [Fact]
    public async Task SaveAsync_RuntimeGraphWithOneNode_DoesNotThrow()
    {
        var store = new JsonTaskGraphStore(_tempDir);
        var graph = new TaskGraphModel
        {
            Name = "单节点图",
            DocumentKind = TaskGraphDocumentKind.Runtime,
        };
        graph.Nodes.Add(new TaskNode { Title = "n1", Kind = TaskNodeKind.Execute });

        await store.SaveAsync(graph); // must NOT throw
    }
}
