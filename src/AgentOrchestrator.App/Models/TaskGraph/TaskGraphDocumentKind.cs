namespace AgentOrchestrator.App.Models.TaskGraph;

/// <summary>
/// Identifies whether a <see cref="TaskGraph"/> document is a runtime instance (executable) or a template (blueprint, not directly executable).
/// </summary>
public enum TaskGraphDocumentKind
{
    Runtime = 0,
    Template = 1,
}
