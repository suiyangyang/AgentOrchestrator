using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.TaskGraph;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

/// <summary>
/// Default implementation of <see cref="ITaskGraphTemplateInstantiator"/>.
/// Stateless — all transformation state is per-call.
/// </summary>
public sealed class TaskGraphTemplateInstantiator : ITaskGraphTemplateInstantiator
{
    /// <inheritdoc />
    public async Task<TaskGraphModel> InstantiateAsync(
        TaskGraphModel template,
        TemplateInstantiationOptions options,
        CancellationToken ct = default)
    {
        // 1. Validate
        if (template is null)
        {
            throw new InvalidOperationException("Template is null.");
        }

        if (template.DocumentKind != TaskGraphDocumentKind.Template)
        {
            throw new InvalidOperationException(
                $"Document is not a template (DocumentKind={template.DocumentKind}).");
        }

        ct.ThrowIfCancellationRequested();

        // 2. Deep-clone via JSON roundtrip.
        var cloneOptions = CloneJsonOptions();
        var cloneJson = JsonSerializer.Serialize(template, cloneOptions);
        var clone = JsonSerializer.Deserialize<TaskGraphModel>(cloneJson, cloneOptions)
            ?? throw new InvalidOperationException("Failed to deep-clone template.");

        // 3. Rewrite node ids and DependsOn.
        RewriteNodeReferences(clone);

        // 4. Set graph-level fields.
        clone.Id = Guid.NewGuid().ToString("N");
        clone.DocumentKind = TaskGraphDocumentKind.Runtime;
        clone.IsBuiltInTemplate = false;
        clone.BasedOnTemplateId = template.Id;
        clone.Name = !string.IsNullOrWhiteSpace(options.RuntimeGraphName)
            ? options.RuntimeGraphName.Trim()
            : $"{template.Name} - 实例";
        clone.SourceContent = options.UserInput ?? template.SourceContent;
        clone.ProjectId = options.ProjectId ?? template.ProjectId;
        clone.ProjectName = options.ProjectName ?? template.ProjectName;
        clone.ExecutionState = TaskGraphExecutionState.Draft;
        clone.ExecutionStartedAt = null;
        clone.ExecutionCompletedAt = null;
        clone.ConversationSessionId = null;
        clone.IsCheckpointPending = false;
        clone.ActiveCheckpointNodeId = null;
        if (options.OriginHint.HasValue)
        {
            clone.OriginHint = options.OriginHint.Value;
        }
        if (!string.IsNullOrWhiteSpace(options.ConversationSessionId))
        {
            clone.ConversationSessionId = options.ConversationSessionId;
        }

        // 5. Replace {{user_input}} placeholder in every node Prompt.
        var userInputForPlaceholder = options.UserInput ?? string.Empty;
        foreach (var node in clone.Nodes)
        {
            if (node.Prompt?.Contains("{{user_input}}", StringComparison.Ordinal) == true)
            {
                node.Prompt = node.Prompt.Replace("{{user_input}}", userInputForPlaceholder, StringComparison.Ordinal);
            }
        }

        // 6. Clear runtime state on every node.
        foreach (var node in clone.Nodes)
        {
            node.Status = TaskNodeStatus.Pending;
            node.AgentSessionId = null;
            node.AttemptCount = 0;
            node.LastError = null;
            node.OutputSummary = null;
            node.RawOutput = null;
            node.StartedAt = null;
            node.CompletedAt = null;
            node.StructuredSummary = null;
            node.TouchedFiles.Clear();
            node.ResultTags.Clear();
        }

        // 7. Dynamic zone expansion.
        var expansionSource = options.UserInput ?? template.SourceContent ?? string.Empty;
        ExpandDynamicZones(clone, expansionSource);

        await Task.CompletedTask;
        return clone;
    }

    // ── Private helpers ──

    private static JsonSerializerOptions CloneJsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
    };

    private static void RewriteNodeReferences(TaskGraphModel clone)
    {
        var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in clone.Nodes)
        {
            var oldId = node.Id;
            node.Id = Guid.NewGuid().ToString("N");
            idMap[oldId] = node.Id;
        }

        // Rewrite DependsOn on every node.
        foreach (var node in clone.Nodes)
        {
            var newDeps = new List<string>(node.DependsOn.Count);
            foreach (var dep in node.DependsOn)
            {
                if (idMap.TryGetValue(dep, out var newDep))
                {
                    newDeps.Add(newDep);
                }
            }

            node.DependsOn.Clear();
            foreach (var d in newDeps)
            {
                node.DependsOn.Add(d);
            }
        }

        // Rewrite metadata references so zone anchors / terminals / fixed
        // node ids still point to the correct nodes in the clone.
        if (clone.TemplateMetadata is not null)
        {
            // FixedNodeIds
            var newFixedIds = new List<string>(clone.TemplateMetadata.FixedNodeIds.Count);
            foreach (var fid in clone.TemplateMetadata.FixedNodeIds)
            {
                if (idMap.TryGetValue(fid, out var newFid))
                {
                    newFixedIds.Add(newFid);
                }
            }

            clone.TemplateMetadata.FixedNodeIds = newFixedIds;

            // DynamicZones
            foreach (var zone in clone.TemplateMetadata.DynamicZones)
            {
                RemapZoneReference(zone, idMap);
            }
        }
    }

    private static void RemapZoneReference(DynamicZoneDefinition zone, Dictionary<string, string> idMap)
    {
        if (!string.IsNullOrWhiteSpace(zone.AnchorNodeId)
            && idMap.TryGetValue(zone.AnchorNodeId, out var newAnchor))
        {
            zone.AnchorNodeId = newAnchor;
        }

        if (!string.IsNullOrWhiteSpace(zone.InsertAfterNodeId)
            && idMap.TryGetValue(zone.InsertAfterNodeId, out var newInsert))
        {
            zone.InsertAfterNodeId = newInsert;
        }

        if (!string.IsNullOrWhiteSpace(zone.ConnectToTerminalNodeId)
            && idMap.TryGetValue(zone.ConnectToTerminalNodeId, out var newTerm))
        {
            zone.ConnectToTerminalNodeId = newTerm;
        }
    }

    /// <summary>
    /// For each <see cref="DynamicZoneDefinition"/> in the template's
    /// <see cref="TaskGraphTemplateMetadata.DynamicZones"/>, parse the user input
    /// as lines, generate up to <c>MaxGeneratedNodeCount</c> Execute nodes chained
    /// in series, connect the first to the anchor (or the node after which to
    /// insert), connect the last to the terminal (if any). Skip zones whose
    /// <c>AllowDynamicExpansion</c> is false (or whose template metadata
    /// is null / has no zones).
    /// </summary>
    private static void ExpandDynamicZones(TaskGraphModel clone, string userInput)
    {
        if (clone.TemplateMetadata is null)
        {
            return;
        }

        if (!clone.TemplateMetadata.AllowDynamicExpansion)
        {
            return;
        }

        if (clone.TemplateMetadata.DynamicZones is null || clone.TemplateMetadata.DynamicZones.Count == 0)
        {
            return;
        }

        var lines = ParseLines(userInput);

        // Build a set of fixed node ids for fast lookup.
        var fixedIds = new HashSet<string>(clone.TemplateMetadata.FixedNodeIds, StringComparer.Ordinal);

        foreach (var zone in clone.TemplateMetadata.DynamicZones)
        {
            if (lines.Count == 0)
            {
                continue;
            }

            if (zone.MaxGeneratedNodeCount <= 0)
            {
                continue;
            }

            // Cap the line count by the zone's limit.
            var linesToExpand = lines.Take(zone.MaxGeneratedNodeCount).ToList();

            // Resolve the splice point — InsertAfterNodeId wins over AnchorNodeId.
            var spliceAfterId = !string.IsNullOrWhiteSpace(zone.InsertAfterNodeId)
                ? zone.InsertAfterNodeId
                : zone.AnchorNodeId;

            if (string.IsNullOrWhiteSpace(spliceAfterId))
            {
                continue;
            }

            var spliceNode = clone.Nodes.FirstOrDefault(n => n.Id == spliceAfterId);
            if (spliceNode is null)
            {
                continue;
            }

            // Build the chain. Each generated node is a fresh Execute node.
            var generated = new List<TaskNode>();
            for (var idx = 0; idx < linesToExpand.Count; idx++)
            {
                var line = linesToExpand[idx];
                var node = new TaskNode
                {
                    Id = $"dyn_{zone.Id}_{idx + 1}",
                    Title = line,
                    Description = zone.GenerationInstruction ?? string.Empty,
                    Kind = TaskNodeKind.Execute,
                    Prompt = TaskGraphFactory.BuildPrompt(line, zone.GenerationInstruction, TaskNodeKind.Execute),
                    Status = TaskNodeStatus.Pending,
                };
                generated.Add(node);
            }

            // Wire up the chain.
            for (var i = 0; i < generated.Count; i++)
            {
                if (i == 0)
                {
                    generated[i].DependsOn.Add(spliceAfterId);
                }
                else
                {
                    generated[i].DependsOn.Add(generated[i - 1].Id);
                }
            }

            // Add all generated nodes to the clone.
            foreach (var n in generated)
            {
                clone.Nodes.Add(n);
            }

            // Re-wire the terminal node (if any).
            if (!string.IsNullOrWhiteSpace(zone.ConnectToTerminalNodeId) && generated.Count > 0)
            {
                var terminal = clone.Nodes.FirstOrDefault(n => n.Id == zone.ConnectToTerminalNodeId);
                if (terminal is not null)
                {
                    if (fixedIds.Contains(terminal.Id))
                    {
                        // Don't mutate a fixed terminal's DependsOn. Add a supplemental edge.
                        if (!terminal.DependsOn.Contains(generated[^1].Id))
                        {
                            terminal.DependsOn.Add(generated[^1].Id);
                        }
                    }
                    else
                    {
                        // Mutable terminal — replace the old splice-point dependency.
                        terminal.DependsOn.Remove(spliceAfterId);
                        if (!terminal.DependsOn.Contains(generated[^1].Id))
                        {
                            terminal.DependsOn.Add(generated[^1].Id);
                        }
                    }
                }
            }
        }
    }

    private static List<string> ParseLines(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.TrimStart('-', '*', ' ', '\t').Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }
}
