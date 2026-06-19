namespace AgentOrchestrator.App.Services.TaskGraph;

public sealed record TaskGraphExecutionRequest(
    string WorkingDirectory,
    string Permission,
    string Model
);
