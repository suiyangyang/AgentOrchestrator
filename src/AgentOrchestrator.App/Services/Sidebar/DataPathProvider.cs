using System;
using System.IO;

namespace AgentOrchestrator.App.Services.Sidebar;

/// <summary>
/// Resolves the on-disk path for runtime artifacts (SQLite db, log files, etc.).
/// The path is fixed to <c>{AppContext.BaseDirectory}/Datas/</c> so the same
/// machine, the same binary, and the same cwd always share the same data.
/// </summary>
public static class DataPathProvider
{
    public static string DataDirectory { get; } = EnsureDataDirectory();

    public static string DatabaseFile => Path.Combine(DataDirectory, "OrchestratorDb.db");

    private static string EnsureDataDirectory()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Datas");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
