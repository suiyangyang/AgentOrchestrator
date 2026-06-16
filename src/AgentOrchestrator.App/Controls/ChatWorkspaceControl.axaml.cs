using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using AgentOrchestrator.App.Models.Chat;

namespace AgentOrchestrator.App.Controls;

public partial class ChatWorkspaceControl : UserControl
{
    public ChatWorkspaceControl()
    {
        InitializeComponent();
    }

    private void OnPermissionClick(object? sender, RoutedEventArgs e)
    {
        PermissionPopup.IsOpen = !PermissionPopup.IsOpen;
        ModelPopup.IsOpen = false;
    }

    private void OnModelClick(object? sender, RoutedEventArgs e)
    {
        ModelPopup.IsOpen = !ModelPopup.IsOpen;
        PermissionPopup.IsOpen = false;
    }

    private void OnPermissionItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: PermissionOption option } &&
            DataContext is ViewModels.ChatWorkspaceViewModel viewModel)
        {
            viewModel.SelectedPermission = option;
            PermissionPopup.IsOpen = false;
        }
    }

    private void OnModelItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: string model } &&
            DataContext is ViewModels.ChatWorkspaceViewModel viewModel)
        {
            viewModel.SelectedModel = model;
            ModelPopup.IsOpen = false;
        }
    }

    private async void OnDraftKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ViewModels.ChatWorkspaceViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (viewModel.SendCommand.CanExecute(null))
            {
                viewModel.SendCommand.Execute(null);
            }

            e.Handled = true;
        }
    }

    /// <summary>
    /// Intercepts the TextBox's paste operation. If the clipboard holds an image,
    /// the image is saved to a temp file and added as an attachment, and the
    /// default text paste is cancelled by marking the event as handled. Otherwise
    /// the event is left unhandled so the TextBox performs its normal text paste.
    /// </summary>
    private async void OnDraftPasting(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.ChatWorkspaceViewModel viewModel)
        {
            return;
        }

        if (await TryPasteImageAsync(viewModel))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// If the clipboard currently holds an image, save it to a temp file and add it as
    /// an attachment on the view model. Returns true when an image was consumed
    /// (so the caller can mark the key event as handled and suppress default text paste).
    /// </summary>
    private async Task<bool> TryPasteImageAsync(ViewModels.ChatWorkspaceViewModel viewModel)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not IClipboard clipboard)
        {
            return false;
        }

        Bitmap? bitmap;
        try
        {
            using var data = await clipboard.TryGetDataAsync();
            if (data is null)
            {
                return false;
            }

            bitmap = await data.TryGetBitmapAsync();
        }
        catch
        {
            return false;
        }

        if (bitmap is null)
        {
            return false;
        }

        var savedPath = SavePastedBitmap(bitmap);
        if (savedPath is null)
        {
            return false;
        }

        viewModel.AddPastedImage(savedPath);
        return true;
    }

    private static string? SavePastedBitmap(Bitmap bitmap)
    {
        try
        {
            var tempDir = Path.Combine(
                Path.GetTempPath(),
                "AgentOrchestrator",
                "pasted-images");
            Directory.CreateDirectory(tempDir);

            var fileName = $"pasted-{DateTime.Now:yyyyMMdd-HHmmssfff}.png";
            var fullPath = Path.Combine(tempDir, fileName);
            bitmap.Save(fullPath);
            return fullPath;
        }
        catch
        {
            return null;
        }
    }
}