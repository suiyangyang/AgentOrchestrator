using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgentOrchestrator.App.Models.Sidebar;

namespace AgentOrchestrator.App.ViewModels;

/// <summary>
/// One node in the sidebar tree. Wraps a <see cref="SessionRecord"/> and
/// exposes UI-friendly state (display title, relative time, selection).
/// </summary>
public sealed class SidebarSessionViewModel : INotifyPropertyChanged
{
    public SidebarSessionViewModel(SessionRecord record)
    {
        _record = record;
        _title = string.IsNullOrWhiteSpace(record.Title) ? "新对话" : record.Title;
    }

    private SessionRecord _record;
    public SessionRecord Record
    {
        get => _record;
        set
        {
            if (_record == value) return;
            _record = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SessionId));
            OnPropertyChanged(nameof(AgentSessionId));
            OnPropertyChanged(nameof(ProjectId));
            OnPropertyChanged(nameof(DisplayRelativeTime));
            OnPropertyChanged(nameof(EffectiveLastActivityAt));
            OnPropertyChanged(nameof(HasUnreadCompletion));
        }
    }

    public string SessionId => Record.SessionId;
    public string AgentSessionId => Record.AgentSessionId;
    public string? ProjectId => Record.ProjectId;

    private string _title = "新对话";
    public string Title
    {
        get => _title;
        set
        {
            if (_title == value) return;
            _title = value;
            OnPropertyChanged();
        }
    }

    public string DisplayRelativeTime => FormatRelative(EffectiveLastActivityAt);

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasUnreadCompletion));
        }
    }

    private bool _isStreaming;
    public bool IsStreaming
    {
        get => _isStreaming;
        set
        {
            if (_isStreaming == value) return;
            _isStreaming = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasUnreadCompletion));
        }
    }

    public long EffectiveLastActivityAt => Record.LastActivityAt > 0 ? Record.LastActivityAt : Record.CreatedAt;

    public bool HasUnreadCompletion
        => !IsSelected
           && !IsStreaming
           && (!Record.ViewedAt.HasValue || Record.ViewedAt.Value < EffectiveLastActivityAt);

    public void UpdateRecord(SessionRecord record)
    {
        // Refresh the underlying record (title sync, etc.) without changing identity.
        Record = record;
        Title = string.IsNullOrWhiteSpace(record.Title) ? "新对话" : record.Title;
        OnPropertyChanged(nameof(DisplayRelativeTime));
        OnPropertyChanged(nameof(EffectiveLastActivityAt));
        OnPropertyChanged(nameof(HasUnreadCompletion));
    }

    public static string FormatRelative(long unixMs)
    {
        var dt = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
        var diff = DateTimeOffset.UtcNow - dt;
        if (diff.TotalSeconds < 60) return "刚刚";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} 分钟";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} 小时";
        if (diff.TotalDays < 30) return $"{(int)diff.TotalDays} 天";
        if (diff.TotalDays < 365) return $"{(int)(diff.TotalDays / 30)} 个月";
        return $"{(int)(diff.TotalDays / 365)} 年";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
