using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Immutable;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Rendering.SceneGraph;

namespace AgentOrchestrator.App.Controls;

public partial class SpinnerControl : UserControl
{
    private readonly DispatcherTimer _timer;
    private double _angle;

    private static readonly Color TrackColor = Color.Parse("#C9CDD3");
    private static readonly Color ActiveColor = Color.Parse("#6F7680");
    private const double StrokeThickness = 1.5;
    private const double GapAngleDegrees = 45;
    private const double StartAngleDegrees = -90;

    public SpinnerControl()
    {
        InitializeComponent();

        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(16),
            DispatcherPriority.Render,
            OnTick);

        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _angle = (_angle + 6) % 360;
        InvalidateVisual();
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (IsVisible)
        {
            _timer.Start();
        }
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty)
        {
            if (IsVisible)
            {
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var size = Math.Min(width, height);
        var radius = Math.Max(0, (size - StrokeThickness) / 2d);
        var center = new Point(width / 2d, height / 2d);

        var trackPen = new Pen(new SolidColorBrush(TrackColor), StrokeThickness, lineCap: PenLineCap.Round);
        var activePen = new Pen(new SolidColorBrush(ActiveColor), StrokeThickness, lineCap: PenLineCap.Round);

        DrawArc(context, center, radius, 0, 360, trackPen);
        DrawArc(context, center, radius, StartAngleDegrees + _angle, 360 - GapAngleDegrees, activePen);
    }

    private static void DrawArc(DrawingContext context, Point center, double radius, double startAngleDegrees, double sweepAngleDegrees, Pen pen)
    {
        if (radius <= 0 || sweepAngleDegrees <= 0)
        {
            return;
        }

        if (sweepAngleDegrees >= 359.999)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var start = PointOnCircle(center, radius, startAngleDegrees);
        var end = PointOnCircle(center, radius, startAngleDegrees + sweepAngleDegrees);
        var isLargeArc = sweepAngleDegrees > 180;

        var geometry = new StreamGeometry();
        using (var geo = geometry.Open())
        {
            geo.BeginFigure(start, isFilled: false);
            geo.ArcTo(end, new Size(radius, radius), rotationAngle: 0, isLargeArc, SweepDirection.Clockwise);
            geo.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var angleRadians = angleDegrees * Math.PI / 180d;
        return new Point(
            center.X + radius * Math.Cos(angleRadians),
            center.Y + radius * Math.Sin(angleRadians));
    }
}
