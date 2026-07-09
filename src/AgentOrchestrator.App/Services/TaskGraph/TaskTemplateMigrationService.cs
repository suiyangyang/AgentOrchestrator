using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.TaskGraph;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

/// <summary>
/// Migrates old pre-refactor <c>TaskTemplate</c> JSON files from the legacy
/// <c>Templates/</c> directory into the unified <see cref="ITaskGraphStore"/>
/// as <see cref="TaskGraphDocumentKind.Template"/> documents.
/// Reads old JSON directly via <see cref="JsonDocument"/> — does not depend
/// on any model class from the deleted pre-refactor template subsystem.
/// Successfully migrated source files are moved to <c>Templates/_migrated/</c>.
/// Safe to run multiple times — subsequent runs are no-ops.
/// </summary>
public sealed class TaskTemplateMigrationService
{
    private readonly string _legacyDirectory;
    private readonly ITaskGraphStore _newStore;

    public TaskTemplateMigrationService(ITaskGraphStore newStore, string? legacyDirectory = null)
    {
        _newStore = newStore;
        _legacyDirectory = legacyDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentOrchestrator",
            "Templates");
    }

    /// <summary>
    /// Scans the legacy directory for <c>*.json</c> files, converts each
    /// legacy template record (id / name / description / baseKind / defaultInput /
    /// isBuiltIn / createdAt / updatedAt) to a template <see cref="TaskGraphModel"/>,
    /// saves it to the new store, and moves the source file to
    /// <c>_migrated/</c>.
    /// </summary>
    /// <returns>Number of files successfully migrated.</returns>
    public async Task<int> MigrateAsync(CancellationToken ct = default)
    {
        var legacyDir = _legacyDirectory;
        if (!Directory.Exists(legacyDir))
        {
            return 0;
        }

        var migratedDir = Path.Combine(legacyDir, "_migrated");
        Directory.CreateDirectory(migratedDir);

        // Get the set of existing template ids in the new store so we can skip duplicates.
        var existingTemplates = await _newStore.ListTemplatesAsync(ct).ConfigureAwait(false);
        var existingIds = existingTemplates.Select(x => x.Id).ToHashSet();

        var files = Directory.EnumerateFiles(legacyDir, "*.json", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileName(Path.GetDirectoryName(f))?.Equals("_migrated", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        var migrated = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var json = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Read the fields we need. Old (pre-refactor) template JSON has:
            // id, name, description, baseKind, defaultInput, isBuiltIn, createdAt, updatedAt.
                var id = root.GetProperty("id").GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (existingIds.Contains(id)) continue;

                var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "未命名模板" : "未命名模板";
                var description = root.TryGetProperty("description", out var d) ? d.GetString() ?? string.Empty : string.Empty;
                var defaultInput = root.TryGetProperty("defaultInput", out var di) ? di.GetString() ?? string.Empty : string.Empty;
                var isBuiltIn = root.TryGetProperty("isBuiltIn", out var ib) && ib.GetBoolean();
                // baseKind is a string like "TaskList" / "FeatureDevelopment" / "BugList" / "Custom"
                var baseKindStr = root.TryGetProperty("baseKind", out var bk) ? bk.GetString() ?? "Custom" : "Custom";
                var baseKind = Enum.TryParse<TaskGraphTemplateKind>(baseKindStr, ignoreCase: true, out var parsedKind) ? parsedKind : TaskGraphTemplateKind.Custom;

                var createdAt = root.TryGetProperty("createdAt", out var ca) && ca.TryGetDateTimeOffset(out var cao) ? cao : DateTimeOffset.UtcNow;
                var updatedAt = root.TryGetProperty("updatedAt", out var ua) && ua.TryGetDateTimeOffset(out var uao) ? uao : DateTimeOffset.UtcNow;

                var graph = new TaskGraphModel
                {
                    Id = id,
                    Name = name,
                    DocumentKind = TaskGraphDocumentKind.Template,
                    IsBuiltInTemplate = isBuiltIn,
                    TemplateKind = baseKind,
                    TemplateNotes = description,
                    TemplatePlannerPrompt = defaultInput,
                    CreatedAt = createdAt,
                    UpdatedAt = updatedAt,
                    TemplateMetadata = new TaskGraphTemplateMetadata
                    {
                        AllowDynamicExpansion = true,
                    },
                };

                // Save to new store.
                await _newStore.SaveAsync(graph, ct).ConfigureAwait(false);

                // Move old file to _migrated/.
                var destPath = Path.Combine(migratedDir, Path.GetFileName(file));
                // Overwrite if somehow the destination exists.
                File.Move(file, destPath, overwrite: true);

                migrated++;
            }
            catch
            {
                // Malformed file — skip silently.
            }
        }

        return migrated;
    }
}
