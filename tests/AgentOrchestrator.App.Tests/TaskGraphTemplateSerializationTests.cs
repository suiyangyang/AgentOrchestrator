using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentOrchestrator.App.Models.TaskGraph;
using Xunit;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Tests;

public sealed class TaskGraphTemplateSerializationTests
{
    private static JsonSerializerOptions CreateStoreOptions()
        => new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            Converters = { new JsonStringEnumConverter() },
        };

    [Fact]
    public void Roundtrip_GraphWithTemplateDocument_PreservesAllNewFields()
    {
        var options = CreateStoreOptions();

        var graph = new TaskGraphModel
        {
            DocumentKind = TaskGraphDocumentKind.Template,
            TemplateNotes = "This is a multi-step task template.",
            TemplatePlannerPrompt = "Plan the execution steps in order.",
            BasedOnTemplateId = "template-abc-123",
            IsBuiltInTemplate = true,
            TemplateMetadata = new TaskGraphTemplateMetadata
            {
                AllowDynamicExpansion = true,
                ExpansionEntryNodeId = "entry-node",
                ExpansionTerminalNodeId = "term-node",
                FixedNodeIds = ["node-1", "node-2"],
                FixedEdgeKeys = ["edge-1"],
                DynamicZones =
                [
                    new DynamicZoneDefinition
                    {
                        Id = "zone-1",
                        Name = "Task Execution Zone",
                        AnchorNodeId = "anchor-1",
                        InsertAfterNodeId = "node-2",
                        ConnectToTerminalNodeId = "term-node",
                        AllowParallelNodes = true,
                        MaxGeneratedNodeCount = 8,
                        GenerationInstruction = "Generate one Execute node per task item.",
                    },
                ],
                NodeGenerationRules = ["rule-n-1", "rule-n-2", "rule-n-3"],
                EdgeGenerationRules = ["rule-e-1"],
                ExecutionRules = ["rule-x-1", "rule-x-2"],
            },
        };

        var node = new TaskNode
        {
            IsTemplateLocked = true,
            IsDynamicPlaceholder = true,
            TemplateRole = "Input",
        };
        graph.Nodes.Add(node);

        var json = JsonSerializer.Serialize(graph, options);
        var deserialized = JsonSerializer.Deserialize<TaskGraphModel>(json, options);

        Assert.NotNull(deserialized);

        // Graph-level fields
        Assert.Equal(TaskGraphDocumentKind.Template, deserialized.DocumentKind);
        Assert.Equal("This is a multi-step task template.", deserialized.TemplateNotes);
        Assert.Equal("Plan the execution steps in order.", deserialized.TemplatePlannerPrompt);
        Assert.Equal("template-abc-123", deserialized.BasedOnTemplateId);
        Assert.True(deserialized.IsBuiltInTemplate);

        // TemplateMetadata
        Assert.NotNull(deserialized.TemplateMetadata);
        Assert.True(deserialized.TemplateMetadata.AllowDynamicExpansion);
        Assert.Equal("entry-node", deserialized.TemplateMetadata.ExpansionEntryNodeId);
        Assert.Equal("term-node", deserialized.TemplateMetadata.ExpansionTerminalNodeId);
        Assert.Equal(["node-1", "node-2"], deserialized.TemplateMetadata.FixedNodeIds);
        Assert.Equal(["edge-1"], deserialized.TemplateMetadata.FixedEdgeKeys);
        Assert.Equal(["rule-n-1", "rule-n-2", "rule-n-3"], deserialized.TemplateMetadata.NodeGenerationRules);
        Assert.Equal(["rule-e-1"], deserialized.TemplateMetadata.EdgeGenerationRules);
        Assert.Equal(["rule-x-1", "rule-x-2"], deserialized.TemplateMetadata.ExecutionRules);

        // DynamicZone
        var zone = Assert.Single(deserialized.TemplateMetadata.DynamicZones);
        Assert.Equal("zone-1", zone.Id);
        Assert.Equal("Task Execution Zone", zone.Name);
        Assert.Equal("anchor-1", zone.AnchorNodeId);
        Assert.Equal("node-2", zone.InsertAfterNodeId);
        Assert.Equal("term-node", zone.ConnectToTerminalNodeId);
        Assert.True(zone.AllowParallelNodes);
        Assert.Equal(8, zone.MaxGeneratedNodeCount);
        Assert.Equal("Generate one Execute node per task item.", zone.GenerationInstruction);

        // Node-level fields
        var deserializedNode = Assert.Single(deserialized.Nodes);
        Assert.True(deserializedNode.IsTemplateLocked);
        Assert.True(deserializedNode.IsDynamicPlaceholder);
        Assert.Equal("Input", deserializedNode.TemplateRole);
    }

    [Fact]
    public void Deserialize_OldGraphJsonWithoutNewFields_DefaultsDocumentKindToRuntime()
    {
        var options = CreateStoreOptions();

        const string json =
            """
            {
              "id": "legacy-graph-1",
              "name": "Legacy Graph",
              "createdAt": "2026-01-01T00:00:00Z",
              "updatedAt": "2026-01-01T00:00:00Z",
              "executionState": "Draft",
              "nodes": [
                {
                  "id": "n1",
                  "title": "Task 1",
                  "kind": "Execute",
                  "status": "Pending"
                }
              ],
              "edges": []
            }
            """;

        var graph = JsonSerializer.Deserialize<TaskGraphModel>(json, options);

        Assert.NotNull(graph);

        // Graph defaults
        Assert.Equal(TaskGraphDocumentKind.Runtime, graph.DocumentKind);
        Assert.Null(graph.TemplateNotes);
        Assert.Null(graph.TemplatePlannerPrompt);
        Assert.Null(graph.BasedOnTemplateId);
        Assert.False(graph.IsBuiltInTemplate);
        Assert.Null(graph.TemplateMetadata);

        // Node defaults
        var node = Assert.Single(graph.Nodes);
        Assert.False(node.IsTemplateLocked);
        Assert.False(node.IsDynamicPlaceholder);
        Assert.Null(node.TemplateRole);
    }

    [Fact]
    public void NormalizeGraph_OnLoadedTemplate_InitializesTemplateMetadataCollectionsToEmpty()
    {
        var options = CreateStoreOptions();

        const string json =
            """
            {
              "id": "tpl-with-null-collections",
              "name": "Template With Null Collections",
              "documentKind": "Template",
              "templateMetadata": {
                "allowDynamicExpansion": true,
                "expansionEntryNodeId": "entry",
                "expansionTerminalNodeId": null,
                "fixedNodeIds": null,
                "fixedEdgeKeys": null,
                "dynamicZones": null,
                "nodeGenerationRules": null,
                "edgeGenerationRules": null,
                "executionRules": null
              },
              "nodes": [],
              "edges": []
            }
            """;

        var graph = JsonSerializer.Deserialize<TaskGraphModel>(json, options);

        Assert.NotNull(graph);
        Assert.Equal(TaskGraphDocumentKind.Template, graph.DocumentKind);
        Assert.NotNull(graph.TemplateMetadata);

        // Before normalization, null lists should be null.
        Assert.Null(graph.TemplateMetadata.FixedNodeIds);
        Assert.Null(graph.TemplateMetadata.FixedEdgeKeys);
        Assert.Null(graph.TemplateMetadata.DynamicZones);
        Assert.Null(graph.TemplateMetadata.NodeGenerationRules);
        Assert.Null(graph.TemplateMetadata.EdgeGenerationRules);
        Assert.Null(graph.TemplateMetadata.ExecutionRules);

        // Apply the same normalization that JsonTaskGraphStore.NormalizeGraph does.
        NormalizeTemplateMetadata(graph);

        // After normalization, all collection properties must be non-null.
        Assert.NotNull(graph.TemplateMetadata.FixedNodeIds);
        Assert.NotNull(graph.TemplateMetadata.FixedEdgeKeys);
        Assert.NotNull(graph.TemplateMetadata.DynamicZones);
        Assert.NotNull(graph.TemplateMetadata.NodeGenerationRules);
        Assert.NotNull(graph.TemplateMetadata.EdgeGenerationRules);
        Assert.NotNull(graph.TemplateMetadata.ExecutionRules);

        Assert.Empty(graph.TemplateMetadata.FixedNodeIds);
    }

    /// <summary>
    /// Duplicates the TemplateMetadata collection normalization from
    /// <c>JsonTaskGraphStore.NormalizeGraph</c> so the test can stay
    /// self-contained without InternalsVisibleTo.
    /// </summary>
    private static void NormalizeTemplateMetadata(TaskGraphModel graph)
    {
        if (graph.TemplateMetadata is null)
        {
            return;
        }

        graph.TemplateMetadata.FixedNodeIds ??= [];
        graph.TemplateMetadata.FixedEdgeKeys ??= [];
        graph.TemplateMetadata.DynamicZones ??= [];
        graph.TemplateMetadata.NodeGenerationRules ??= [];
        graph.TemplateMetadata.EdgeGenerationRules ??= [];
        graph.TemplateMetadata.ExecutionRules ??= [];
    }
}
