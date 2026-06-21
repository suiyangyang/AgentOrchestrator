using System.Collections.Generic;

namespace AgentOrchestrator.App.Models.TaskGraph;

public static class TaskNodePortStyle
{
    public sealed record PortColors(string FillHex, string SoftBgHex, string TextHex, string Label);

    private static readonly IReadOnlyDictionary<TaskNodeKind, PortColors> Map =
        new Dictionary<TaskNodeKind, PortColors>
        {
            // Each kind gets a distinct hue so the connection lines + ports
            // are visually distinguishable on the canvas. SoftBg is used
            // for tiny badges/labels (15% opacity equivalent), Text for
            // the kind label, Fill for the port circle and edge stroke.
            [TaskNodeKind.Plan]       = new("#2459B8", "#1A2459B8", "#2459B8", "方案"),
            [TaskNodeKind.Execute]    = new("#FF6A00", "#1AFF6A00", "#FF6A00", "执行"),
            [TaskNodeKind.Verify]     = new("#18A558", "#1A18A558", "#0F8A45", "验证"),
            [TaskNodeKind.Decision]   = new("#8B5CF6", "#1A8B5CF6", "#7C3AED", "决策"),
            [TaskNodeKind.Parallel]   = new("#06B6D4", "#1A06B6D4", "#0891B2", "并行"),
            [TaskNodeKind.HumanInput] = new("#EC4899", "#1AEC4899", "#DB2777", "人工"),
        };

    public static PortColors For(TaskNodeKind kind)
        => Map.TryGetValue(kind, out var v) ? v : Map[TaskNodeKind.Execute];

    /// <summary>
    /// Y offset (from the node's top) where the input (left) and output
    /// (right) ports are anchored. Tuned so the port sits in the row that
    /// holds the kind label — visually consistent across all nodes.
    /// </summary>
    public const double PortAnchorOffsetY = 58.0;

    /// <summary>
    /// Horizontal inset from the card edge to the port center, so the port
    /// sits entirely on the card surface rather than straddling the border.
    /// Used by both the control layer (<c>UpdatePortVisual</c>) and the VM
    /// (<c>RefreshGraphSurface</c>) to keep line endpoints in lockstep
    /// with the port visuals.
    /// </summary>
    public const double PortAnchorOffsetX = 8.0;
}
