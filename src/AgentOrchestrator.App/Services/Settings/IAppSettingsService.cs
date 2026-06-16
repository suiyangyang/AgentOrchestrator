namespace AgentOrchestrator.App.Services.Settings;

public interface IAppSettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);
}
