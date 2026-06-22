using System;

namespace AgentOrchestrator.App.Models.Chat;

public sealed record GraphMutationSnapshot(
    string SourceNodeId,
    string Description,
    DateTimeOffset OccurredAt);
