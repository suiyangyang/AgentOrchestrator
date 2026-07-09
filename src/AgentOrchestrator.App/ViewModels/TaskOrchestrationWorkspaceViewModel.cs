using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.TaskGraph;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentOrchestrator.App.ViewModels;

/// <summary>
/// Owns the task orchestration independent workspace. Left panel: quick actions +
/// two collapsible groups (模板 / 任务图). Right panel: unified graph canvas
/// (via <see cref="Workspace"/>) with editor panel (via <see cref="Editor"/>).
/// Phase 3 removes the separate template detail form and task graph summary form;
/// both modes now share the same graph canvas and document editor.
/// </summary>
public sealed partial class TaskOrchestrationWorkspaceViewModel : ViewModelBase
{
    private readonly ITaskGraphStore _taskGraphStore;
    private readonly TaskGraphWorkspaceViewModel _workspace;
    private readonly TaskGraphDocumentEditorViewModel _editor;
    private readonly Dictionary<string, SidebarTaskGraphItemViewModel> _templatesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SidebarTaskGraphItemViewModel> _taskGraphsById = new(StringComparer.Ordinal);
    private IReadOnlyList<TaskGraphListItem> _allTemplateItems = [];
    private IReadOnlyList<TaskGraphListItem> _allTaskGraphItems = [];

    public TaskOrchestrationWorkspaceViewModel(
        ITaskGraphStore taskGraphStore,
        TaskGraphWorkspaceViewModel workspace,
        TaskGraphDocumentEditorViewModel editor)
    {
        _taskGraphStore = taskGraphStore;
        _workspace = workspace;
        _editor = editor;
        _ = InitializeAsync();
    }

    public string Title => "任务编排";
    public string HeaderTitle => "任务编排";

    public string SearchPlaceholder => "搜索模板和任务图";

    public ObservableCollection<SidebarTaskGraphItemViewModel> Templates { get; } = [];
    public ObservableCollection<SidebarTaskGraphItemViewModel> TaskGraphs { get; } = [];

    /// <summary>
    /// The shared graph canvas view model. Both template and runtime documents
    /// are displayed on the same canvas, with execution buttons gated by
    /// <see cref="TaskGraphWorkspaceViewModel.IsRuntimeDocument"/>.
    /// </summary>
    public TaskGraphWorkspaceViewModel Workspace => _workspace;

    /// <summary>
    /// The unified document editor context. Drives the right-side editor panel
    /// (name, template rules, dynamic zones, save/instantiate actions).
    /// </summary>
    public TaskGraphDocumentEditorViewModel Editor => _editor;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _areTemplatesExpanded = true;

    [ObservableProperty]
    private bool _areTaskGraphsExpanded = true;

    [ObservableProperty]
    private bool _isBusy;

    // ---- Selection state ----

    [ObservableProperty]
    private SidebarTaskGraphItemViewModel? _selectedTemplate;

    [ObservableProperty]
    private SidebarTaskGraphItemViewModel? _selectedTaskGraph;

    // ---- Template kind options for the ComboBox ----

    public static IReadOnlyList<TaskGraphTemplateKind> TemplateKindOptions { get; } =
    [
        TaskGraphTemplateKind.TaskList,
        TaskGraphTemplateKind.FeatureDevelopment,
        TaskGraphTemplateKind.BugList,
        TaskGraphTemplateKind.Custom,
    ];

    public static string TemplateKindDisplayName(TaskGraphTemplateKind kind) => kind switch
    {
        TaskGraphTemplateKind.TaskList => "任务列表",
        TaskGraphTemplateKind.FeatureDevelopment => "功能开发",
        TaskGraphTemplateKind.BugList => "Bug 列表",
        _ => "自定义",
    };

    // ---- Events exposed to the shell ----

    public event EventHandler? NewTaskGraphRequested;
    public event EventHandler<string>? TaskGraphOpenRequested;
    public event EventHandler<TaskGraphActionRequest>? TaskGraphActionRequested;

    // ================================================================
    // Initialization
    // ================================================================

    private async Task InitializeAsync()
    {
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var templates = await _taskGraphStore.ListTemplatesAsync().ConfigureAwait(true);
            var taskGraphs = await _taskGraphStore.ListRuntimeGraphsAsync().ConfigureAwait(true);

            _allTemplateItems = templates;
            _allTaskGraphItems = taskGraphs;

            ApplyFilter();

            // Re-load the current selection into the editor and workspace
            // in case the underlying data changed.
            if (SelectedTemplate is not null)
            {
                _ = LoadTemplateIntoEditorAsync(SelectedTemplate.Id);
                _ = _workspace.OpenTemplateByIdAsync(SelectedTemplate.Id);
            }
            else if (SelectedTaskGraph is not null)
            {
                _ = LoadTaskGraphIntoEditorAsync(SelectedTaskGraph.Id);
                _ = _workspace.OpenGraphByIdAsync(SelectedTaskGraph.Id);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ================================================================
    // Quick actions
    // ================================================================

    [RelayCommand]
    private void NewTaskGraph()
    {
        NewTaskGraphRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task NewTemplateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            // Create a minimal template via the builder, then save.
            var graph = TaskGraphTemplateBuilder.Build(TaskGraphTemplateKind.Custom, string.Empty, TaskGraphDocumentKind.Template);
            graph.Id = Guid.NewGuid().ToString("N");
            graph.Name = "新模板";
            graph.IsBuiltInTemplate = false;
            graph.TemplateMetadata = new TaskGraphTemplateMetadata
            {
                AllowDynamicExpansion = true,
            };
            await _taskGraphStore.SaveAsync(graph).ConfigureAwait(true);
            await RefreshAsync();

            if (_templatesById.TryGetValue(graph.Id, out var vm))
            {
                SelectTemplate(vm);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ================================================================
    // Selection
    // ================================================================

    [RelayCommand]
    private void SelectTemplate(SidebarTaskGraphItemViewModel? template)
    {
        if (ReferenceEquals(SelectedTemplate, template))
        {
            return;
        }

        // Clear previous selection
        if (SelectedTemplate is not null)
        {
            SelectedTemplate.IsSelected = false;
        }

        if (SelectedTaskGraph is not null)
        {
            SelectedTaskGraph.IsSelected = false;
            SelectedTaskGraph = null;
        }

        SelectedTemplate = template;
        if (template is not null)
        {
            template.IsSelected = true;
            _ = LoadTemplateIntoEditorAsync(template.Id);
            _ = _workspace.OpenTemplateByIdAsync(template.Id);
        }
        else
        {
            _ = _editor.LoadDocumentAsync(null);
            _workspace.CurrentGraph = null;
        }
    }

    [RelayCommand]
    private void SelectTaskGraph(SidebarTaskGraphItemViewModel? taskGraph)
    {
        if (ReferenceEquals(SelectedTaskGraph, taskGraph))
        {
            return;
        }

        // Clear previous selection
        if (SelectedTaskGraph is not null)
        {
            SelectedTaskGraph.IsSelected = false;
        }

        if (SelectedTemplate is not null)
        {
            SelectedTemplate.IsSelected = false;
            SelectedTemplate = null;
        }

        SelectedTaskGraph = taskGraph;
        if (taskGraph is not null)
        {
            taskGraph.IsSelected = true;
            _ = LoadTaskGraphIntoEditorAsync(taskGraph.Id);
            _ = _workspace.OpenGraphByIdAsync(taskGraph.Id);
        }
        else
        {
            _ = _editor.LoadDocumentAsync(null);
            _workspace.CurrentGraph = null;
        }
    }

    // ================================================================
    // Template detail actions (kept for sidebar interactions)
    // ================================================================

    [RelayCommand]
    private async Task DeleteTemplateAsync(SidebarTaskGraphItemViewModel? template)
    {
        if (template is null)
        {
            return;
        }

        if (template.IsBuiltIn)
        {
            return;
        }

        try
        {
            await _taskGraphStore.DeleteAsync(template.Id).ConfigureAwait(true);
        }
        catch (InvalidOperationException)
        {
            // Deletion of built-in was rejected.
        }

        if (SelectedTemplate is not null && SelectedTemplate.Id == template.Id)
        {
            SelectTemplate(null!);
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DuplicateTemplateAsync(SidebarTaskGraphItemViewModel? template)
    {
        if (template is null)
        {
            return;
        }

        var source = await _taskGraphStore.LoadTemplateAsync(template.Id).ConfigureAwait(true);
        if (source is null)
        {
            return;
        }

        // Use JSON roundtrip to deep-clone (same approach as TaskGraphTemplateInstantiator).
        var cloneOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };
        var cloneJson = JsonSerializer.Serialize(source, cloneOptions);
        var clone = JsonSerializer.Deserialize<TaskGraph>(cloneJson, cloneOptions);
        if (clone is null)
        {
            return;
        }

        clone.Id = Guid.NewGuid().ToString("N");
        clone.Name = $"{source.Name} 副本";
        clone.IsBuiltInTemplate = false;
        await _taskGraphStore.SaveAsync(clone).ConfigureAwait(true);
        await RefreshAsync();

        if (_templatesById.TryGetValue(clone.Id, out var vm))
        {
            SelectTemplate(vm);
        }
    }

    /// <summary>
    /// Initiate inline rename on the selected template row.
    /// </summary>
    [RelayCommand]
    private void BeginRenameTemplate(SidebarTaskGraphItemViewModel? template)
    {
        if (template is null || template.IsBuiltIn)
        {
            return;
        }

        template.IsEditing = true;
    }

    /// <summary>
    /// Commits an inline rename from the row's TextBox.
    /// </summary>
    [RelayCommand]
    private async Task CommitTemplateRenameAsync(SidebarTaskGraphItemViewModel? template)
    {
        if (template is null || template.IsBuiltIn)
        {
            return;
        }

        var graph = await _taskGraphStore.LoadTemplateAsync(template.Id).ConfigureAwait(true);
        if (graph is null)
        {
            return;
        }

        graph.Name = string.IsNullOrWhiteSpace(template.Name) ? "未命名模板" : template.Name.Trim();

        await _taskGraphStore.SaveAsync(graph).ConfigureAwait(true);
        template.IsEditing = false;
        await RefreshAsync();
    }

    // ================================================================
    // Task graph actions (delegated to shell via events)
    // ================================================================

    [RelayCommand]
    private void RenameTaskGraph(SidebarTaskGraphItemViewModel? taskGraph)
    {
        if (taskGraph is null)
        {
            return;
        }

        TaskGraphActionRequested?.Invoke(this, new TaskGraphActionRequest(taskGraph.Id, TaskGraphActionKind.Rename));
    }

    [RelayCommand]
    private void DeleteTaskGraph(SidebarTaskGraphItemViewModel? taskGraph)
    {
        if (taskGraph is null)
        {
            return;
        }

        TaskGraphActionRequested?.Invoke(this, new TaskGraphActionRequest(taskGraph.Id, TaskGraphActionKind.Remove));
    }

    [RelayCommand]
    private void ChangeTaskGraphProject(SidebarTaskGraphItemViewModel? taskGraph)
    {
        if (taskGraph is null)
        {
            return;
        }

        TaskGraphActionRequested?.Invoke(this, new TaskGraphActionRequest(taskGraph.Id, TaskGraphActionKind.ChangeProject));
    }

    // ================================================================
    // Expand / collapse
    // ================================================================

    [RelayCommand]
    private void ToggleTemplatesExpanded()
    {
        AreTemplatesExpanded = !AreTemplatesExpanded;
    }

    [RelayCommand]
    private void ToggleTaskGraphsExpanded()
    {
        AreTaskGraphsExpanded = !AreTaskGraphsExpanded;
    }

    // ================================================================
    // Search
    // ================================================================

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    // ================================================================
    // Editor loading helpers
    // ================================================================

    private async Task LoadTemplateIntoEditorAsync(string templateId)
    {
        if (_editor is null)
        {
            return;
        }

        await _editor.LoadDocumentByIdAsync(templateId).ConfigureAwait(true);
    }

    private async Task LoadTaskGraphIntoEditorAsync(string graphId)
    {
        if (_editor is null)
        {
            return;
        }

        var graph = await _taskGraphStore.LoadAsync(graphId).ConfigureAwait(true);
        if (graph is null || graph.DocumentKind != TaskGraphDocumentKind.Runtime)
        {
            return;
        }

        await _editor.LoadDocumentAsync(graph).ConfigureAwait(true);
    }

    /// <summary>
    /// Kept as a no-op for backward compatibility with the sidebar control's
    /// code-behind double-click handler. The graph is already loaded into the
    /// editor and workspace when selected — no separate "open" step is needed.
    /// </summary>
    [RelayCommand]
    private void OpenSelectedTaskGraphInEditor()
    {
        // No-op: the graph is already loaded into the editor when selected.
    }

    // ================================================================
    // Internals
    // ================================================================

    private void ApplyFilter()
    {
        var query = (SearchText ?? string.Empty).Trim();

        Templates.Clear();
        _templatesById.Clear();

        foreach (var item in _allTemplateItems
            .Where(t => query.Length == 0 || t.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.IsBuiltIn ? 0 : 1)
            .ThenByDescending(t => t.UpdatedAt))
        {
            bool wasSelected = SelectedTemplate is not null &&
                string.Equals(SelectedTemplate.Id, item.Id, StringComparison.Ordinal);
            var vm = new SidebarTaskGraphItemViewModel(item) { IsSelected = wasSelected };
            Templates.Add(vm);
            _templatesById[item.Id] = vm;

            if (wasSelected)
            {
                SelectedTemplate = vm;
            }
        }

        TaskGraphs.Clear();
        _taskGraphsById.Clear();

        foreach (var item in _allTaskGraphItems
            .Where(t => query.Length == 0 || t.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.UpdatedAt))
        {
            bool wasSelected = SelectedTaskGraph is not null &&
                string.Equals(SelectedTaskGraph.Id, item.Id, StringComparison.Ordinal);
            var vm = new SidebarTaskGraphItemViewModel(item) { IsSelected = wasSelected };
            TaskGraphs.Add(vm);
            _taskGraphsById[item.Id] = vm;

            if (wasSelected)
            {
                SelectedTaskGraph = vm;
            }
        }
    }
}
