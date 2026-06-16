namespace AgentOrchestrator.App.Services.Settings;

public class AppSettings
{
    public string OpenCodeCliPath { get; set; } = "";
    public string Host { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 8908;
    public string Username { get; set; } = "opencode";
    public string Password { get; set; } = "";
}
