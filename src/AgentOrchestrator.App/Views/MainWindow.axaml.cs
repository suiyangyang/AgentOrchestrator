using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AgentOrchestrator.App.ViewModels;

namespace AgentOrchestrator.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Storage = StorageProvider;
        }
    }

    // --- Title bar drag-to-move ---
    // Pattern matches IChromAgent.Desktop reference:
    // BeginMoveDrag only activates when the pointer MOVES while pressed,
    // so buttons inside the title bar remain clickable for normal clicks.
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    // --- Double-click title bar to maximize/restore ---
    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        ToggleMaximize();
    }

    private void OnMinButtonClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaxButtonClick(object? sender, RoutedEventArgs e)
    {
        var isMaximized = WindowState == WindowState.Maximized;
        WindowState = isMaximized ? WindowState.Normal : WindowState.Maximized;
        // Segoe MDL2 Assets: \uE922 = maximize, \uE923 = restore
        MaxButton.Content = isMaximized ? "\uE922" : "\uE923";
    }

    private void OnCloseButtonClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnSettingsButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.Settings is { } settingsVm)
        {
            var settingsWindow = new SettingsWindow
            {
                DataContext = settingsVm
            };
            await settingsWindow.ShowDialog<bool>(this);
        }
    }

    private void OnSidebarToggleButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ToggleLeftSidebarCommand.Execute(null);
        }
    }

    private void OnRightSidebarToggleButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ToggleRightSidebarCommand.Execute(null);
        }
    }

    private void OnSearchOverlayPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && e.Source == sender)
        {
            vm.Sidebar.CloseSearchOverlayCommand.Execute(null);
        }
    }

    private void OnQuestionBannerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && e.Source == sender)
        {
            vm.Chat.ClosePendingQuestionCommand.Execute(null);
        }
    }

    private void OnSearchResultClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SidebarSessionViewModel session }
            && DataContext is MainWindowViewModel vm)
        {
            vm.Sidebar.SelectSessionCommand.Execute(session.SessionId);
        }
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        // Mirror the maximize icon swap so the double-tap path stays in sync.
        var isMaximized = WindowState == WindowState.Maximized;
        MaxButton.Content = isMaximized ? "\uE923" : "\uE922";
    }
}
