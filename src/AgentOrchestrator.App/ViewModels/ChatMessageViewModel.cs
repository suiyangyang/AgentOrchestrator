using AgentOrchestrator.App.Models.Chat;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AgentOrchestrator.App.ViewModels;

public partial class ChatMessageViewModel : ObservableObject, IChatMessage
{
    public ChatMessageViewModel(string id, ChatRole role, string author)
    {
        Id = id;
        Role = role;
        Author = author;
    }

    public string Id { get; }

    public ChatRole Role { get; }

    public string Author { get; }

    public bool IsUser => Role == ChatRole.User;

    public bool IsAssistant => Role == ChatRole.Assistant;

    [ObservableProperty]
    private bool _isStreaming;

    public ObservableCollection<ChatBlockViewModel> Blocks { get; } = [];

    IReadOnlyList<IChatBlock> IChatMessage.Blocks => Blocks;
}
