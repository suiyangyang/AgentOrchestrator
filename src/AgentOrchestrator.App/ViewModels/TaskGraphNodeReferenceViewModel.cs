namespace AgentOrchestrator.App.ViewModels;

public sealed class TaskGraphNodeReferenceViewModel
{
    public TaskGraphNodeReferenceViewModel(string id, string title)
    {
        Id = id;
        Title = title;
    }

    public string Id { get; }

    public string Title { get; }
}
