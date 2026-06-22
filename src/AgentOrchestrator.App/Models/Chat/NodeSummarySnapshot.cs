using System;

namespace AgentOrchestrator.App.Models.Chat;

public sealed record NodeSummarySnapshot(
    string NodeId,
    string Title,
    string Summary,
    DateTimeOffset CompletedAt);
