using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentOrchestrator.App.Models.Chat;

public partial class PermissionOption : ObservableObject
{
    public PermissionOption(string key, string title, string description, string icon)
    {
        Key = key;
        Title = title;
        Description = description;
        Icon = icon;
    }

    public string Key { get; }

    public string Title { get; }

    public string Description { get; }

    public string Icon { get; }

    [ObservableProperty]
    private bool _isSelected;
}
