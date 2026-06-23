using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentOrchestrator.App.Models.TaskGraph;

/// <summary>
/// Template is a generation asset (not an execution entity). A Template helps
/// the user generate a TaskGraph from a base skeleton + name + description.
/// Stored in JSON files in the %LOCALAPPDATA%/AgentOrchestrator/Templates
/// directory.
/// </summary>
public sealed partial class TaskTemplate : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _name = "未命名模板";

    [ObservableProperty]
    private string _description = string.Empty;

    /// <summary>Base skeleton (task-list | feature-dev | bug-list | custom).</summary>
    [ObservableProperty]
    private TaskGraphTemplateKind _baseKind = TaskGraphTemplateKind.TaskList;

    /// <summary>
    /// Default input the template will be populated with. Free text
    /// or a hint shown in the UI; generation uses the TaskGraphTemplateBuilder
    /// on this text (the user can edit it before generating the TaskGraph).
    /// </summary>
    [ObservableProperty]
    private string _defaultInput = "- 任务 1\n- 任务 2\n- 任务 3";

    /// <summary>
    /// True for the 3 official built-in templates. Built-ins cannot
    /// be deleted; they can be "duplicated" to create a custom copy.
    /// </summary>
    [ObservableProperty]
    private bool _isBuiltIn;

    [ObservableProperty]
    private DateTimeOffset _createdAt = DateTimeOffset.UtcNow;

    [ObservableProperty]
    private DateTimeOffset _updatedAt = DateTimeOffset.UtcNow;
}
