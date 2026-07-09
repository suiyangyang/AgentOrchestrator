using AgentOrchestrator.App.Models.Chat;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text;

namespace AgentOrchestrator.App.ViewModels;

public partial class ChatMessageViewModel : ObservableObject, IChatMessage
{
    public ChatMessageViewModel(string id, ChatRole role, string author)
    {
        Id = id;
        Role = role;
        Author = author;
        Blocks.CollectionChanged += OnBlocksCollectionChanged;
    }

    public string Id { get; }

    public ChatRole Role { get; }

    public string Author { get; }

    public bool IsUser => Role == ChatRole.User;

    public bool IsAssistant => Role == ChatRole.Assistant;

    [ObservableProperty]
    private string? _remoteMessageId;

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    private string? _streamingStatusText;

    partial void OnIsStreamingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowStreamingPlaceholder));
    }

    public ObservableCollection<ChatBlockViewModel> Blocks { get; } = [];

    public bool ShowStreamingPlaceholder => IsAssistant && IsStreaming && Blocks.Count == 0;

    private void OnBlocksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ShowStreamingPlaceholder));
    }

    public string BuildCopyText()
    {
        var builder = new StringBuilder();
        foreach (var block in Blocks.Where(block => !string.IsNullOrWhiteSpace(GetBlockText(block))))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.Append(GetBlockText(block));
        }

        return builder.ToString().Trim();
    }

    private static string? GetBlockText(ChatBlockViewModel block) => block.Kind switch
    {
        ChatBlockKind.Text or ChatBlockKind.Thought => block.Text?.Trim(),
        ChatBlockKind.Image => string.IsNullOrWhiteSpace(block.Text) ? null : $"附件：{block.Text.Trim()}",
        ChatBlockKind.Tool or ChatBlockKind.Task => BuildToolText(block),
        _ => block.Text?.Trim(),
    };

    private static string? BuildToolText(ChatBlockViewModel block)
    {
        var title = string.IsNullOrWhiteSpace(block.ToolName) ? "tool" : block.ToolName.Trim();
        var output = string.IsNullOrWhiteSpace(block.ToolOutput) ? null : block.ToolOutput.Trim();
        return string.IsNullOrWhiteSpace(output) ? title : $"{title}\n{output}";
    }

    IReadOnlyList<IChatBlock> IChatMessage.Blocks => Blocks;
}
