using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.Sidebar;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

public sealed class JsonTaskGraphStore : ITaskGraphStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _graphsDirectory;
    private readonly ITaskGraphTemplateInstantiator _instantiator;

    public JsonTaskGraphStore()
        : this(Path.Combine(DataPathProvider.DataDirectory, "TaskGraphs"), new TaskGraphTemplateInstantiator())
    {
    }

    public JsonTaskGraphStore(string graphsDirectory)
        : this(graphsDirectory, new TaskGraphTemplateInstantiator())
    {
    }

    public JsonTaskGraphStore(string graphsDirectory, ITaskGraphTemplateInstantiator instantiator)
    {
        _graphsDirectory = graphsDirectory;
        _instantiator = instantiator;
        Directory.CreateDirectory(_graphsDirectory);
    }

    /// <summary>
    /// Lists ALL documents (templates + runtime) ordered by <c>UpdatedAt</c> descending.
    /// Preserves backward compatibility with existing UI code.
    /// </summary>
    public async Task<IReadOnlyList<TaskGraphListItem>> ListAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_graphsDirectory);
        var result = new List<TaskGraphListItem>();
        foreach (var file in Directory.EnumerateFiles(_graphsDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var json = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                var graph = JsonSerializer.Deserialize<TaskGraphModel>(json, SerializerOptions);
                if (graph is null)
                {
                    continue;
                }

                NormalizeGraph(graph);
                result.Add(new TaskGraphListItem(
                    graph.Id,
                    graph.Name,
                    graph.UpdatedAt,
                    graph.ExecutionState,
                    graph.Nodes.Count,
                    graph.TemplateKind,
                    graph.ProjectId,
                    graph.ProjectName,
                    graph.IsBuiltInTemplate,
                    graph.DocumentKind));
            }
            catch
            {
                // Ignore malformed files so one bad graph does not break the workspace.
            }
        }

        return result
            .OrderByDescending(x => x.UpdatedAt)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TaskGraphListItem>> ListTemplatesAsync(CancellationToken ct = default)
    {
        var all = await ListAsync(ct).ConfigureAwait(false);
        // ListAsync already deserialized every file. We need to re-load to check
        // DocumentKind because TaskGraphListItem does not carry it. For correctness,
        // we enumerate files ourselves so we can filter by DocumentKind efficiently.
        return await EnumerateDocumentsAsync(TaskGraphDocumentKind.Template, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TaskGraphListItem>> ListRuntimeGraphsAsync(CancellationToken ct = default)
    {
        return await EnumerateDocumentsAsync(TaskGraphDocumentKind.Runtime, ct).ConfigureAwait(false);
    }

    public async Task<TaskGraphModel?> LoadAsync(string id, CancellationToken ct = default)
    {
        if (!TryGetFilePath(id, out var filePath) || !File.Exists(filePath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        var graph = JsonSerializer.Deserialize<TaskGraphModel>(json, SerializerOptions);
        if (graph is null)
        {
            return null;
        }

        // Infer DocumentKind from filename prefix for legacy files that lack the field.
        var fileName = Path.GetFileName(filePath);
        if (graph.DocumentKind == TaskGraphDocumentKind.Runtime
            && fileName.StartsWith("template.", StringComparison.OrdinalIgnoreCase))
        {
            graph.DocumentKind = TaskGraphDocumentKind.Template;
        }
        else if (graph.DocumentKind == TaskGraphDocumentKind.Runtime
                 && fileName.StartsWith("runtime.", StringComparison.OrdinalIgnoreCase))
        {
            // Already Runtime — nothing to infer.
        }
        // For unprefixed legacy files, trust the serialized value (default Runtime).

        NormalizeGraph(graph);
        return graph;
    }

    /// <inheritdoc />
    public async Task<TaskGraphModel?> LoadTemplateAsync(string id, CancellationToken ct = default)
    {
        // Try prefixed template file first.
        var templatePath = GetFilePath(id, TaskGraphDocumentKind.Template);
        if (!File.Exists(templatePath))
        {
            // Fall back to legacy unprefixed file.
            var legacyPath = Path.Combine(_graphsDirectory, $"{id}.json");
            if (!File.Exists(legacyPath))
            {
                return null;
            }

            templatePath = legacyPath;
        }

        var json = await File.ReadAllTextAsync(templatePath, ct).ConfigureAwait(false);
        var graph = JsonSerializer.Deserialize<TaskGraphModel>(json, SerializerOptions);
        if (graph is null)
        {
            return null;
        }

        NormalizeGraph(graph);
        return graph;
    }

    /// <inheritdoc />
    public async Task<TaskGraphModel> InstantiateTemplateAsync(
        string templateId,
        TemplateInstantiationOptions options,
        CancellationToken ct = default)
    {
        var template = await LoadTemplateAsync(templateId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Template '{templateId}' not found.");
        if (template.DocumentKind != TaskGraphDocumentKind.Template)
        {
            throw new InvalidOperationException(
                $"Document '{templateId}' is not a template (DocumentKind={template.DocumentKind}).");
        }

        var clone = await _instantiator.InstantiateAsync(template, options, ct).ConfigureAwait(false);
        await SaveAsync(clone, ct).ConfigureAwait(false);
        return (await LoadAsync(clone.Id, ct).ConfigureAwait(false))!;
    }

    public async Task SaveAsync(TaskGraphModel graph, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_graphsDirectory);

        if (graph.DocumentKind == TaskGraphDocumentKind.Runtime && graph.Nodes.Count == 0)
        {
            throw new TaskGraphValidationException("任务编排不能为空,至少需要一个节点。");
        }

        var normalizedName = (graph.Name ?? string.Empty).Trim();
        var sameKindDocuments = await EnumerateDocumentsAsync(graph.DocumentKind, ct).ConfigureAwait(false);
        if (sameKindDocuments.Any(item =>
                !string.Equals(item.Id, graph.Id, StringComparison.Ordinal)
                && string.Equals(item.Name.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DuplicateTaskGraphNameException(normalizedName, graph.DocumentKind);
        }

        NormalizeGraph(graph);
        graph.UpdatedAt = DateTimeOffset.UtcNow;
        graph.RebuildEdges();
        var json = JsonSerializer.Serialize(graph, SerializerOptions);
        await File.WriteAllTextAsync(GetFilePath(graph.Id, graph.DocumentKind), json, ct).ConfigureAwait(false);
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        // Try all 3 forms so deletion works regardless of how the file was named.
        foreach (var candidate in GetDeleteCandidatePaths(id))
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }

        return Task.CompletedTask;
    }

    // ── Private helpers ──

    /// <summary>
    /// Returns the prefixed path for a graph with the given <paramref name="kind"/>.
    /// </summary>
    private string GetFilePath(string id, TaskGraphDocumentKind kind)
    {
        var prefix = kind == TaskGraphDocumentKind.Template ? "template" : "runtime";
        return Path.Combine(_graphsDirectory, $"{prefix}.{id}.json");
    }

    /// <summary>
    /// Tries to find an existing file for <paramref name="id"/> across all 3 naming
    /// conventions: <c>template.{id}.json</c>, <c>runtime.{id}.json</c>, <c>{id}.json</c>.
    /// </summary>
    private bool TryGetFilePath(string id, out string filePath)
    {
        // Prefixed forms.
        var templatePath = GetFilePath(id, TaskGraphDocumentKind.Template);
        if (File.Exists(templatePath))
        {
            filePath = templatePath;
            return true;
        }

        var runtimePath = GetFilePath(id, TaskGraphDocumentKind.Runtime);
        if (File.Exists(runtimePath))
        {
            filePath = runtimePath;
            return true;
        }

        // Legacy unprefixed.
        var legacyPath = Path.Combine(_graphsDirectory, $"{id}.json");
        if (File.Exists(legacyPath))
        {
            filePath = legacyPath;
            return true;
        }

        filePath = string.Empty;
        return false;
    }

    /// <summary>
    /// Returns all 3 possible paths for a given id so that <see cref="DeleteAsync"/>
    /// can clean up regardless of which naming convention was used.
    /// </summary>
    private IEnumerable<string> GetDeleteCandidatePaths(string id)
    {
        yield return GetFilePath(id, TaskGraphDocumentKind.Template);
        yield return GetFilePath(id, TaskGraphDocumentKind.Runtime);
        yield return Path.Combine(_graphsDirectory, $"{id}.json");
    }

    /// <summary>
    /// Enumerates all <c>*.json</c> files in the store directory, deserializes them,
    /// filters to the requested <paramref name="kind"/>, and projects them to
    /// <see cref="TaskGraphListItem"/> ordered by <c>UpdatedAt</c> descending.
    /// </summary>
    private async Task<IReadOnlyList<TaskGraphListItem>> EnumerateDocumentsAsync(
        TaskGraphDocumentKind kind,
        CancellationToken ct)
    {
        Directory.CreateDirectory(_graphsDirectory);
        var result = new List<TaskGraphListItem>();
        foreach (var file in Directory.EnumerateFiles(_graphsDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var json = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                var graph = JsonSerializer.Deserialize<TaskGraphModel>(json, SerializerOptions);
                if (graph is null)
                {
                    continue;
                }

                // Infer kind from filename prefix if the field is missing/legacy.
                var fileName = Path.GetFileName(file);
                if (graph.DocumentKind == TaskGraphDocumentKind.Runtime
                    && fileName.StartsWith("template.", StringComparison.OrdinalIgnoreCase))
                {
                    graph.DocumentKind = TaskGraphDocumentKind.Template;
                }

                if (graph.DocumentKind != kind)
                {
                    continue;
                }

                NormalizeGraph(graph);
                result.Add(new TaskGraphListItem(
                    graph.Id,
                    graph.Name,
                    graph.UpdatedAt,
                    graph.ExecutionState,
                    graph.Nodes.Count,
                    graph.TemplateKind,
                    graph.ProjectId,
                    graph.ProjectName,
                    graph.IsBuiltInTemplate,
                    graph.DocumentKind));
            }
            catch
            {
                // Ignore malformed files.
            }
        }

        return result
            .OrderByDescending(x => x.UpdatedAt)
            .ToList();
    }

    private static void NormalizeGraph(TaskGraphModel graph)
    {
        graph.Id = string.IsNullOrWhiteSpace(graph.Id) ? Guid.NewGuid().ToString("N") : graph.Id;
        graph.Name = string.IsNullOrWhiteSpace(graph.Name) ? "未命名编排" : graph.Name;
        graph.ProjectId = string.IsNullOrWhiteSpace(graph.ProjectId) ? null : graph.ProjectId;
        graph.ProjectName = string.IsNullOrWhiteSpace(graph.ProjectName) ? null : graph.ProjectName;
        graph.Nodes ??= [];
        graph.Edges ??= [];

        foreach (var node in graph.Nodes)
        {
            node.Id = string.IsNullOrWhiteSpace(node.Id) ? Guid.NewGuid().ToString("N") : node.Id;
            node.Title = string.IsNullOrWhiteSpace(node.Title) ? "新任务" : node.Title;
            node.Description ??= string.Empty;
            node.Prompt ??= string.Empty;
            node.Tags ??= [];
            node.DependsOn ??= [];
            node.TouchedFiles ??= [];
            node.ResultTags ??= [];
            node.Position ??= new(40, 40);
            node.RawOutput ??= null;
        }

        if (graph.TemplateMetadata is not null)
        {
            graph.TemplateMetadata.FixedNodeIds ??= [];
            graph.TemplateMetadata.FixedEdgeKeys ??= [];
            graph.TemplateMetadata.DynamicZones ??= [];
            graph.TemplateMetadata.NodeGenerationRules ??= [];
            graph.TemplateMetadata.EdgeGenerationRules ??= [];
            graph.TemplateMetadata.ExecutionRules ??= [];
        }

        graph.RebuildEdges();
    }
}
