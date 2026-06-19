using System.Collections.Generic;
using AgentOrchestrator.App.Models.Sidebar;
using AgentOrchestrator.App.Models.TaskGraph;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

public interface INodeOutputInjector
{
    string BuildPrompt(TaskNode node, TaskGraphModel graph);

    void PopulateOutput(TaskNode node, IReadOnlyList<RemoteMessage> messages);
}
