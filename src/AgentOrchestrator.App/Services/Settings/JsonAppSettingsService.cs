using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace AgentOrchestrator.App.Services.Settings;

public sealed class JsonAppSettingsService : IAppSettingsService
{
    private const string EmbeddedResourceName = "AgentOrchestrator.App.appsettings.json";
    private const string LocalFileName = "appsettings.local.json";
    private const string AppFolderName = "AgentOrchestrator";
    private const string LegacyUiFontFamily = "Source Han Sans";
    private const string DefaultUiFontFamily = "Segoe UI, Microsoft YaHei UI, Microsoft YaHei";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _localFilePath;

    public JsonAppSettingsService()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _localFilePath = Path.Combine(localAppData, AppFolderName, LocalFileName);
    }

    public AppSettings Load()
    {
        // 1. Start with compiled defaults
        var settings = new AppSettings();

        // 2. Overlay embedded appsettings.json
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName);
        if (stream != null)
        {
            var embedded = JsonSerializer.Deserialize<AppSettings>(stream, JsonOptions);
            if (embedded != null) settings = embedded;
        }

        // 3. Overlay local file (if it exists)
        if (File.Exists(_localFilePath))
        {
            try
            {
                var json = File.ReadAllText(_localFilePath);
                var local = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (local != null) settings = local;
            }
            catch
            {
                // Corrupt local file — ignore and use current settings
            }
        }

        return Normalize(settings);
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_localFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(Normalize(settings), JsonOptions);
        File.WriteAllText(_localFilePath, json);
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        if (string.Equals(settings.UiFontFamily, LegacyUiFontFamily, StringComparison.Ordinal))
        {
            settings.UiFontFamily = DefaultUiFontFamily;
        }

        return settings;
    }
}
