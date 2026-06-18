using System.Collections.Generic;

namespace AgentOrchestrator.App.Services.Agent;

public sealed record AgentQuestionOption(
    string Label,
    string? Description,
    string? Value);

public sealed record AgentQuestionItem(
    string Id,
    string Header,
    string Question,
    bool Multiple,
    bool Custom,
    IReadOnlyList<AgentQuestionOption> Options);

public sealed record AgentQuestionRequest(
    string RequestId,
    string Title,
    IReadOnlyList<AgentQuestionItem> Questions);
