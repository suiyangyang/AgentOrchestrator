using System;
using System.Collections.Generic;
using System.Linq;
using AgentOrchestrator.App.Models.Chat;

namespace AgentOrchestrator.App.ViewModels;

public sealed class QueuedChatDraftViewModel : ViewModelBase
{
    public QueuedChatDraftViewModel(string id, string prompt, IReadOnlyList<ChatAttachment> attachments)
    {
        Id = id;
        Prompt = prompt;
        Attachments = attachments;
    }

    public string Id { get; }

    public string Prompt { get; }

    public IReadOnlyList<ChatAttachment> Attachments { get; }

    public string DisplayText
    {
        get
        {
            var prompt = (Prompt ?? string.Empty)
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();

            if (string.IsNullOrWhiteSpace(prompt))
            {
                return Attachments.Count switch
                {
                    0 => "空白消息",
                    1 => Attachments[0].DisplayName,
                    _ => $"{Attachments.Count} 个附件"
                };
            }

            return Attachments.Count == 0
                ? prompt
                : $"{prompt} · {Attachments.Count} 个附件";
        }
    }
}
