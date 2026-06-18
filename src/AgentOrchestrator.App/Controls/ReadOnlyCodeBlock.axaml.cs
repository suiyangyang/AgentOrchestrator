using Avalonia;
using Avalonia.Controls;

namespace AgentOrchestrator.App.Controls;

public partial class ReadOnlyCodeBlock : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<ReadOnlyCodeBlock, string?>(nameof(Text));

    public ReadOnlyCodeBlock()
    {
        InitializeComponent();
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
