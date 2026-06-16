using System;
using AgentOrchestrator.App.Services.Settings;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentOrchestrator.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IAppSettingsService _settingsService;

    [ObservableProperty]
    private string _openCodeCliPath = "";

    [ObservableProperty]
    private string _host = "0.0.0.0";

    [ObservableProperty]
    private int _port = 8908;

    [ObservableProperty]
    private string _username = "opencode";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private bool _isPasswordVisible = false;

    public Window? HostWindow { get; set; }

    public SettingsViewModel() : this(new JsonAppSettingsService()) { }

    public SettingsViewModel(IAppSettingsService settingsService)
    {
        _settingsService = settingsService;
        LoadSettings();
    }

    private void LoadSettings()
    {
        var s = _settingsService.Load();
        OpenCodeCliPath = s.OpenCodeCliPath;
        Host = s.Host;
        Port = s.Port;
        Username = s.Username;
        Password = s.Password;
    }

    [RelayCommand]
    private void Ok()
    {
        var s = new AppSettings
        {
            OpenCodeCliPath = OpenCodeCliPath,
            Host = Host,
            Port = Port,
            Username = Username,
            Password = Password
        };
        _settingsService.Save(s);
        HostWindow?.Close(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        HostWindow?.Close(false);
    }

    [RelayCommand]
    private void TogglePasswordVisibility()
    {
        IsPasswordVisible = !IsPasswordVisible;
    }

    [RelayCommand]
    private void BrowseOpenCode()
    {
        // Implemented in code-behind (OnBrowseOpenCodeClick in SettingsWindow.axaml.cs).
        // This command exists to keep the XAML binding surface clean.
    }
}
