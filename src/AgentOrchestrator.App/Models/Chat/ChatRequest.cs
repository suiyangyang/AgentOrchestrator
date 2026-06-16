using System.Collections.Generic;

namespace AgentOrchestrator.App.Models.Chat;

public sealed record ChatRequest(
    string Prompt,
    IReadOnlyList<ChatAttachment> Attachments,
    string Permission,
    string Model);
