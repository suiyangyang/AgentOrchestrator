using System;

namespace AgentOrchestrator.App.Models.Chat;

public sealed record PendingDecisionSnapshot(
    string NodeId,
    string Title,
    string Reason,
    DateTimeOffset RequestedAt);
