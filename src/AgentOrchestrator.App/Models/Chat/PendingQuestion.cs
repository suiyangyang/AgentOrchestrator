using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentOrchestrator.App.Models.Chat;

public partial class PendingQuestion : ObservableObject
{
    public PendingQuestion(string requestId, string title)
    {
        RequestId = requestId;
        Title = title;
    }

    public string RequestId { get; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _promptText = string.Empty;

    [ObservableProperty]
    private bool _multipleSelection;

    [ObservableProperty]
    private bool _allowCustomAnswer;

    [ObservableProperty]
    private string _customAnswer = string.Empty;

    [ObservableProperty]
    private bool _isExpanded;

    public ObservableCollection<PendingQuestionItem> Questions { get; } = [];
}

public partial class PendingQuestionItem : ObservableObject
{
    public PendingQuestionItem(string id, string header, string question)
    {
        Id = id;
        Header = header;
        Question = question;
    }

    public string Id { get; }

    [ObservableProperty]
    private string _header;

    [ObservableProperty]
    private string _question;

    public ObservableCollection<PendingQuestionOption> Options { get; } = [];
}

public partial class PendingQuestionOption : ObservableObject
{
    public PendingQuestionOption(string label, string? description, string? value)
    {
        Label = label;
        Description = description;
        Value = value;
    }

    public string Label { get; }

    public string? Description { get; }

    public string? Value { get; }

    [ObservableProperty]
    private bool _isSelected;
}
