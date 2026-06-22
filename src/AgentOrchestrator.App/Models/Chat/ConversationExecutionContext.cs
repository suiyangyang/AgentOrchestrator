using System;
using System.Collections.Generic;

namespace AgentOrchestrator.App.Models.Chat;

public sealed class ConversationExecutionContext
{
    public string ConversationSessionId { get; init; } = string.Empty;
    public string GraphId { get; init; } = string.Empty;
    public bool HasExecutionLease { get; set; }
    public string? CurrentRunningNodeId { get; set; }
    public string? GraphSummary { get; set; }
    public List<NodeSummarySnapshot> RecentCompletedNodes { get; } = [];
    public List<NodeFailureSnapshot> RecentFailedNodes { get; } = [];
    public List<PendingDecisionSnapshot> PendingDecisions { get; } = [];
    public List<GraphMutationSnapshot> RecentMutations { get; } = [];
    public DateTimeOffset UpdatedAt { get; set; }
}
