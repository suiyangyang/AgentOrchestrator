using System.Linq;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.TaskGraph;
using Xunit;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Tests;

public sealed class TaskGraphFactoryEnsureDefaultNodeTests
{
    [Fact]
    public void EmptyRuntimeGraph_GetsDefaultExecuteNode()
    {
        var graph = new TaskGraphModel
        {
            Name = "空白图",
            DocumentKind = TaskGraphDocumentKind.Runtime,
        };
        Assert.Empty(graph.Nodes);

        var added = TaskGraphFactory.EnsureAtLeastOneExecuteNode(graph);

        Assert.NotNull(added);
        Assert.Single(graph.Nodes);
        Assert.Equal(TaskNodeKind.Execute, added!.Kind);
        Assert.Equal("新任务", added.Title);
        Assert.Equal(TaskNodeStatus.Pending, added.Status);
        Assert.StartsWith("default_", added.Id);
    }

    [Fact]
    public void RuntimeGraphWithExistingNodes_IsLeftUntouched()
    {
        var graph = new TaskGraphModel
        {
            Name = "已存在节点",
            DocumentKind = TaskGraphDocumentKind.Runtime,
        };
        graph.Nodes.Add(new TaskNode { Id = "existing", Title = "已有节点" });

        var result = TaskGraphFactory.EnsureAtLeastOneExecuteNode(graph);

        Assert.Null(result);
        Assert.Single(graph.Nodes);
        Assert.Equal("existing", graph.Nodes[0].Id);
    }

    [Fact]
    public void EmptyTemplateGraph_IsNotModified()
    {
        // Templates are inert skeletons — never auto-injected with a default node.
        var graph = new TaskGraphModel
        {
            Name = "内置模板",
            DocumentKind = TaskGraphDocumentKind.Template,
        };

        var result = TaskGraphFactory.EnsureAtLeastOneExecuteNode(graph);

        Assert.Null(result);
        Assert.Empty(graph.Nodes);
    }

    [Fact]
    public void DefaultNodeId_IsAlwaysDefault1ForEmptyGraph()
    {
        // Verify that the default node ID is consistently "default_1"
        // for every empty runtime graph (stable ID generation).
        for (int i = 0; i < 3; i++)
        {
            var graph = new TaskGraphModel
            {
                Name = $"g{i}",
                DocumentKind = TaskGraphDocumentKind.Runtime,
            };
            var added = TaskGraphFactory.EnsureAtLeastOneExecuteNode(graph);

            Assert.NotNull(added);
            Assert.Equal("default_1", added!.Id);
            Assert.StartsWith("default_", added.Id);
            Assert.Single(graph.Nodes);
        }
    }
}
