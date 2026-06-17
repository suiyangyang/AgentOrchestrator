using System;
using CommunityToolkit.Mvvm.ComponentModel;
using AgentOrchestrator.App.Services.Agent;

namespace AgentOrchestrator.App.ViewModels;

public partial class SubagentActivityViewModel : ObservableObject
{
    private const string UnknownAgentName = "subagent";
    private const string UnknownModelName = "unknown";

    [ObservableProperty]
    private string _sessionId = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _agentName = UnknownAgentName;

    [ObservableProperty]
    private string _modelName = UnknownModelName;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private long _durationMs;

    [ObservableProperty]
    private long _updatedAt;

    public string FooterText => $"{AgentName} · {ModelName} · {FormatDuration(DurationMs)}";

    partial void OnAgentNameChanged(string value) => OnPropertyChanged(nameof(FooterText));
    partial void OnModelNameChanged(string value) => OnPropertyChanged(nameof(FooterText));
    partial void OnDurationMsChanged(long value) => OnPropertyChanged(nameof(FooterText));

    public void UpdateFrom(SubagentActivitySnapshot snapshot)
    {
        SessionId = snapshot.SessionId;
        Title = snapshot.Title;
        StatusText = snapshot.StatusText;
        AgentName = string.IsNullOrWhiteSpace(snapshot.AgentName) ? UnknownAgentName : snapshot.AgentName;
        ModelName = string.IsNullOrWhiteSpace(snapshot.ModelName) ? UnknownModelName : snapshot.ModelName;
        Content = snapshot.Content;
        IsBusy = snapshot.IsBusy;
        DurationMs = snapshot.DurationMs;
        UpdatedAt = snapshot.UpdatedAt;
    }

    private static string FormatDuration(long durationMs)
    {
        var normalizedMs = Math.Max(durationMs, 0);
        var duration = TimeSpan.FromMilliseconds(normalizedMs);

        if (duration.TotalMinutes < 1)
        {
            return $"{duration.TotalSeconds:0.#}s";
        }

        if (duration.TotalHours < 1)
        {
            return $"{(int)duration.TotalMinutes}m{duration.Seconds}s";
        }

        return $"{(int)duration.TotalHours}h{duration.Minutes}m{duration.Seconds}s";
    }
}
