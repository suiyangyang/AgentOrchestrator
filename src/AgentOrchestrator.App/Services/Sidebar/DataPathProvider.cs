using System;
using System.IO;

namespace AgentOrchestrator.App.Services.Sidebar;

/// <summary>
/// Resolves the on-disk path for runtime artifacts (SQLite db, log files, etc.).
/// The path is fixed under LocalAppData so the same machine keeps one stable
/// data store regardless of the launch directory.
/// </summary>
public static class DataPathProvider
{
    public static string DataDirectory { get; } = EnsureDataDirectory();

    public static string DatabaseFile => Path.Combine(DataDirectory, "OrchestratorDb.db");

    private static string EnsureDataDirectory()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentOrchestrator",
            "Datas");
        Directory.CreateDirectory(dir);

        var databaseFile = Path.Combine(dir, "OrchestratorDb.db");
        if (!File.Exists(databaseFile))
        {
            var legacyDir = Path.Combine(AppContext.BaseDirectory, "Datas");
            var legacyFile = Path.Combine(legacyDir, "OrchestratorDb.db");
            if (File.Exists(legacyFile))
            {
                File.Copy(legacyFile, databaseFile, overwrite: false);
            }
        }

        return dir;
    }
}
