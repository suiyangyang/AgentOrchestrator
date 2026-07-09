namespace AgentOrchestrator.App.Models.Chat;

/// <summary>
/// Payload for <see cref="ViewModels.ChatWorkspaceViewModel.TemplateOrchestrationRequested"/>.
/// Carries the user's draft text, the current agent session id, and the working
/// directory so the shell can decide how to route the template instantiation flow.
/// </summary>
public sealed record TemplateOrchestrationRequest(
    string Prompt,
    string? AgentSessionId,
    string? WorkingDirectory);
