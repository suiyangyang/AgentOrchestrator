using System;
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

    /// <summary>
    /// Lists all documents whose <see cref="TaskGraph.DocumentKind"/> is <see cref="TaskGraphDocumentKind.Template"/>,
    /// ordered by <see cref="TaskGraph.UpdatedAt"/> descending.
    /// </summary>
    Task<IReadOnlyList<TaskGraphListItem>> ListTemplatesAsync(CancellationToken ct = default);

    /// <summary>
    /// Lists all documents whose <see cref="TaskGraph.DocumentKind"/> is <see cref="TaskGraphDocumentKind.Runtime"/>,
    /// ordered by <see cref="TaskGraph.UpdatedAt"/> descending.
    /// </summary>
    Task<IReadOnlyList<TaskGraphListItem>> ListRuntimeGraphsAsync(CancellationToken ct = default);

    /// <summary>
    /// Loads a single template document by id. Tries the <c>template.{id}.json</c> prefixed
    /// file first, then falls back to the legacy <c>{id}.json</c> form. Returns <c>null</c> if
    /// the document is not found.
    /// </summary>
    Task<TaskGraphModel?> LoadTemplateAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Creates a fresh runtime graph by deep-copying the template identified by
    /// <paramref name="templateId"/>. Clears all runtime execution state on the clone,
    /// assigns fresh ids to every node, rewrites <c>DependsOn</c> references, and
    /// persists the result.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the template does not exist or its <see cref="TaskGraph.DocumentKind"/>
    /// is not <see cref="TaskGraphDocumentKind.Template"/>.
    /// </exception>
    Task<TaskGraphModel> InstantiateTemplateAsync(string templateId, TemplateInstantiationOptions options, CancellationToken ct = default);
}
