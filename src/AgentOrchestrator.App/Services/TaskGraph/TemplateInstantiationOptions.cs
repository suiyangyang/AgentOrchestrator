using AgentOrchestrator.App.Models.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

/// <summary>
/// Options passed to <see cref="ITaskGraphStore.InstantiateTemplateAsync"/> to control
/// how a template is materialized into a runtime graph.
/// </summary>
public sealed class TemplateInstantiationOptions
{
    /// <summary>
    /// Name for the new runtime graph. When null or empty, the store generates a
    /// default name based on the template name (e.g. "{template name} - 实例").
    /// </summary>
    public string? RuntimeGraphName { get; set; }

    /// <summary>
    /// User-provided input text that replaces the template's <c>SourceContent</c>
    /// in the runtime instance. When null, the template's original content is kept.
    /// </summary>
    public string? UserInput { get; set; }

    /// <summary>
    /// Identifier of the project the new runtime graph belongs to.
    /// When null, the template's <c>ProjectId</c> is inherited.
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>
    /// Human-readable name of the project the new runtime graph belongs to.
    /// When null, the template's <c>ProjectName</c> is inherited.
    /// </summary>
    public string? ProjectName { get; set; }

    /// <summary>
    /// Origin hint indicating how this runtime graph was created (Workspace, Chat, etc.).
    /// When null, the default on <see cref="TaskGraph"/> applies (<c>WorkspaceDirect</c>).
    /// </summary>
    public TaskGraphOriginHint? OriginHint { get; set; }

    /// <summary>
    /// Chat conversation session identifier that links the runtime graph back to its
    /// originating chat session. When null, the runtime graph's <c>ConversationSessionId</c>
    /// remains cleared.
    /// </summary>
    public string? ConversationSessionId { get; set; }
}
