using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.TaskGraph;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

public interface ITaskGraphStore
{
    Task<IReadOnlyList<TaskGraphListItem>> ListAsync(CancellationToken ct = default);

    Task<TaskGraphModel?> LoadAsync(string id, CancellationToken ct = default);

    Task SaveAsync(TaskGraphModel graph, CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);
}
