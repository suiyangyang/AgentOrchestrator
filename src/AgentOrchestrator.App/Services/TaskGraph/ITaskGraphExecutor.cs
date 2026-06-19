using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.TaskGraph;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

public interface ITaskGraphExecutor
{
    Task ExecuteAsync(TaskGraphModel graph, TaskGraphExecutionRequest request, CancellationToken ct = default);

    Task RetryFailedAsync(TaskGraphModel graph, TaskGraphExecutionRequest request, CancellationToken ct = default);

    Task ContinueAsync(TaskGraphModel graph, TaskGraphExecutionRequest request, CancellationToken ct = default);

    void RequestCancel(string graphId);

    Task ReconcileAsync(TaskGraphModel graph, CancellationToken ct = default);
}
