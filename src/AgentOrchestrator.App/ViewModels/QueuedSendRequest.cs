using System.Collections.Generic;
using AgentOrchestrator.App.Models.Chat;

namespace AgentOrchestrator.App.ViewModels;

internal sealed record QueuedSendRequest(
    string Id,
    string Prompt,
    IReadOnlyList<ChatAttachment> Attachments);
