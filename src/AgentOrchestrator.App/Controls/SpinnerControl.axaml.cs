using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace AgentOrchestrator.App.Controls;

public partial class SpinnerControl : UserControl
{
    private readonly RotateTransform _rotateTransform = new() { Angle = 0 };
    private readonly DispatcherTimer _timer;

    public SpinnerControl()
    {
        InitializeComponent();
        SpinnerPath.RenderTransform = _rotateTransform;
        SpinnerPath.RenderTransformOrigin = RelativePoint.Center;

        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(16),
            DispatcherPriority.Render,
            OnTick);

        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _rotateTransform.Angle = (_rotateTransform.Angle + 6) % 360;
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
}
