using AgentOrchestrator.App.Models.Chat;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace AgentOrchestrator.App.ViewModels;

public partial class ChatWorkspaceViewModel : ViewModelBase
{
    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ObservableCollection<ChatAttachment> Attachments { get; } = [];

    public ObservableCollection<PermissionOption> Permissions { get; } =
    [
        new("ask", "请求批准", "编辑外部文件和使用互联网时始终询问", "✋"),
        new("replace", "替代批准", "仅对检测到的风险操作请求批准", "◔"),
        new("full", "完全访问权限", "可不受限制地访问互联网和您电脑上的任何文件", "🛡")
    ];

    public IReadOnlyList<string> Models { get; } = ["codex", "gpt-5", "claude-compatible"];

    [ObservableProperty]
    private string _draftText = string.Empty;

    [ObservableProperty]
    private PermissionOption _selectedPermission = null!;

    [ObservableProperty]
    private string _selectedModel = "codex";

    public bool HasAttachments => Attachments.Count > 0;

    public ChatWorkspaceViewModel()
    {
        _selectedPermission = Permissions[2];
        _selectedPermission.IsSelected = true;
        Attachments.CollectionChanged += OnAttachmentsChanged;
        SeedConversation();
    }

    [RelayCommand]
    private void Send()
    {
        var prompt = DraftText.Trim();
        if (string.IsNullOrWhiteSpace(prompt) && Attachments.Count == 0)
        {
            return;
        }

        var userMessage = new ChatMessageViewModel(Guid.NewGuid().ToString("N"), ChatRole.User, "你");
        if (!string.IsNullOrWhiteSpace(prompt))
        {
            userMessage.Blocks.Add(new ChatBlockViewModel(ChatBlockKind.Text, prompt));
        }

        foreach (var attachment in Attachments)
        {
            userMessage.Blocks.Add(new ChatBlockViewModel(ChatBlockKind.Image, attachment.DisplayName, attachment.Path));
        }

        Messages.Add(userMessage);

        DraftText = string.Empty;
        Attachments.Clear();

        var assistantMessage = new ChatMessageViewModel(Guid.NewGuid().ToString("N"), ChatRole.Assistant, "Codex")
        {
            IsStreaming = true
        };
        assistantMessage.Blocks.Add(new ChatBlockViewModel(ChatBlockKind.Thought, "thinking", isExpanded: false));
        assistantMessage.Blocks.Add(new ChatBlockViewModel(ChatBlockKind.Text, "这里会接普通消息块，直接展开显示。"));
        Messages.Add(assistantMessage);
    }

    [RelayCommand]
    private void AddAttachment()
    {
        var index = Attachments.Count + 1;
        Attachments.Add(new ChatAttachment($"file-{index}.md", $"/mock/file-{index}.md", false));
    }

[RelayCommand]
    private void AddImage()
    {
        var index = Attachments.Count + 1;
        Attachments.Add(new ChatAttachment($"image-{index}.png", $"/mock/file-{index}.png", true));
    }

    /// <summary>
    /// Adds an image attachment backed by an actual file on disk (e.g. from clipboard paste).
    /// </summary>
    public void AddPastedImage(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return;
        }

        var displayName = Path.GetFileName(path);
        Attachments.Add(new ChatAttachment(displayName, path, isImage: true));
    }

    [RelayCommand]
    private void OpenPermissionMenu()
    {
    }

    [RelayCommand]
    private void OpenModelConfig()
    {
    }

    [RelayCommand]
    private void RemoveAttachment(ChatAttachment attachment)
    {
        Attachments.Remove(attachment);
    }

    private void OnAttachmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasAttachments));
    }

    partial void OnSelectedPermissionChanged(PermissionOption value)
    {
        foreach (var item in Permissions)
        {
            item.IsSelected = ReferenceEquals(item, value);
        }
    }

    private void SeedConversation()
    {
        var assistant = new ChatMessageViewModel(Guid.NewGuid().ToString("N"), ChatRole.Assistant, "Codex");
        assistant.Blocks.Add(new ChatBlockViewModel(ChatBlockKind.Thought, "thinking", isExpanded: false));
        assistant.Blocks.Add(new ChatBlockViewModel("browser_use", ToolState.Completed,
            "## Page Title\n\nVisited https://example.com.\n\n- Status: 200\n- Content-Type: text/html\n\n```json\n{ \"title\": \"Example Domain\", \"snippet\": \"Reserved for examples.\" }\n```",
            isExpanded: false));
        assistant.Blocks.Add(new ChatBlockViewModel(ChatBlockKind.Text,
            "Done. Here's what shipped:\n\n- **Markdown** rendering via Markdig (code blocks, lists, headings, links, bold/italic).\n- A custom `CollapsibleBlockControl` for **Thinking** and **Tool** blocks with three states:\n  - Default — just an icon + label inline.\n  - Hover — light gray pill, chevron-down.\n  - Expanded — same pill + content panel below, chevron-up.\n\nUsage:\n\n```csharp\nnew ChatBlockViewModel(\"browser_use\", ToolState.Completed, output, isExpanded: false);\n```\n\nSee `Services/MarkdownRenderer.cs` for the renderer and `Controls/CollapsibleBlockControl.axaml` for the control."));
        Messages.Add(assistant);
    }
}
