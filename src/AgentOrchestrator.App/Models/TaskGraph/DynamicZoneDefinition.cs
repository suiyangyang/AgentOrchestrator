using System;
using System.Collections.Generic;

namespace AgentOrchestrator.App.Models.TaskGraph;

/// <summary>
/// Declares a region within a template where nodes may be dynamically generated at instantiation time.
/// Each zone specifies its anchor, insertion point, connectivity target, size limit, and generation instruction.
/// </summary>
public sealed class DynamicZoneDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string AnchorNodeId { get; set; } = string.Empty;

    public string? InsertAfterNodeId { get; set; }

    public string? ConnectToTerminalNodeId { get; set; }

    public bool AllowParallelNodes { get; set; }

    public int MaxGeneratedNodeCount { get; set; } = 12;

    public string GenerationInstruction { get; set; } = string.Empty;
}
