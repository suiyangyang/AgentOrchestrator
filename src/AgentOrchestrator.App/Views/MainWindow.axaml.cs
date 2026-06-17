using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
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
        ToggleMaximize();
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

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        UpdateMaxButtonIcon();
    }

    private void UpdateMaxButtonIcon()
    {
        if (this.FindControl<Button>("MaxButton")?.Content is Path path)
        {
            path.Data = WindowState == WindowState.Maximized
                ? Geometry.Parse("M4,8H8V4H20V16H16V20H4V8M6,10V18H14V18H16V10H6M18,6H10V8H18V6Z")
                : Geometry.Parse("M4,4H20V20H4V4M6,6V18H18V6H6Z");
        }
    }
}
