namespace AgentOrchestrator.App.Services.Settings;

public class AppSettings
{
    public bool OpenCodeEnabled { get; set; } = true;
    public string Host { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 8908;
    public string Username { get; set; } = "opencode";
    public string Password { get; set; } = "";
    /// <summary>Last focused project id; restored on app start so the blank
    /// page opens against the same working directory as the previous session.</summary>
    public string? LastProjectId { get; set; }
}
