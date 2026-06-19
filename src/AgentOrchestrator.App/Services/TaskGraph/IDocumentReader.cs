using System.Threading;
using System.Threading.Tasks;

namespace AgentOrchestrator.App.Services.TaskGraph;

public interface IDocumentReader
{
    Task<string> ReadAsync(string filePath, CancellationToken ct = default);
}
