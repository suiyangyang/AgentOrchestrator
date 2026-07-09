using System.Collections.Generic;

namespace AgentOrchestrator.App.Models.TaskGraph;

/// <summary>
/// Structured template metadata carried on a <see cref="TaskGraph"/> whose <see cref="TaskGraph.DocumentKind"/> is <see cref="TaskGraphDocumentKind.Template"/>.
/// Describes fixed structural elements, dynamic expansion zones, generation rules, and execution constraints.
/// </summary>
public sealed class TaskGraphTemplateMetadata
{
    public bool AllowDynamicExpansion { get; set; }

    public string? ExpansionEntryNodeId { get; set; }

    public string? ExpansionTerminalNodeId { get; set; }

    public List<string> FixedNodeIds { get; set; } = [];

    public List<string> FixedEdgeKeys { get; set; } = [];

    public List<DynamicZoneDefinition> DynamicZones { get; set; } = [];

    public List<string> NodeGenerationRules { get; set; } = [];

    public List<string> EdgeGenerationRules { get; set; } = [];

    public List<string> ExecutionRules { get; set; } = [];
}
