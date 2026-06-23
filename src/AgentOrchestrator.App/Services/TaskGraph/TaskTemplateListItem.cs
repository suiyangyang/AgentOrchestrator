using System;
using AgentOrchestrator.App.Models.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

public sealed record TaskTemplateListItem(
    string Id,
    string Name,
    DateTimeOffset UpdatedAt,
    TaskGraphTemplateKind BaseKind,
    bool IsBuiltIn
);
