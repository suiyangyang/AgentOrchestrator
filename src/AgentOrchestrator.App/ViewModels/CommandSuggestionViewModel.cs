using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentOrchestrator.App.ViewModels;

public partial class CommandSuggestionViewModel : ObservableObject
{
    public CommandSuggestionViewModel(
        string name,
        string insertText,
        string? description,
        bool isBuiltIn = false)
    {
        Name = name;
        InsertText = insertText;
        Description = description;
        IsBuiltIn = isBuiltIn;
    }

    public string Name { get; }

    public string InsertText { get; }

    public string? Description { get; }

    public bool IsBuiltIn { get; }

    [ObservableProperty]
    private bool _isSelected;
}
