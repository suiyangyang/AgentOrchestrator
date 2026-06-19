using System.Collections.ObjectModel;

namespace AgentOrchestrator.App.ViewModels;

public sealed class TaskGraphNodeColumnViewModel
{
    public string Header { get; init; } = string.Empty;

    public ObservableCollection<Models.TaskGraph.TaskNode> Nodes { get; } = [];
}
