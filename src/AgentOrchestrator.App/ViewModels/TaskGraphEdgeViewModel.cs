using Avalonia;

namespace AgentOrchestrator.App.ViewModels;

public sealed class TaskGraphEdgeViewModel
{
    public TaskGraphEdgeViewModel(string sourceId, string targetId, double x1, double y1, double x2, double y2)
    {
        SourceId = sourceId;
        TargetId = targetId;
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
    }

    public string SourceId { get; }

    public string TargetId { get; }

    public double X1 { get; }

    public double Y1 { get; }

    public double X2 { get; }

    public double Y2 { get; }

    public Point StartPoint => new(X1, Y1);

    public Point EndPoint => new(X2, Y2);

    public double MidX => (X1 + X2) / 2;

    public double MidY => (Y1 + Y2) / 2;
}
