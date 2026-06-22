using AgentOrchestrator.App.Models.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

public sealed record TaskGraphExecutionRequest(
    string WorkingDirectory,
    string Permission,
    string Model,
    string? ConversationSessionId = null,
    bool AllowInlineExecution = false,
    TaskGraphExecutionPresentationMode PresentationMode = TaskGraphExecutionPresentationMode.Workspace);
