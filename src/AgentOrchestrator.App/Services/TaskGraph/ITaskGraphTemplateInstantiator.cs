using System;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.TaskGraph;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

/// <summary>
/// Transforms a <see cref="TaskGraphDocumentKind.Template"/> into a fresh
/// <see cref="TaskGraphDocumentKind.Runtime"/> instance: deep-copies the
/// template, clears runtime state, applies the user input, and performs
/// controlled dynamic-zone expansion per the template's
/// <see cref="TaskGraphTemplateMetadata.DynamicZones"/>.
/// </summary>
/// <remarks>
/// The instantiator is pure transformation — it performs no I/O except the
/// JSON roundtrip used for deep-clone. The caller is responsible for persisting
/// the result via <see cref="ITaskGraphStore.SaveAsync"/>.
/// </remarks>
public interface ITaskGraphTemplateInstantiator
{
    /// <summary>
    /// Instantiates the given <paramref name="template"/> into a runtime graph.
    /// Does NOT persist the result — the caller is responsible for saving.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="template"/> is null or its
    /// <see cref="TaskGraphModel.DocumentKind"/> is not <see cref="TaskGraphDocumentKind.Template"/>.
    /// </exception>
    Task<TaskGraphModel> InstantiateAsync(
        TaskGraphModel template,
        TemplateInstantiationOptions options,
        CancellationToken ct = default);
}
