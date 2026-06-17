namespace AgentOrchestrator.App.Services.Agent;

public sealed record SubagentActivitySnapshot(
    string SessionId,
    string Title,
    string StatusText,
    string AgentName,
    string ModelName,
    string Content,
    bool IsBusy,
    long DurationMs,
    long UpdatedAt
);
