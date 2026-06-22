using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.TaskGraph;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

public interface ITaskGraphExecutionController
{
    Task StartAsync(TaskGraphModel graph, TaskGraphExecutionRequest request, CancellationToken ct = default);

    Task ResumeAsync(string graphId, GraphContinueDecision decision, CancellationToken ct = default);

    Task PauseAsync(string graphId, CancellationToken ct = default);

    Task CancelAsync(string graphId, CancellationToken ct = default);
}
