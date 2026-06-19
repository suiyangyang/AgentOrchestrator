namespace AgentOrchestrator.App.ViewModels;

public sealed class TaskGraphTemplateOptionViewModel
{
    public TaskGraphTemplateOptionViewModel(string key, string title, string description)
    {
        Key = key;
        Title = title;
        Description = description;
    }

    public string Key { get; }

    public string Title { get; }

    public string Description { get; }
}
