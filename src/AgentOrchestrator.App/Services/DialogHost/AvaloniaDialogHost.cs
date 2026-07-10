using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AgentOrchestrator.App.Services.DialogHost;

/// <summary>
/// Tiny helper that shows modal confirm + text-input dialogs over the
/// owner window. Used by MainWindowViewModel for sidebar actions that
/// need a one-shot user prompt (rename / remove confirm).
/// </summary>
public sealed class AvaloniaDialogHost : IDialogHost
{
    public Task<bool> ConfirmAsync(Window? owner, string title, string message)
    {
        var tcs = new TaskCompletionSource<bool>();
        var dialog = new Window
        {
            Title = title,
            Width = 400,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
        };

        var root = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
        };

        var messageBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x28)),
        };
        root.Children.Add(messageBlock);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        var cancel = MakeButton("取消", false);
        var ok = MakeButton("确定", true);
        ok.IsDefault = true;

        cancel.Click += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };
        ok.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };

        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);

        dialog.Content = root;
        ShowDialog(dialog, owner);
        return tcs.Task;
    }

    public Task<string?> InputAsync(Window? owner, string title, string label, string initial)
    {
        var tcs = new TaskCompletionSource<string?>();
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
        };

        var root = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
        };

        root.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0x72, 0x7A)),
        });

        var input = new TextBox
        {
            Text = initial,
            FontSize = 13,
            Padding = new Thickness(10, 8),
            Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF6, 0xF8)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0xE1, 0xE8)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
        };
        root.Children.Add(input);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        var cancel = MakeButton("取消", false);
        var ok = MakeButton("确定", true);
        ok.IsDefault = true;

        cancel.Click += (_, _) => { tcs.TrySetResult(null); dialog.Close(); };
        ok.Click += (_, _) => { tcs.TrySetResult(input.Text); dialog.Close(); };

        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);

        dialog.Content = root;
        dialog.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
        ShowDialog(dialog, owner);
        return tcs.Task;
    }

    public Task<string?> SelectAsync(
        Window? owner,
        string title,
        string label,
        IReadOnlyList<string> options,
        string? selectedOption = null)
    {
        var tcs = new TaskCompletionSource<string?>();
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
        };

        var root = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
        };

        root.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0x72, 0x7A)),
        });

        var combo = new ComboBox
        {
            ItemsSource = options,
            SelectedItem = selectedOption,
            FontSize = 13,
            Padding = new Thickness(10, 8),
            Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF6, 0xF8)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0xE1, 0xE8)),
            BorderThickness = new Thickness(1),
        };
        root.Children.Add(combo);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        var cancel = MakeButton("取消", false);
        var ok = MakeButton("确定", true);
        ok.IsDefault = true;

        cancel.Click += (_, _) => { tcs.TrySetResult(null); dialog.Close(); };
        ok.Click += (_, _) => { tcs.TrySetResult(combo.SelectedItem as string); dialog.Close(); };

        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);

        dialog.Content = root;
        dialog.Opened += (_, _) =>
        {
            if (combo.SelectedItem is null && options.Count > 0)
            {
                combo.SelectedIndex = 0;
            }
            combo.Focus();
        };
        ShowDialog(dialog, owner);
        return tcs.Task;
    }

    private static Button MakeButton(string text, bool primary)
    {
        var btn = new Button
        {
            Content = text,
            MinWidth = 80,
            Height = 32,
            Padding = new Thickness(14, 0),
            CornerRadius = new CornerRadius(6),
            FontSize = 13,
            Background = new SolidColorBrush(primary ? Color.FromRgb(0x1F, 0x23, 0x28) : Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Foreground = new SolidColorBrush(primary ? Colors.White : Color.FromRgb(0x1F, 0x23, 0x28)),
            BorderBrush = new SolidColorBrush(primary ? Color.FromRgb(0x1F, 0x23, 0x28) : Color.FromRgb(0xDC, 0xE1, 0xE8)),
            BorderThickness = new Thickness(1),
        };
        return btn;
    }

    private static void ShowDialog(Window dialog, Window? owner)
    {
        if (owner is not null)
        {
            _ = dialog.ShowDialog<bool>(owner);
        }
        else
        {
            dialog.Show();
        }
    }
}
