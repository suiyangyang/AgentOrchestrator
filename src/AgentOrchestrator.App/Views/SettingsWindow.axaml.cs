using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using AgentOrchestrator.App.ViewModels;

namespace AgentOrchestrator.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is SettingsViewModel vm)
        {
            vm.HostWindow = this;
        }
    }

    private async void OnBrowseOpenCodeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (StorageProvider == null) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 OpenCode 可执行文件",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("可执行文件")
                {
                    Patterns = new[] { "*.exe" }
                },
                new FilePickerFileType("所有文件")
                {
                    Patterns = new[] { "*" }
                }
            }
        });

        if (files?.Count > 0 && DataContext is SettingsViewModel vm)
        {
            var path = files[0].Path.LocalPath;
            if (!string.IsNullOrEmpty(path))
            {
                vm.OpenCodeCliPath = path;
            }
        }
    }
}
