using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.TaskGraph;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentOrchestrator.App.ViewModels;

/// <summary>
/// Owns the task orchestration independent workspace. Left panel: quick actions +
/// two collapsible groups (模板 / 任务图). Right panel: template detail or task graph
/// summary, switched via selection.
/// </summary>
public sealed partial class TaskOrchestrationWorkspaceViewModel : ViewModelBase
{
    private readonly ITaskTemplateStore _templateStore;
    private readonly ITaskGraphStore _taskGraphStore;
    private readonly Dictionary<string, TaskTemplateItemViewModel> _templatesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SidebarTaskGraphItemViewModel> _taskGraphsById = new(StringComparer.Ordinal);
    private IReadOnlyList<TaskTemplateListItem> _allTemplateItems = [];
    private IReadOnlyList<TaskGraphListItem> _allTaskGraphItems = [];

    public TaskOrchestrationWorkspaceViewModel(
        ITaskTemplateStore templateStore,
        ITaskGraphStore taskGraphStore)
    {
        _templateStore = templateStore;
        _taskGraphStore = taskGraphStore;
        _ = InitializeAsync();
    }

    public string Title => "任务编排";
    public string HeaderTitle => "任务编排";

    public string SearchPlaceholder => "搜索模板和任务图";

    public ObservableCollection<TaskTemplateItemViewModel> Templates { get; } = [];
    public ObservableCollection<SidebarTaskGraphItemViewModel> TaskGraphs { get; } = [];

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
    private TaskTemplateItemViewModel? _selectedTemplate;

    [ObservableProperty]
    private SidebarTaskGraphItemViewModel? _selectedTaskGraph;

    public bool IsTemplateSelected => SelectedTemplate is not null;
    public bool IsTaskGraphSelected => SelectedTaskGraph is not null;
    public bool IsEmpty => SelectedTemplate is null && SelectedTaskGraph is null;

    // ---- Template detail bindings (when a template is selected) ----

    [ObservableProperty]
    private string _templateDetailName = string.Empty;

    [ObservableProperty]
    private string _templateDetailDescription = string.Empty;

    [ObservableProperty]
    private TaskGraphTemplateKind _templateDetailBaseKind = TaskGraphTemplateKind.TaskList;

    [ObservableProperty]
    private string _templateDetailDefaultInput = string.Empty;

    [ObservableProperty]
    private bool _isTemplateDetailBuiltIn;

    [ObservableProperty]
    private string _templateGenerationInput = string.Empty;

    [ObservableProperty]
    private string _templateGenerationGraphName = string.Empty;

    // ---- Task graph summary bindings (when a task graph is selected) ----

    [ObservableProperty]
    private string _selectedTaskGraphName = string.Empty;

    [ObservableProperty]
    private string _selectedTaskGraphStatusText = "草稿";

    [ObservableProperty]
    private int _selectedTaskGraphNodeCount;

    [ObservableProperty]
    private string _selectedTaskGraphUpdatedText = string.Empty;

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
    public event EventHandler<TaskTemplateGenerationRequest>? TemplateGenerateRequested;
    public event EventHandler? TemplateFocusRequested;

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
            var templates = await _templateStore.ListAsync().ConfigureAwait(true);
            var taskGraphs = await _taskGraphStore.ListAsync().ConfigureAwait(true);

            _allTemplateItems = templates;
            _allTaskGraphItems = taskGraphs;

            ApplyFilter();
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
            var template = new TaskTemplate
            {
                Name = "新模板",
                Description = string.Empty,
                BaseKind = TaskGraphTemplateKind.TaskList,
                DefaultInput = "- 任务 1\n- 任务 2\n- 任务 3",
                IsBuiltIn = false,
            };

            await _templateStore.SaveAsync(template).ConfigureAwait(true);
            await RefreshAsync();

            if (_templatesById.TryGetValue(template.Id, out var vm))
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
    private void SelectTemplate(TaskTemplateItemViewModel? template)
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
            PopulateTemplateDetail(template);
        }

        OnPropertyChanged(nameof(IsTemplateSelected));
        OnPropertyChanged(nameof(IsTaskGraphSelected));
        OnPropertyChanged(nameof(IsEmpty));
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
            PopulateTaskGraphSummary(taskGraph);
        }

        OnPropertyChanged(nameof(IsTemplateSelected));
        OnPropertyChanged(nameof(IsTaskGraphSelected));
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// Called by the control when the "打开图编辑" button is clicked in the
    /// right-pane task graph summary. Raises <see cref="TaskGraphOpenRequested"/>
    /// so the shell can switch to the full graph workspace.
    /// </summary>
    [RelayCommand]
    private void OpenSelectedTaskGraphInEditor()
    {
        if (SelectedTaskGraph is null)
        {
            return;
        }

        TaskGraphOpenRequested?.Invoke(this, SelectedTaskGraph.Id);
    }

    // ================================================================
    // Template detail actions
    // ================================================================

    [RelayCommand]
    private async Task SaveTemplateDetailAsync()
    {
        if (SelectedTemplate is null)
        {
            return;
        }

        var template = await _templateStore.LoadAsync(SelectedTemplate.Id).ConfigureAwait(true);
        if (template is null)
        {
            return;
        }

        template.Name = string.IsNullOrWhiteSpace(TemplateDetailName) ? "未命名模板" : TemplateDetailName.Trim();
        template.Description = TemplateDetailDescription?.Trim() ?? string.Empty;
        template.BaseKind = TemplateDetailBaseKind;
        template.DefaultInput = TemplateDetailDefaultInput?.Trim() ?? string.Empty;

        await _templateStore.SaveAsync(template).ConfigureAwait(true);
        await RefreshAsync();

        if (_templatesById.TryGetValue(template.Id, out var vm))
        {
            SelectTemplate(vm);
        }
    }

    [RelayCommand]
    private void GenerateTaskGraphFromSelectedTemplate()
    {
        if (SelectedTemplate is null)
        {
            return;
        }

        var input = string.IsNullOrWhiteSpace(TemplateGenerationInput)
            ? TemplateDetailDefaultInput
            : TemplateGenerationInput.Trim();

        TemplateGenerateRequested?.Invoke(this, new TaskTemplateGenerationRequest(
            SelectedTemplate.Id,
            TemplateDetailName,
            TemplateDetailBaseKind,
            input,
            string.IsNullOrWhiteSpace(TemplateGenerationGraphName) ? null : TemplateGenerationGraphName.Trim()));
    }

    /// <summary>
    /// Initiate inline rename on the selected template row.
    /// The control's pointer handler toggles IsEditing on the VM,
    /// and the inline TextBox commits the rename.
    /// </summary>
    [RelayCommand]
    private void BeginRenameTemplate(TaskTemplateItemViewModel? template)
    {
        if (template is null || template.IsBuiltIn)
        {
            return;
        }

        template.IsEditing = true;
    }

    /// <summary>
    /// Commits an inline rename from the row's TextBox.
    /// Called by the control when Enter is pressed or focus is lost.
    /// </summary>
    [RelayCommand]
    private async Task CommitTemplateRenameAsync(TaskTemplateItemViewModel? template)
    {
        if (template is null || template.IsBuiltIn)
        {
            return;
        }

        var storeTemplate = await _templateStore.LoadAsync(template.Id).ConfigureAwait(true);
        if (storeTemplate is null)
        {
            return;
        }

        storeTemplate.Name = string.IsNullOrWhiteSpace(template.Name) ? "未命名模板" : template.Name.Trim();

        await _templateStore.SaveAsync(storeTemplate).ConfigureAwait(true);
        template.IsEditing = false;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeleteTemplateAsync(TaskTemplateItemViewModel? template)
    {
        if (template is null)
        {
            return;
        }

        if (template.IsBuiltIn)
        {
            // Built-in templates cannot be deleted.
            return;
        }

        try
        {
            await _templateStore.DeleteAsync(template.Id).ConfigureAwait(true);
        }
        catch (InvalidOperationException)
        {
            // Deletion of built-in was rejected.
        }

        if (SelectedTemplate == template)
        {
            SelectTemplate(null!);
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DuplicateTemplateAsync(TaskTemplateItemViewModel? template)
    {
        if (template is null)
        {
            return;
        }

        var source = await _templateStore.LoadAsync(template.Id).ConfigureAwait(true);
        if (source is null)
        {
            return;
        }

        var duplicate = new TaskTemplate
        {
            Name = $"{source.Name} 副本",
            Description = source.Description,
            BaseKind = source.BaseKind,
            DefaultInput = source.DefaultInput,
            IsBuiltIn = false,
        };

        await _templateStore.SaveAsync(duplicate).ConfigureAwait(true);
        await RefreshAsync();

        if (_templatesById.TryGetValue(duplicate.Id, out var vm))
        {
            SelectTemplate(vm);
        }
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
            var vm = new TaskTemplateItemViewModel(item) { IsSelected = wasSelected };
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

    private void PopulateTemplateDetail(TaskTemplateItemViewModel vm)
    {
        // For a richer detail view, load the full template from the store.
        // But for immediate UI responsiveness, use the lightweight list data first.
        TemplateDetailName = vm.Name;
        TemplateDetailDescription = string.Empty;
        TemplateDetailBaseKind = vm.BaseKind;
        IsTemplateDetailBuiltIn = vm.IsBuiltIn;
        TemplateGenerationInput = string.Empty;
        TemplateGenerationGraphName = string.Empty;

        // Fire-and-forget: load full template details asynchronously.
        _ = PopulateTemplateDetailAsync(vm.Id);
    }

    private async Task PopulateTemplateDetailAsync(string templateId)
    {
        var template = await _templateStore.LoadAsync(templateId).ConfigureAwait(true);
        if (template is null || SelectedTemplate is null || !string.Equals(SelectedTemplate.Id, templateId, StringComparison.Ordinal))
        {
            return;
        }

        TemplateDetailName = template.Name;
        TemplateDetailDescription = template.Description;
        TemplateDetailBaseKind = template.BaseKind;
        TemplateDetailDefaultInput = template.DefaultInput;
        IsTemplateDetailBuiltIn = template.IsBuiltIn;
        TemplateGenerationInput = template.DefaultInput;
        TemplateGenerationGraphName = string.Empty;
    }

    private void PopulateTaskGraphSummary(SidebarTaskGraphItemViewModel vm)
    {
        SelectedTaskGraphName = vm.Name;
        SelectedTaskGraphStatusText = vm.ExecutionStateText;
        SelectedTaskGraphNodeCount = vm.NodeCount;
        SelectedTaskGraphUpdatedText = vm.UpdatedAtText;
    }

    public void PrepareTemplateGenerationFromChat(string userInput)
    {
        if (SelectedTemplate is null && Templates.Count > 0)
        {
            SelectTemplate(Templates[0]);
        }

        if (!string.IsNullOrWhiteSpace(userInput))
        {
            TemplateGenerationInput = userInput.Trim();
        }
    }

    public void RequestTemplateFocus()
    {
        TemplateFocusRequested?.Invoke(this, EventArgs.Empty);
    }
}

public sealed record TaskTemplateGenerationRequest(
    string TemplateId,
    string TemplateName,
    TaskGraphTemplateKind BaseKind,
    string Input,
    string? GraphName);
