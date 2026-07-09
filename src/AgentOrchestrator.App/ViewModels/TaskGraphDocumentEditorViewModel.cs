using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.TaskGraph;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.ViewModels;

/// <summary>
/// Unified document editing context for both template and runtime <see cref="TaskGraph"/> documents.
/// Serves as the single source of truth for the orchestration workspace's right-side editor panel,
/// exposing name editing, template rules (notes, planner prompt, dynamic zones), and save/instantiate
/// actions. The graph canvas is handled separately by <see cref="TaskGraphWorkspaceViewModel"/>.
/// </summary>
public sealed partial class TaskGraphDocumentEditorViewModel : ViewModelBase
{
    private readonly ITaskGraphStore _store;
    private readonly SidebarViewModel _sidebar;
    private TaskGraphModel? _currentDocument;

    public TaskGraphDocumentEditorViewModel(
        ITaskGraphStore store,
        SidebarViewModel sidebar)
    {
        _store = store;
        _sidebar = sidebar;
    }

    // ── Document identity ──────────────────────────────────────────────

    public TaskGraphModel? CurrentDocument
    {
        get => _currentDocument;
        private set
        {
            if (ReferenceEquals(_currentDocument, value))
            {
                return;
            }

            DetachDocumentSubscriptions(_currentDocument);
            _currentDocument = value;
            AttachDocumentSubscriptions(_currentDocument);

            if (_currentDocument is not null && IsTemplateDocument && _currentDocument.TemplateMetadata is null)
            {
                _currentDocument.TemplateMetadata = new TaskGraphTemplateMetadata();
            }

            RefreshDynamicZones();

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTemplateDocument));
            OnPropertyChanged(nameof(IsRuntimeDocument));
            OnPropertyChanged(nameof(IsBuiltInTemplate));
            OnPropertyChanged(nameof(HasDocument));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(IsReadOnly));
            OnPropertyChanged(nameof(TemplateNotesDraft));
            OnPropertyChanged(nameof(TemplatePlannerPromptDraft));
            OnPropertyChanged(nameof(AllowDynamicExpansion));
            OnPropertyChanged(nameof(DynamicZones));
            OnPropertyChanged(nameof(CanEditTemplateRules));
            OnPropertyChanged(nameof(CanInstantiate));
            OnPropertyChanged(nameof(CanExecute));
            OnPropertyChanged(nameof(CanSave));
        }
    }

    public bool IsTemplateDocument => CurrentDocument?.DocumentKind == TaskGraphDocumentKind.Template;

    public bool IsRuntimeDocument => CurrentDocument?.DocumentKind == TaskGraphDocumentKind.Runtime;

    public bool IsBuiltInTemplate => CurrentDocument?.IsBuiltInTemplate == true;

    public bool HasDocument => CurrentDocument is not null;

    public bool IsEmpty => !HasDocument;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>
    /// Raised after <see cref="InstantiateCommand"/> creates a runtime graph.
    /// The shell listens to this and decides whether to open the graph in the
    /// independent TaskGraph workspace, switch to chat, or do nothing.
    /// </summary>
    public event EventHandler<TaskGraphModel>? GraphInstantiated;

    // ── Editable properties ────────────────────────────────────────────

    /// <summary>
    /// Two-way bound to <see cref="TaskGraphModel.Name"/>. Returns an appropriate
    /// placeholder when the document is null.
    /// </summary>
    public string Name
    {
        get
        {
            if (CurrentDocument is null)
            {
                return string.Empty;
            }

            return CurrentDocument.Name;
        }
        set
        {
            if (CurrentDocument is not null && CurrentDocument.Name != value)
            {
                CurrentDocument.Name = string.IsNullOrWhiteSpace(value)
                    ? (IsTemplateDocument ? "未命名模板" : "未命名编排")
                    : value.Trim();
                OnPropertyChanged(nameof(Name));
            }
        }
    }

    public bool IsReadOnly => !HasDocument || IsBuiltInTemplate;

    public string TemplateNotesDraft
    {
        get => CurrentDocument?.TemplateNotes ?? string.Empty;
        set
        {
            if (CurrentDocument is not null)
            {
                CurrentDocument.TemplateNotes = value;
                OnPropertyChanged(nameof(TemplateNotesDraft));
            }
        }
    }

    public string TemplatePlannerPromptDraft
    {
        get => CurrentDocument?.TemplatePlannerPrompt ?? string.Empty;
        set
        {
            if (CurrentDocument is not null)
            {
                CurrentDocument.TemplatePlannerPrompt = value;
                OnPropertyChanged(nameof(TemplatePlannerPromptDraft));
            }
        }
    }

    public bool AllowDynamicExpansion
    {
        get => CurrentDocument?.TemplateMetadata?.AllowDynamicExpansion ?? false;
        set
        {
            if (CurrentDocument is not null)
            {
                EnsureTemplateMetadata().AllowDynamicExpansion = value;
                OnPropertyChanged(nameof(AllowDynamicExpansion));
            }
        }
    }

    public ObservableCollection<DynamicZoneDefinitionViewModel> DynamicZones { get; } = [];

    public bool CanEditTemplateRules => IsTemplateDocument && !IsReadOnly;

    public bool CanInstantiate => IsTemplateDocument && !IsBuiltInTemplate && HasDocument;

    public bool CanExecute => IsRuntimeDocument && HasDocument;

    public bool CanSave => HasDocument && !IsReadOnly;

    // ── Document loading ───────────────────────────────────────────────

    public async Task LoadDocumentByIdAsync(string id, CancellationToken ct = default)
    {
        var document = await _store.LoadTemplateAsync(id, ct).ConfigureAwait(true)
                       ?? await _store.LoadAsync(id, ct).ConfigureAwait(true);
        CurrentDocument = document;
    }

    public Task LoadDocumentAsync(TaskGraphModel? document, CancellationToken ct = default)
    {
        CurrentDocument = document;
        return Task.CompletedTask;
    }

    // ── Save ───────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (CurrentDocument is null)
        {
            return;
        }

        // Sync draft values back to the model in case they were set via direct property access.
        // The two-way bindings should keep these in sync, but this is a defensive measure.
        SyncDraftsToModel();
        SyncDynamicZonesToModel();

        await _store.SaveAsync(CurrentDocument).ConfigureAwait(true);
        await _sidebar.RefreshTaskGraphsAsync().ConfigureAwait(true);
        StatusText = "已保存。";
    }

    // ── Instantiate ────────────────────────────────────────────────────

    [RelayCommand]
    private async Task InstantiateAsync()
    {
        if (CurrentDocument is null || !IsTemplateDocument)
        {
            return;
        }

        var options = new TemplateInstantiationOptions
        {
            RuntimeGraphName = $"{CurrentDocument.Name} - 实例",
        };

        var runtime = await _store.InstantiateTemplateAsync(CurrentDocument.Id, options).ConfigureAwait(true);
        await _sidebar.RefreshTaskGraphsAsync().ConfigureAwait(true);
        StatusText = "已基于模板生成任务图。";
        GraphInstantiated?.Invoke(this, runtime);
    }

    /// <summary>
    /// Creates a runtime graph from the current template document and returns it.
    /// The caller decides what to do with the result (e.g. load it into the workspace).
    /// Does NOT auto-load the new graph into this editor.
    /// </summary>
    public async Task<TaskGraphModel> InstantiateAsync(TemplateInstantiationOptions options, CancellationToken ct = default)
    {
        if (CurrentDocument is null || !IsTemplateDocument)
        {
            throw new InvalidOperationException("当前文档不是模板，无法实例化。");
        }

        var runtime = await _store.InstantiateTemplateAsync(CurrentDocument.Id, options, ct).ConfigureAwait(true);
        GraphInstantiated?.Invoke(this, runtime);
        return runtime;
    }

    // ── Dynamic zones ──────────────────────────────────────────────────

    [RelayCommand]
    private void AddDynamicZone()
    {
        if (CurrentDocument is null)
        {
            return;
        }

        var metadata = EnsureTemplateMetadata();
        var zone = new DynamicZoneDefinition();
        metadata.DynamicZones.Add(zone);
        DynamicZones.Add(new DynamicZoneDefinitionViewModel(zone));
    }

    [RelayCommand]
    private void RemoveDynamicZone(DynamicZoneDefinitionViewModel? zone)
    {
        if (CurrentDocument is null || zone is null)
        {
            return;
        }

        var metadata = CurrentDocument.TemplateMetadata;
        if (metadata is null)
        {
            return;
        }

        var matching = metadata.DynamicZones.FirstOrDefault(z =>
            string.Equals(z.Id, zone.Zone.Id, StringComparison.Ordinal));
        if (matching is not null)
        {
            metadata.DynamicZones.Remove(matching);
        }

        DynamicZones.Remove(zone);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private TaskGraphTemplateMetadata EnsureTemplateMetadata()
    {
        if (CurrentDocument is null)
        {
            throw new InvalidOperationException("当前没有加载文档。");
        }

        if (CurrentDocument.TemplateMetadata is null)
        {
            CurrentDocument.TemplateMetadata = new TaskGraphTemplateMetadata();
        }

        return CurrentDocument.TemplateMetadata;
    }

    private void RefreshDynamicZones()
    {
        DynamicZones.Clear();
        if (CurrentDocument?.TemplateMetadata?.DynamicZones is { } zones)
        {
            foreach (var zone in zones)
            {
                DynamicZones.Add(new DynamicZoneDefinitionViewModel(zone));
            }
        }
    }

    private void SyncDraftsToModel()
    {
        if (CurrentDocument is null)
        {
            return;
        }

        if (CurrentDocument.TemplateMetadata is not null)
        {
            CurrentDocument.TemplateMetadata.AllowDynamicExpansion = AllowDynamicExpansion;
        }
    }

    private void SyncDynamicZonesToModel()
    {
        // Each DynamicZoneDefinitionViewModel already syncs its changes to the underlying
        // DynamicZoneDefinition via partial OnXxxChanged methods. This is a no-op in practice,
        // but kept for defensive clarity.
    }

    private void AttachDocumentSubscriptions(TaskGraphModel? document)
    {
        if (document is not null)
        {
            document.PropertyChanged += OnDocumentPropertyChanged;
        }
    }

    private void DetachDocumentSubscriptions(TaskGraphModel? document)
    {
        if (document is not null)
        {
            document.PropertyChanged -= OnDocumentPropertyChanged;
        }
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TaskGraphModel.TemplateNotes))
        {
            OnPropertyChanged(nameof(TemplateNotesDraft));
        }
        else if (e.PropertyName is nameof(TaskGraphModel.TemplatePlannerPrompt))
        {
            OnPropertyChanged(nameof(TemplatePlannerPromptDraft));
        }
        else if (e.PropertyName is nameof(TaskGraphModel.Name))
        {
            OnPropertyChanged(nameof(Name));
        }
        else if (e.PropertyName is nameof(TaskGraphModel.DocumentKind))
        {
            OnPropertyChanged(nameof(IsTemplateDocument));
            OnPropertyChanged(nameof(IsRuntimeDocument));
            OnPropertyChanged(nameof(CanEditTemplateRules));
            OnPropertyChanged(nameof(CanInstantiate));
            OnPropertyChanged(nameof(CanExecute));
        }
    }
}
