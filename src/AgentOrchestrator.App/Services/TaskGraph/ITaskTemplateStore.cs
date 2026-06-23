using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

public interface ITaskTemplateStore
{
    Task<IReadOnlyList<TaskTemplateListItem>> ListAsync(CancellationToken ct = default);

    Task<TaskTemplate?> LoadAsync(string id, CancellationToken ct = default);

    Task SaveAsync(TaskTemplate template, CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>Returns the directory the store reads from / writes to.</summary>
    string StorageDirectory { get; }
}
