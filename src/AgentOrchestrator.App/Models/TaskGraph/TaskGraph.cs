using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentOrchestrator.App.Models.TaskGraph;

public sealed partial class TaskGraph : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _name = "未命名编排";

    [ObservableProperty]
    private DateTimeOffset _createdAt = DateTimeOffset.UtcNow;

    [ObservableProperty]
    private DateTimeOffset _updatedAt = DateTimeOffset.UtcNow;

    [ObservableProperty]
    private TaskGraphMode _mode = TaskGraphMode.Direct;

    [ObservableProperty]
    private TaskGraphTemplateKind _templateKind = TaskGraphTemplateKind.Custom;

    [ObservableProperty]
    private TaskGraphExecutionState _executionState = TaskGraphExecutionState.Draft;

    [ObservableProperty]
    private string? _sourceContent;

    [ObservableProperty]
    private string? _sourceFilePath;

    [ObservableProperty]
    private string? _projectId;

    [ObservableProperty]
    private string? _projectName;

    [ObservableProperty]
    private DateTimeOffset? _executionStartedAt;

    [ObservableProperty]
    private DateTimeOffset? _executionCompletedAt;

    [ObservableProperty]
    private TaskGraphOriginHint _originHint = TaskGraphOriginHint.WorkspaceDirect;

    [ObservableProperty]
    private string? _conversationSessionId;

    [ObservableProperty]
    private bool _isCheckpointPending;

    [ObservableProperty]
    private string? _activeCheckpointNodeId;

    public ObservableCollection<TaskNode> Nodes { get; set; } = [];

    public ObservableCollection<TaskEdge> Edges { get; set; } = [];

    public void RebuildEdges()
    {
        var edges = Nodes
            .SelectMany(
                node => node.DependsOn.Select(dep => new TaskEdge
                {
                    SourceId = dep,
                    TargetId = node.Id,
                }))
            .ToList();

        Edges.Clear();
        foreach (var edge in edges)
        {
            Edges.Add(edge);
        }
    }
}
