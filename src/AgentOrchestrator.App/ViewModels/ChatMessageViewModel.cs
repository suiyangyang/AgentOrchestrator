using AgentOrchestrator.App.Models.Chat;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

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

    IReadOnlyList<IChatBlock> IChatMessage.Blocks => Blocks;
}
