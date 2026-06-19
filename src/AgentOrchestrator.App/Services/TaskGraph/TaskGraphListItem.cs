using System;
using AgentOrchestrator.App.Models.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

public sealed record TaskGraphListItem(
    string Id,
    string Name,
    DateTimeOffset UpdatedAt,
    TaskGraphExecutionState ExecutionState,
    int NodeCount,
    TaskGraphTemplateKind TemplateKind
);
