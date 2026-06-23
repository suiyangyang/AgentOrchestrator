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

    public JsonTaskGraphStore()
        : this(Path.Combine(DataPathProvider.DataDirectory, "TaskGraphs"))
    {
    }

    public JsonTaskGraphStore(string graphsDirectory)
    {
        _graphsDirectory = graphsDirectory;
        Directory.CreateDirectory(_graphsDirectory);
    }

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
                    graph.ProjectName));
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

    public async Task<TaskGraphModel?> LoadAsync(string id, CancellationToken ct = default)
    {
        var filePath = GetFilePath(id);
        if (!File.Exists(filePath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        var graph = JsonSerializer.Deserialize<TaskGraphModel>(json, SerializerOptions);
        if (graph is null)
        {
            return null;
        }

        NormalizeGraph(graph);
        return graph;
    }

    public async Task SaveAsync(TaskGraphModel graph, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_graphsDirectory);
        NormalizeGraph(graph);
        graph.UpdatedAt = DateTimeOffset.UtcNow;
        graph.RebuildEdges();
        var json = JsonSerializer.Serialize(graph, SerializerOptions);
        await File.WriteAllTextAsync(GetFilePath(graph.Id), json, ct).ConfigureAwait(false);
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var filePath = GetFilePath(id);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }

    private string GetFilePath(string id)
        => Path.Combine(_graphsDirectory, $"{id}.json");

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

        graph.RebuildEdges();
    }
}
