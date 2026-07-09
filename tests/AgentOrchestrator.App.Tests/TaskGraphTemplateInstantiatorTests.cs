using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.TaskGraph;
using Xunit;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Tests;

public sealed class TaskGraphTemplateInstantiatorTests
{
    private readonly ITaskGraphTemplateInstantiator _instantiator = new TaskGraphTemplateInstantiator();

    private static TaskGraphModel CreateTemplate(string id = "tpl-1", string name = "Test Template")
        => new()
        {
            Id = id,
            Name = name,
            DocumentKind = TaskGraphDocumentKind.Template,
            SourceContent = "original content",
            ProjectId = "proj-1",
            ProjectName = "Test Project",
        };

    private static TaskNode CreateNode(string id, string title, TaskNodeKind kind = TaskNodeKind.Execute)
        => new()
        {
            Id = id,
            Title = title,
            Kind = kind,
            Status = TaskNodeStatus.Pending,
        };

    // ── Test 1: Null template throws ──
    [Fact]
    public async Task InstantiateAsync_NullTemplate_ThrowsInvalidOperationException()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _instantiator.InstantiateAsync(null!, new TemplateInstantiationOptions()));
        Assert.Contains("null", ex.Message);
    }

    // ── Test 2: Non-template document throws ──
    [Fact]
    public async Task InstantiateAsync_NonTemplateDocument_ThrowsInvalidOperationException()
    {
        var runtime = new TaskGraphModel
        {
            Id = "r1",
            Name = "Runtime",
            DocumentKind = TaskGraphDocumentKind.Runtime,
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _instantiator.InstantiateAsync(runtime, new TemplateInstantiationOptions()));
        Assert.Contains("not a template", ex.Message);
    }

    // ── Test 3: Basic fields and state clear ──
    [Fact]
    public async Task InstantiateAsync_BasicFieldsAndStateClear()
    {
        var template = CreateTemplate();
        var n1 = CreateNode("n1", "Node 1");
        n1.Status = TaskNodeStatus.Failed;
        n1.AttemptCount = 2;
        n1.AgentSessionId = "s1";
        n1.OutputSummary = "out";
        n1.RawOutput = "raw";
        n1.TouchedFiles.Add("file1.cs");
        n1.ResultTags.Add("tag1");

        var n2 = CreateNode("n2", "Node 2");
        n2.DependsOn.Add("n1");

        template.Nodes.Add(n1);
        template.Nodes.Add(n2);

        var clone = await _instantiator.InstantiateAsync(template, new TemplateInstantiationOptions());

        // Graph-level
        Assert.NotEqual(template.Id, clone.Id);
        Assert.Equal(TaskGraphDocumentKind.Runtime, clone.DocumentKind);
        Assert.Equal("Test Template", template.Name);
        Assert.True(clone.Name.Contains("实例"), "Default name should contain 实例");
        Assert.Equal("original content", clone.SourceContent);
        Assert.Equal(TaskGraphExecutionState.Draft, clone.ExecutionState);
        Assert.Null(clone.ExecutionStartedAt);
        Assert.Null(clone.ExecutionCompletedAt);
        Assert.Null(clone.ConversationSessionId);
        Assert.False(clone.IsCheckpointPending);
        Assert.Null(clone.ActiveCheckpointNodeId);
        Assert.Equal(template.Id, clone.BasedOnTemplateId);
        Assert.False(clone.IsBuiltInTemplate);

        // Node-level: fresh ids
        Assert.Equal(2, clone.Nodes.Count);
        var oldIds = new HashSet<string> { "n1", "n2" };
        foreach (var node in clone.Nodes)
        {
            Assert.DoesNotContain(node.Id, oldIds);
        }

        // Node-level: cleared state
        foreach (var node in clone.Nodes)
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

        // Template itself is unchanged
        Assert.Equal(TaskGraphDocumentKind.Template, template.DocumentKind);
        Assert.Equal("n1", template.Nodes[0].Id);
        Assert.Equal(TaskNodeStatus.Failed, template.Nodes[0].Status);
    }

    // ── Test 4: RuntimeGraphName from options ──
    [Fact]
    public async Task InstantiateAsync_SetsRuntimeGraphNameFromOptions()
    {
        var template = CreateTemplate();
        template.Nodes.Add(CreateNode("n1", "Node 1"));

        var clone1 = await _instantiator.InstantiateAsync(template,
            new TemplateInstantiationOptions { RuntimeGraphName = "My New Graph" });
        Assert.Equal("My New Graph", clone1.Name);

        var clone2 = await _instantiator.InstantiateAsync(template,
            new TemplateInstantiationOptions());
        Assert.Equal("Test Template - 实例", clone2.Name);
    }

    // ── Test 5: SourceContent from options.UserInput ──
    [Fact]
    public async Task InstantiateAsync_SetsSourceContentFromOptions_UserInput()
    {
        var template = CreateTemplate();
        template.Nodes.Add(CreateNode("n1", "Node 1"));

        var clone1 = await _instantiator.InstantiateAsync(template,
            new TemplateInstantiationOptions { UserInput = "my custom input" });
        Assert.Equal("my custom input", clone1.SourceContent);

        var clone2 = await _instantiator.InstantiateAsync(template,
            new TemplateInstantiationOptions());
        Assert.Equal("original content", clone2.SourceContent);
    }

    // ── Test 6: No DynamicZones → no new nodes ──
    [Fact]
    public async Task InstantiateAsync_TemplateWithNoDynamicZones_GeneratesNoNewNodes()
    {
        var template = CreateTemplate();
        template.Nodes.Add(CreateNode("n1", "Node 1"));
        // No TemplateMetadata — defaults to null.

        var clone = await _instantiator.InstantiateAsync(template,
            new TemplateInstantiationOptions { UserInput = "line1\nline2\nline3" });
        Assert.Single(clone.Nodes);
    }

    // ── Test 7: AllowDynamicExpansion = false → no expansion ──
    [Fact]
    public async Task InstantiateAsync_DynamicZoneDisabled_DoesNotExpand()
    {
        var template = CreateTemplate();
        template.Nodes.Add(CreateNode("n1", "Node 1"));
        template.TemplateMetadata = new TaskGraphTemplateMetadata
        {
            AllowDynamicExpansion = false,
            DynamicZones =
            [
                new DynamicZoneDefinition
                {
                    AnchorNodeId = "n1",
                    MaxGeneratedNodeCount = 20,
                },
            ],
        };

        var clone = await _instantiator.InstantiateAsync(template,
            new TemplateInstantiationOptions { UserInput = "line1\nline2\nline3" });
        Assert.Single(clone.Nodes);
    }

    // ── Test 8: Basic expansion — one node per input line ──
    [Fact]
    public async Task InstantiateAsync_TaskListStyleExpansion_GeneratesOneExecuteNodePerLine()
    {
        var template = CreateTemplate();
        template.Nodes.Add(CreateNode("input", "Input"));
        template.TemplateMetadata = new TaskGraphTemplateMetadata
        {
            AllowDynamicExpansion = true,
            FixedNodeIds = ["input"],
            DynamicZones =
            [
                new DynamicZoneDefinition
                {
                    Id = "zone_a",
                    Name = "Task Zone",
                    AnchorNodeId = "input",
                    MaxGeneratedNodeCount = 20,
                    GenerationInstruction = "process each line",
                },
            ],
        };

        var clone = await _instantiator.InstantiateAsync(template,
            new TemplateInstantiationOptions { UserInput = "- foo\n- bar\n- baz" });

        // 1 original (id rewritten) + 3 generated = 4 nodes
        Assert.Equal(4, clone.Nodes.Count);

        var generatedNodes = clone.Nodes.Where(n => n.Id.StartsWith("dyn_zone_a_", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, generatedNodes.Count);
        Assert.Contains(generatedNodes, n => n.Title == "foo");
        Assert.Contains(generatedNodes, n => n.Title == "bar");
        Assert.Contains(generatedNodes, n => n.Title == "baz");
        Assert.All(generatedNodes, n => Assert.Equal(TaskNodeKind.Execute, n.Kind));

        // Chain: dyn_1 depends on input, dyn_2 on dyn_1, dyn_3 on dyn_2
        var dyn1 = generatedNodes.First(n => n.Title == "foo");
        var dyn2 = generatedNodes.First(n => n.Title == "bar");
        var dyn3 = generatedNodes.First(n => n.Title == "baz");

        var inputNode = clone.Nodes.First(n => n.Title == "Input");
        Assert.Contains(inputNode.Id, dyn1.DependsOn);
        Assert.Contains(dyn1.Id, dyn2.DependsOn);
        Assert.Contains(dyn2.Id, dyn3.DependsOn);
    }

    // ── Test 9: MaxGeneratedNodeCount respected ──
    [Fact]
    public async Task InstantiateAsync_DynamicZoneRespectsMaxGeneratedNodeCount()
    {
        var template = CreateTemplate();
        template.Nodes.Add(CreateNode("input", "Input"));
        template.TemplateMetadata = new TaskGraphTemplateMetadata
        {
            AllowDynamicExpansion = true,
            DynamicZones =
            [
                new DynamicZoneDefinition
                {
                    Id = "zone_a",
                    AnchorNodeId = "input",
                    MaxGeneratedNodeCount = 5,
                },
            ],
        };

        var manyLines = string.Join("\n", Enumerable.Range(1, 30).Select(i => $"line {i}"));
        var clone = await _instantiator.InstantiateAsync(template,
            new TemplateInstantiationOptions { UserInput = manyLines });

        var generatedNodes = clone.Nodes.Where(n => n.Id.StartsWith("dyn_zone_a_", StringComparison.Ordinal)).ToList();
        Assert.Equal(5, generatedNodes.Count);
    }

    // ── Test 10a: Terminal wiring (mutable terminal) ──
    [Fact]
    public async Task InstantiateAsync_DynamicZone_ConnectsToTerminal_MutableTerminal()
    {
        var template = CreateTemplate();
        template.Nodes.Add(CreateNode("input", "Input"));
        template.Nodes.Add(CreateNode("terminal", "Terminal"));
        template.TemplateMetadata = new TaskGraphTemplateMetadata
        {
            AllowDynamicExpansion = true,
            DynamicZones =
            [
                new DynamicZoneDefinition
                {
                    Id = "zone_a",
                    AnchorNodeId = "input",
                    ConnectToTerminalNodeId = "terminal",
                    MaxGeneratedNodeCount = 10,
                },
            ],
        };

        var clone = await _instantiator.InstantiateAsync(template,
            new TemplateInstantiationOptions { UserInput = "foo\nbar\nbaz" });

        var generatedNodes = clone.Nodes.Where(n => n.Id.StartsWith("dyn_zone_a_", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, generatedNodes.Count);

        var terminal = clone.Nodes.First(n => n.Title == "Terminal");
        // Terminal should now depend on the last generated node, not "input"
        Assert.DoesNotContain(terminal.DependsOn, d => clone.Nodes.Any(n => n.Id == d && n.Title == "Input"));
        Assert.Contains(generatedNodes[^1].Id, terminal.DependsOn);
    }

    // ── Test 10b: Terminal wiring (fixed terminal) — supplemental edge added ──
    [Fact]
    public async Task InstantiateAsync_DynamicZone_ConnectsToTerminal_FixedTerminal_AddsSupplementalEdge()
    {
        var template = CreateTemplate();
        template.Nodes.Add(CreateNode("input", "Input"));
        var terminal = CreateNode("terminal", "Terminal");
        terminal.DependsOn.Add("input"); // existing dependency
        template.Nodes.Add(terminal);
        template.TemplateMetadata = new TaskGraphTemplateMetadata
        {
            AllowDynamicExpansion = true,
            FixedNodeIds = ["terminal"],
            DynamicZones =
            [
                new DynamicZoneDefinition
                {
                    Id = "zone_a",
                    AnchorNodeId = "input",
                    ConnectToTerminalNodeId = "terminal",
                    MaxGeneratedNodeCount = 10,
                },
            ],
        };

        var clone = await _instantiator.InstantiateAsync(template,
            new TemplateInstantiationOptions { UserInput = "foo\nbar" });

        var generatedNodes = clone.Nodes.Where(n => n.Id.StartsWith("dyn_zone_a_", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, generatedNodes.Count);

        var cloneTerminal = clone.Nodes.First(n => n.Title == "Terminal");
        // Fixed terminal retains its old input dep AND gets the new generated dep
        var inputNode = clone.Nodes.First(n => n.Title == "Input");
        Assert.Contains(inputNode.Id, cloneTerminal.DependsOn);
        Assert.Contains(generatedNodes[^1].Id, cloneTerminal.DependsOn);
    }

    // ── Test 11: ConnectToTerminalNodeId not in graph — silently skips ──
    [Fact]
    public async Task InstantiateAsync_DynamicZone_ConnectToTerminalNodeIdNotInGraph_SkipsTerminalWiring()
    {
        var template = CreateTemplate();
        template.Nodes.Add(CreateNode("input", "Input"));
        template.TemplateMetadata = new TaskGraphTemplateMetadata
        {
            AllowDynamicExpansion = true,
            DynamicZones =
            [
                new DynamicZoneDefinition
                {
                    Id = "zone_a",
                    AnchorNodeId = "input",
                    ConnectToTerminalNodeId = "nonexistent",
                    MaxGeneratedNodeCount = 5,
                },
            ],
        };

        var clone = await _instantiator.InstantiateAsync(template,
            new TemplateInstantiationOptions { UserInput = "foo\nbar" });

        var generatedNodes = clone.Nodes.Where(n => n.Id.StartsWith("dyn_zone_a_", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, generatedNodes.Count);

        // Chain still works — dyn_1 depends on input
        var inputNode = clone.Nodes.First(n => n.Title == "Input");
        Assert.Contains(inputNode.Id, generatedNodes[0].DependsOn);
    }

    // ── Test 12: No anchor id → zone skipped ──
    [Fact]
    public async Task InstantiateAsync_DynamicZone_NoAnchorId_SkipsZone()
    {
        var template = CreateTemplate();
        template.Nodes.Add(CreateNode("input", "Input"));
        template.TemplateMetadata = new TaskGraphTemplateMetadata
        {
            AllowDynamicExpansion = true,
            DynamicZones =
            [
                new DynamicZoneDefinition
                {
                    Id = "zone_a",
                    AnchorNodeId = "",
                    InsertAfterNodeId = null,
                    MaxGeneratedNodeCount = 10,
                },
            ],
        };

        var clone = await _instantiator.InstantiateAsync(template,
            new TemplateInstantiationOptions { UserInput = "foo\nbar" });

        Assert.Single(clone.Nodes); // only the original (id rewritten) node
    }

    // ── Test 13: Anchor not in graph → zone skipped ──
    [Fact]
    public async Task InstantiateAsync_DynamicZone_AnchorNotInGraph_SkipsZone()
    {
        var template = CreateTemplate();
        template.Nodes.Add(CreateNode("input", "Input"));
        template.TemplateMetadata = new TaskGraphTemplateMetadata
        {
            AllowDynamicExpansion = true,
            DynamicZones =
            [
                new DynamicZoneDefinition
                {
                    Id = "zone_a",
                    AnchorNodeId = "nonexistent",
                    MaxGeneratedNodeCount = 10,
                },
            ],
        };

        var clone = await _instantiator.InstantiateAsync(template,
            new TemplateInstantiationOptions { UserInput = "foo\nbar" });

        Assert.Single(clone.Nodes);
    }

    // ── Test 14: Multiple zones each expanded independently ──
    [Fact]
    public async Task InstantiateAsync_MultipleZones_EachExpandedIndependently()
    {
        var template = CreateTemplate();
        template.Nodes.Add(CreateNode("input_a", "Input A"));
        template.Nodes.Add(CreateNode("input_b", "Input B"));
        template.TemplateMetadata = new TaskGraphTemplateMetadata
        {
            AllowDynamicExpansion = true,
            DynamicZones =
            [
                new DynamicZoneDefinition
                {
                    Id = "zone_a",
                    AnchorNodeId = "input_a",
                    MaxGeneratedNodeCount = 10,
                },
                new DynamicZoneDefinition
                {
                    Id = "zone_b",
                    AnchorNodeId = "input_b",
                    MaxGeneratedNodeCount = 10,
                },
            ],
        };

        var clone = await _instantiator.InstantiateAsync(template,
            new TemplateInstantiationOptions { UserInput = "foo\nbar\nbaz" });

        // 2 original + 3 (zone a) + 3 (zone b) = 8
        Assert.Equal(8, clone.Nodes.Count);
        var zoneANodes = clone.Nodes.Where(n => n.Id.StartsWith("dyn_zone_a_", StringComparison.Ordinal)).ToList();
        var zoneBNodes = clone.Nodes.Where(n => n.Id.StartsWith("dyn_zone_b_", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, zoneANodes.Count);
        Assert.Equal(3, zoneBNodes.Count);
    }

    // ── Test 15: Does not mutate template ──
    [Fact]
    public async Task InstantiateAsync_DoesNotMutateTemplate()
    {
        var template = CreateTemplate();
        template.Nodes.Add(CreateNode("n1", "Node 1"));
        template.Nodes.Add(CreateNode("n2", "Node 2"));
        template.TemplateMetadata = new TaskGraphTemplateMetadata
        {
            AllowDynamicExpansion = true,
            FixedNodeIds = ["n1"],
            DynamicZones =
            [
                new DynamicZoneDefinition
                {
                    Id = "zone_a",
                    AnchorNodeId = "n1",
                    MaxGeneratedNodeCount = 5,
                },
            ],
        };

        var templateId = template.Id;
        var templateNodeCount = template.Nodes.Count;
        var n1Id = template.Nodes[0].Id;
        var n1Status = template.Nodes[0].Status;
        var metadataZonesCount = template.TemplateMetadata.DynamicZones.Count;

        await _instantiator.InstantiateAsync(template,
            new TemplateInstantiationOptions { UserInput = "foo\nbar" });

        Assert.Equal(templateId, template.Id);
        Assert.Equal(templateNodeCount, template.Nodes.Count);
        Assert.Equal(n1Id, template.Nodes[0].Id);
        Assert.Equal(n1Status, template.Nodes[0].Status);
        Assert.Equal(metadataZonesCount, template.TemplateMetadata.DynamicZones.Count);
        Assert.Equal(TaskGraphDocumentKind.Template, template.DocumentKind);
    }

    // ── Test 16: FixedNodeIds preserved in clone ──
    [Fact]
    public async Task InstantiateAsync_FixedNodesAreNotRemovedOrRenamed()
    {
        // FixedNodeIds refer to original template node ids.
        // After RewriteNodeReferences, the clone's nodes have NEW ids,
        // so the old FixedNodeIds won't match the clone's nodes.
        // This is expected — FixedNodeIds are only used during expansion
        // to prevent mutation of certain nodes. The test verifies that
        // the expansion doesn't remove any nodes and doesn't break.
        var template = CreateTemplate();
        template.Nodes.Add(CreateNode("input", "Input"));
        template.Nodes.Add(CreateNode("terminal", "Terminal"));
        template.TemplateMetadata = new TaskGraphTemplateMetadata
        {
            AllowDynamicExpansion = true,
            FixedNodeIds = ["input", "terminal"],
            DynamicZones =
            [
                new DynamicZoneDefinition
                {
                    Id = "zone_a",
                    AnchorNodeId = "input",
                    ConnectToTerminalNodeId = "terminal",
                    MaxGeneratedNodeCount = 10,
                },
            ],
        };

        var clone = await _instantiator.InstantiateAsync(template,
            new TemplateInstantiationOptions { UserInput = "foo" });

        // Clone should have 2 original nodes (id rewritten) + 1 generated = 3
        Assert.Equal(3, clone.Nodes.Count);
        Assert.Contains(clone.Nodes, n => n.Title == "Input");
        Assert.Contains(clone.Nodes, n => n.Title == "Terminal");
    }

    // ── Test 18: OriginHint from options applied to clone ──
    [Fact]
    public async Task InstantiateAsync_AppliesOriginHintFromOptions()
    {
        var template = CreateTemplate();
        template.Nodes.Add(CreateNode("n1", "Node 1"));

        var clone = await _instantiator.InstantiateAsync(template,
            new TemplateInstantiationOptions { OriginHint = TaskGraphOriginHint.ChatAuto });
        Assert.Equal(TaskGraphOriginHint.ChatAuto, clone.OriginHint);
    }

    // ── Test 19: ConversationSessionId from options applied to clone ──
    [Fact]
    public async Task InstantiateAsync_AppliesConversationSessionIdFromOptions()
    {
        var template = CreateTemplate();
        template.Nodes.Add(CreateNode("n1", "Node 1"));

        var clone = await _instantiator.InstantiateAsync(template,
            new TemplateInstantiationOptions { ConversationSessionId = "session-123" });
        Assert.Equal("session-123", clone.ConversationSessionId);
    }

    // ── Test 20: {{user_input}} placeholder replaced in node Prompts ──
    [Fact]
    public async Task InstantiateAsync_ReplacesUserInputPlaceholderInPrompts()
    {
        var template = CreateTemplate();
        var node = CreateNode("n1", "Node 1");
        node.Prompt = "Do the following: {{user_input}}";
        template.Nodes.Add(node);

        var clone = await _instantiator.InstantiateAsync(template,
            new TemplateInstantiationOptions { UserInput = "my actual input" });

        var cloneNode = clone.Nodes[0];
        Assert.DoesNotContain("{{user_input}}", cloneNode.Prompt);
        Assert.Contains("my actual input", cloneNode.Prompt);
        Assert.Equal("Do the following: my actual input", cloneNode.Prompt);

        // Null UserInput replaces placeholder with empty string
        var clone2 = await _instantiator.InstantiateAsync(template,
            new TemplateInstantiationOptions());
        var clone2Node = clone2.Nodes[0];
        Assert.DoesNotContain("{{user_input}}", clone2Node.Prompt);
        Assert.Equal("Do the following: ", clone2Node.Prompt);
    }

    // ── Test 17: No cycles introduced ──
    [Fact]
    public async Task InstantiateAsync_NoCyclesIntroduced()
    {
        var template = CreateTemplate();
        template.Nodes.Add(CreateNode("input", "Input"));
        template.Nodes.Add(CreateNode("terminal", "Terminal"));
        template.TemplateMetadata = new TaskGraphTemplateMetadata
        {
            AllowDynamicExpansion = true,
            DynamicZones =
            [
                new DynamicZoneDefinition
                {
                    Id = "zone_a",
                    AnchorNodeId = "input",
                    ConnectToTerminalNodeId = "terminal",
                    MaxGeneratedNodeCount = 10,
                },
            ],
        };

        var clone = await _instantiator.InstantiateAsync(template,
            new TemplateInstantiationOptions { UserInput = "foo\nbar\nbaz" });

        // TopologicalSort throws if cycles exist
        var ordered = TaskGraphTopology.TopologicalSort(clone);
        Assert.NotNull(ordered);
        Assert.True(ordered.Count > 0);
    }
}
