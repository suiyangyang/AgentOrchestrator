using System.Linq;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.TaskGraph;
using Xunit;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Tests;

public sealed class TaskGraphTemplateBuilderTemplateShapeTests
{
    // ── Test 13 ──
    [Fact]
    public void Build_TaskList_WithTemplateDocumentKind_HasTemplateMetadataWithDynamicZone()
    {
        var graph = TaskGraphTemplateBuilder.BuildTaskListGraph(
            "- foo\n- bar",
            TaskGraphDocumentKind.Template);

        Assert.Equal(TaskGraphDocumentKind.Template, graph.DocumentKind);
        Assert.True(graph.IsBuiltInTemplate);
        Assert.NotNull(graph.TemplateNotes);
        Assert.NotEmpty(graph.TemplateNotes);
        Assert.NotNull(graph.TemplateMetadata);
        Assert.NotEmpty(graph.TemplateMetadata.DynamicZones);

        var zone = Assert.Single(graph.TemplateMetadata.DynamicZones);
        Assert.Equal("任务执行区", zone.Name);
        Assert.NotEmpty(zone.AnchorNodeId);
        Assert.NotEmpty(zone.GenerationInstruction);
        Assert.True(zone.MaxGeneratedNodeCount > 0);

        // At least one fixed node id.
        Assert.NotEmpty(graph.TemplateMetadata.FixedNodeIds);

        // All nodes are locked.
        Assert.All(graph.Nodes, n => Assert.True(n.IsTemplateLocked));
    }

    // ── Test 14 ──
    [Fact]
    public void Build_TaskList_WithRuntimeDocumentKind_DoesNotSetTemplateMetadata()
    {
        var graph = TaskGraphTemplateBuilder.BuildTaskListGraph(
            "- foo\n- bar",
            TaskGraphDocumentKind.Runtime);

        Assert.Equal(TaskGraphDocumentKind.Runtime, graph.DocumentKind);
        Assert.False(graph.IsBuiltInTemplate);
        Assert.Null(graph.TemplateNotes);
        Assert.Null(graph.TemplateMetadata);

        // Nodes should NOT be locked.
        Assert.All(graph.Nodes, n => Assert.False(n.IsTemplateLocked));
    }

    // ── Test 15 ──
    [Fact]
    public void Build_AllKinds_WithTemplateDocumentKind_ProduceValidTemplateGraphs()
    {
        CheckKind(TaskGraphTemplateKind.TaskList, "- foo\n- bar");
        CheckKind(TaskGraphTemplateKind.FeatureDevelopment, "build a login page");
        CheckKind(TaskGraphTemplateKind.BugList, "- bug 1\n- bug 2");
        CheckKind(TaskGraphTemplateKind.Custom, "some custom input");
    }

    private static void CheckKind(TaskGraphTemplateKind kind, string input)
    {
        var graph = TaskGraphTemplateBuilder.Build(kind, input, TaskGraphDocumentKind.Template);

        Assert.Equal(TaskGraphDocumentKind.Template, graph.DocumentKind);
        Assert.NotNull(graph.TemplateMetadata);
        Assert.NotEmpty(graph.Nodes);
    }
}
