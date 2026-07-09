namespace AgentOrchestrator.App.ViewModels;

public sealed record MessageActionRequest(
    string MessageId,
    string? PartId = null
);
