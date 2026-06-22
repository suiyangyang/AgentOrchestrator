using System;

namespace AgentOrchestrator.App.Models.Chat;

public sealed record NodeFailureSnapshot(
    string NodeId,
    string Title,
    string Error,
    bool Retryable,
    DateTimeOffset FailedAt);
