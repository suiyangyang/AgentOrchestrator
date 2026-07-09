using System;
using System.Threading.Tasks;
using AgentOrchestrator.App.Services.Settings;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCode.Client;

namespace AgentOrchestrator.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IAppSettingsService _settingsService;
    private readonly OpenCodeClient _openCodeClient;

    [ObservableProperty]
    private SettingsPage _selectedPage = SettingsPage.General;

    public bool IsGeneralSelected => SelectedPage == SettingsPage.General;
    public bool IsAppearanceSelected => SelectedPage == SettingsPage.Appearance;
    public bool IsServicesSelected => SelectedPage == SettingsPage.Services;

    [ObservableProperty]
    private bool _openCodeEnabled = true;

    [ObservableProperty]
    private string _host = "0.0.0.0";

    [ObservableProperty]
    private int _port = 8908;

    [ObservableProperty]
    private string _username = "opencode";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _uiFontFamily = "Segoe UI";

    [ObservableProperty]
    private string _codeFontFamily = "Consolas";

    [ObservableProperty]
    private double _uiFontSize = 14;

    [ObservableProperty]
    private double _codeFontSize = 12;

    [ObservableProperty]
    private bool _isPasswordVisible = false;

    [ObservableProperty]
    private bool _isOpenCodeConnected;

    [ObservableProperty]
    private string _openCodeStatusText = "断开";

    [ObservableProperty]
    private string _openCodeUrl = "http://0.0.0.0:8908";

    public Window? HostWindow { get; set; }

    public SettingsViewModel() : this(
        new JsonAppSettingsService(),
        new OpenCodeClient(new OpenCodeClientOptions
        {
            BaseUrl = new Uri("http://0.0.0.0:8908"),
        }))
    {
    }

    public SettingsViewModel(IAppSettingsService settingsService, OpenCodeClient openCodeClient)
    {
        _settingsService = settingsService;
        _openCodeClient = openCodeClient;
        LoadSettings();
    }

    private void LoadSettings()
    {
        var s = _settingsService.Load();
        OpenCodeEnabled = s.OpenCodeEnabled;
        Host = s.Host;
        Port = s.Port;
        Username = s.Username;
        Password = s.Password;
        UiFontFamily = s.UiFontFamily;
        CodeFontFamily = s.CodeFontFamily;
        UiFontSize = s.UiFontSize;
        CodeFontSize = s.CodeFontSize;
        OpenCodeUrl = BuildOpenCodeUrl();
        UpdateOpenCodeStatus(false);
    }

    [RelayCommand]
    private void Ok()
    {
        var s = new AppSettings
        {
            OpenCodeEnabled = OpenCodeEnabled,
            Host = Host,
            Port = Port,
            Username = Username,
            Password = Password,
            UiFontFamily = UiFontFamily,
            CodeFontFamily = CodeFontFamily,
            UiFontSize = UiFontSize,
            CodeFontSize = CodeFontSize
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
    private async Task RefreshOpenCodeStatusAsync()
    {
        if (!OpenCodeEnabled)
        {
            UpdateOpenCodeStatus(false);
            return;
        }

        try
        {
            var health = await _openCodeClient.HealthAsync();
            UpdateOpenCodeStatus(health.Healthy);
        }
        catch
        {
            UpdateOpenCodeStatus(false);
        }
    }

    [RelayCommand]
    private void SelectPage(SettingsPage page)
    {
        SelectedPage = page;
    }

    public void OpenServicesPage()
    {
        SelectedPage = SettingsPage.Services;
    }

    partial void OnSelectedPageChanged(SettingsPage value)
    {
        OnPropertyChanged(nameof(IsGeneralSelected));
        OnPropertyChanged(nameof(IsAppearanceSelected));
        OnPropertyChanged(nameof(IsServicesSelected));
    }

    public Task InitializeAsync() => RefreshOpenCodeStatusAsync();

    partial void OnOpenCodeEnabledChanged(bool value)
    {
        if (!value)
        {
            UpdateOpenCodeStatus(false);
            return;
        }

        _ = RefreshOpenCodeStatusAsync();
    }

    partial void OnHostChanged(string value)
    {
        OpenCodeUrl = BuildOpenCodeUrl();
    }

    partial void OnPortChanged(int value)
    {
        OpenCodeUrl = BuildOpenCodeUrl();
    }

    partial void OnUsernameChanged(string value)
    {
        if (OpenCodeEnabled)
        {
            _ = RefreshOpenCodeStatusAsync();
        }
    }

    partial void OnPasswordChanged(string value)
    {
        if (OpenCodeEnabled)
        {
            _ = RefreshOpenCodeStatusAsync();
        }
    }

    private string BuildOpenCodeUrl() => $"http://{Host}:{Port}";

    private void UpdateOpenCodeStatus(bool connected)
    {
        IsOpenCodeConnected = connected;
        OpenCodeStatusText = connected ? "已连接" : "断开";
    }
}

public enum SettingsPage
{
    General,
    Appearance,
    Services,
}
