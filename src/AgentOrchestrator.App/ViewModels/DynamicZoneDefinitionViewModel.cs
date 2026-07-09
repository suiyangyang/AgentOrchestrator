using System;
using AgentOrchestrator.App.Models.TaskGraph;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentOrchestrator.App.ViewModels;

/// <summary>
/// Wrapper view model around <see cref="DynamicZoneDefinition"/> so the orchestration UI
/// can edit dynamic zone properties via two-way bindings. Each property setter automatically
/// syncs the change back to the underlying <see cref="DynamicZoneDefinition"/> so that the
/// VM serves as a live editor — saving the parent document persists all zone edits.
/// The <see cref="Id"/> property is a stable key for <c>ObservableCollection</c> diffing
/// and is unrelated to <see cref="DynamicZoneDefinition.Id"/> (the persistent model id).
/// </summary>
public sealed partial class DynamicZoneDefinitionViewModel : ObservableObject
{
    /// <summary>
    /// Stable VM-level key for <c>ObservableCollection</c> diffing. Not persisted; distinct
    /// from <see cref="DynamicZoneDefinition.Id"/>.
    /// </summary>
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _anchorNodeId = string.Empty;

    [ObservableProperty]
    private string? _insertAfterNodeId;

    [ObservableProperty]
    private string? _connectToTerminalNodeId;

    [ObservableProperty]
    private bool _allowParallelNodes;

    [ObservableProperty]
    private int _maxGeneratedNodeCount = 12;

    [ObservableProperty]
    private string _generationInstruction = string.Empty;

    /// <summary>
    /// The underlying model object this VM wraps. All property setters sync changes
    /// back to this instance so the parent document stays consistent.
    /// </summary>
    public DynamicZoneDefinition Zone { get; }

    public DynamicZoneDefinitionViewModel(DynamicZoneDefinition zone)
    {
        Zone = zone;
        _name = zone.Name;
        _anchorNodeId = zone.AnchorNodeId;
        _insertAfterNodeId = zone.InsertAfterNodeId;
        _connectToTerminalNodeId = zone.ConnectToTerminalNodeId;
        _allowParallelNodes = zone.AllowParallelNodes;
        _maxGeneratedNodeCount = zone.MaxGeneratedNodeCount;
        _generationInstruction = zone.GenerationInstruction;
    }

    partial void OnNameChanged(string value)
    {
        Zone.Name = value;
    }

    partial void OnAnchorNodeIdChanged(string value)
    {
        Zone.AnchorNodeId = value;
    }

    partial void OnInsertAfterNodeIdChanged(string? value)
    {
        Zone.InsertAfterNodeId = value;
    }

    partial void OnConnectToTerminalNodeIdChanged(string? value)
    {
        Zone.ConnectToTerminalNodeId = value;
    }

    partial void OnAllowParallelNodesChanged(bool value)
    {
        Zone.AllowParallelNodes = value;
    }

    partial void OnMaxGeneratedNodeCountChanged(int value)
    {
        Zone.MaxGeneratedNodeCount = value;
    }

    partial void OnGenerationInstructionChanged(string value)
    {
        Zone.GenerationInstruction = value;
    }
}
