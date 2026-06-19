using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.TaskGraph;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

public interface ITaskGraphPlanner
{
    Task<TaskGraphModel> CreateFromIntentAsync(
        string userText,
        string workingDirectory,
        string permission,
        string model,
        CancellationToken ct = default);

    Task<TaskGraphModel> CreateFromDocumentAsync(
        string filePath,
        string documentContent,
        string workingDirectory,
        string permission,
        string model,
        CancellationToken ct = default);
}
