using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AgentOrchestrator.App.Models.Chat;

namespace AgentOrchestrator.App.Controls;

public partial class ChatWorkspaceControl : UserControl
{
    private const double AutoScrollThreshold = 48d;
    private const double LoadOlderThreshold = 80d;

    private ViewModels.ChatWorkspaceViewModel? _attachedVm;
    private INotifyCollectionChanged? _subscribedMessages;
    private ViewModels.ChatMessageViewModel? _subscribedTail;
    private readonly NotifyCollectionChangedEventHandler _blocksHandler;
    private bool _suppressScrollChanged;
    private bool _pendingOlderHistoryCompensation;
    private double _olderHistoryOffsetBeforeLoad;
    private double _olderHistoryExtentBeforeLoad;

    public ChatWorkspaceControl()
    {
        InitializeComponent();
        _blocksHandler = OnBlocksCollectionChanged;
        MessagesScrollViewer.ScrollChanged += OnMessagesScrollChanged;
    }

    // ── Enter / Shift+Enter handling ────────────────────────────────────
    //
    // TextBox.AcceptsReturn must stay False here because the TextBox does
    // not expose a PreviewKeyDown tunnel event in Avalonia 12 — its
    // internal Enter handling runs before any bubbling KeyDown we could
    // intercept. So Enter is fully owned by this handler: plain Enter
    // triggers SendCommand; Shift+Enter inserts a "\n" at the caret.

    private void OnDraftKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ViewModels.ChatWorkspaceViewModel viewModel) return;

        if (e.Key != Key.Enter) return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            // Shift+Enter: insert newline at caret, replacing any selection.
            if (sender is TextBox tb)
            {
                var text = tb.Text ?? string.Empty;
                var start = tb.SelectionStart;
                var end = tb.SelectionEnd;
                if (start > end) (start, end) = (end, start);
                tb.Text = string.Concat(text.AsSpan(0, start), "\n", text.AsSpan(end));
                tb.CaretIndex = start + 1;
            }
        }
        else
        {
            // Enter (no Shift): trigger the same primary action as the button.
            if (viewModel.PrimaryActionCommand.CanExecute(null))
                viewModel.PrimaryActionCommand.Execute(null);
        }

        e.Handled = true;
    }

    // ── Auto-scroll subscription management ─────────────────────────────

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        UnsubscribeFromVm();

        if (DataContext is ViewModels.ChatWorkspaceViewModel vm)
        {
            _attachedVm = vm;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            AttachMessagesCollection(vm.Messages);
        }
    }

    private void UnsubscribeFromVm()
    {
        if (_attachedVm is null) return;

        _attachedVm.PropertyChanged -= OnViewModelPropertyChanged;
        DetachMessagesCollection();
        DetachTailBlocksSubscription();
        _attachedVm = null;
    }

    private void AttachMessagesCollection(INotifyCollectionChanged messagesCollection)
    {
        if (ReferenceEquals(_subscribedMessages, messagesCollection))
        {
            return;
        }

        DetachMessagesCollection();
        _subscribedMessages = messagesCollection;
        _subscribedMessages.CollectionChanged += OnMessagesCollectionChanged;
        SubscribeToLastMessageBlocks();
        MaybeScrollToEnd();
    }

    private void DetachMessagesCollection()
    {
        if (_subscribedMessages is null)
        {
            return;
        }

        _subscribedMessages.CollectionChanged -= OnMessagesCollectionChanged;
        _subscribedMessages = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_attachedVm is null)
        {
            return;
        }

        if (string.Equals(e.PropertyName, nameof(ViewModels.ChatWorkspaceViewModel.Messages), StringComparison.Ordinal))
        {
            AttachMessagesCollection(_attachedVm.Messages);
        }
    }

    private void SubscribeToLastMessageBlocks()
    {
        DetachTailBlocksSubscription();
        var last = _attachedVm?.Messages.LastOrDefault();
        if (last is null) return;
        last.Blocks.CollectionChanged += _blocksHandler;
        _subscribedTail = last;
    }

    private void DetachTailBlocksSubscription()
    {
        if (_subscribedTail is null) return;
        _subscribedTail.Blocks.CollectionChanged -= _blocksHandler;
        _subscribedTail = null;
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        MaybeScrollToEnd();

        // The tail may have shifted (or been removed/reset); re-attach.
        SubscribeToLastMessageBlocks();
    }

    private void OnBlocksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        MaybeScrollToEnd();
    }

    private void MaybeScrollToEnd()
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                if (ShouldAutoScroll())
                {
                    MessagesScrollViewer.ScrollToEnd();
                }
            },
            DispatcherPriority.Background);
    }

    private void OnMessagesScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_suppressScrollChanged || _attachedVm is null)
        {
            return;
        }

        if (_pendingOlderHistoryCompensation)
        {
            _pendingOlderHistoryCompensation = false;
            var delta = Math.Max(MessagesScrollViewer.Extent.Height - _olderHistoryExtentBeforeLoad, 0d);
            var targetY = _olderHistoryOffsetBeforeLoad + delta;
            _suppressScrollChanged = true;
            MessagesScrollViewer.Offset = new Vector(MessagesScrollViewer.Offset.X, targetY);
            _suppressScrollChanged = false;
            return;
        }

        if (MessagesScrollViewer.Offset.Y <= LoadOlderThreshold)
        {
            _ = TriggerLoadOlderHistoryAsync(_attachedVm);
        }
    }

    private async Task TriggerLoadOlderHistoryAsync(ViewModels.ChatWorkspaceViewModel vm)
    {
        if (_pendingOlderHistoryCompensation || vm.IsLoadingOlderHistory || !vm.HasOlderHistory)
        {
            return;
        }

        _olderHistoryOffsetBeforeLoad = MessagesScrollViewer.Offset.Y;
        _olderHistoryExtentBeforeLoad = MessagesScrollViewer.Extent.Height;

        var added = await vm.LoadOlderHistoryAsync().ConfigureAwait(true);
        if (!added)
        {
            return;
        }

        _pendingOlderHistoryCompensation = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!_pendingOlderHistoryCompensation)
                {
                    return;
                }

                var delta = Math.Max(MessagesScrollViewer.Extent.Height - _olderHistoryExtentBeforeLoad, 0d);
                var targetY = _olderHistoryOffsetBeforeLoad + delta;
                _suppressScrollChanged = true;
                MessagesScrollViewer.Offset = new Vector(MessagesScrollViewer.Offset.X, targetY);
                _suppressScrollChanged = false;
                _pendingOlderHistoryCompensation = false;
            },
            DispatcherPriority.Background);
    }

    private bool ShouldAutoScroll()
    {
        if (MessagesScrollViewer.Extent.Height <= MessagesScrollViewer.Viewport.Height)
        {
            return true;
        }

        return MessagesScrollViewer.Extent.Height - (MessagesScrollViewer.Offset.Y + MessagesScrollViewer.Viewport.Height) <= AutoScrollThreshold;
    }

    // ── Popup handlers ──────────────────────────────────────────────────

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

    // ── Paste-image handling ────────────────────────────────────────────

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
