using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

public sealed class JsonTaskTemplateStore : ITaskTemplateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _templatesDirectory;
    private bool _initialized;

    /// <summary>
    /// Stable ids for the three built-in templates so they can be
    /// referenced predictably by other parts of the system.
    /// </summary>
    public static class BuiltInIds
    {
        public const string TaskList = "builtin.task-list";
        public const string FeatureDevelopment = "builtin.feature-dev";
        public const string BugList = "builtin.bug-list";
    }

    public JsonTaskTemplateStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentOrchestrator",
            "Templates"))
    {
    }

    public JsonTaskTemplateStore(string templatesDirectory)
    {
        _templatesDirectory = templatesDirectory;
        Directory.CreateDirectory(_templatesDirectory);
    }

    public string StorageDirectory => _templatesDirectory;

    public async Task<IReadOnlyList<TaskTemplateListItem>> ListAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);

        Directory.CreateDirectory(_templatesDirectory);
        var result = new List<TaskTemplateListItem>();
        foreach (var file in Directory.EnumerateFiles(_templatesDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var json = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                var template = JsonSerializer.Deserialize<TaskTemplate>(json, SerializerOptions);
                if (template is null)
                {
                    continue;
                }

                NormalizeTemplate(template);
                result.Add(new TaskTemplateListItem(
                    template.Id,
                    template.Name,
                    template.UpdatedAt,
                    template.BaseKind,
                    template.IsBuiltIn));
            }
            catch
            {
                // Ignore malformed files so one bad template does not break the workspace.
            }
        }

        return result
            .OrderBy(t => t.IsBuiltIn ? 0 : 1) // built-ins first
            .ThenByDescending(t => t.UpdatedAt)
            .ToList();
    }

    public async Task<TaskTemplate?> LoadAsync(string id, CancellationToken ct = default)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);

        var filePath = GetFilePath(id);
        if (!File.Exists(filePath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        var template = JsonSerializer.Deserialize<TaskTemplate>(json, SerializerOptions);
        if (template is null)
        {
            return null;
        }

        NormalizeTemplate(template);
        return template;
    }

    public async Task SaveAsync(TaskTemplate template, CancellationToken ct = default)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);

        Directory.CreateDirectory(_templatesDirectory);
        NormalizeTemplate(template);
        template.UpdatedAt = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(template, SerializerOptions);
        await File.WriteAllTextAsync(GetFilePath(template.Id), json, ct).ConfigureAwait(false);
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        // Guard: built-in templates cannot be deleted.
        if (id == BuiltInIds.TaskList || id == BuiltInIds.FeatureDevelopment || id == BuiltInIds.BugList)
        {
            throw new InvalidOperationException("内置模板不可删除。请使用「复制」创建自定义副本。");
        }

        var filePath = GetFilePath(id);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        // If the directory is empty, seed the three built-in templates.
        if (!Directory.EnumerateFiles(_templatesDirectory, "*.json", SearchOption.TopDirectoryOnly).Any())
        {
            await SeedBuiltInTemplatesAsync().ConfigureAwait(false);
        }
    }

    private async Task SeedBuiltInTemplatesAsync()
    {
        var now = DateTimeOffset.UtcNow;

        var taskList = new TaskTemplate
        {
            Id = BuiltInIds.TaskList,
            Name = "任务列表",
            Description = "适合 1-20 个长链任务，按顺序逐项执行。输入每一行的任务名称即可生成顺序执行的任务图。",
            BaseKind = TaskGraphTemplateKind.TaskList,
            DefaultInput = "- 需求分析\n- 实现功能\n- 测试验证\n- 代码审查\n- 部署上线",
            IsBuiltIn = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var featureDev = new TaskTemplate
        {
            Id = BuiltInIds.FeatureDevelopment,
            Name = "功能开发",
            Description = "先生成方案，等待确认，再动态注入开发计划并执行。适合复杂功能的分阶段开发。",
            BaseKind = TaskGraphTemplateKind.FeatureDevelopment,
            DefaultInput = "- 分析需求并生成方案\n- 等待用户确认\n- 实现核心逻辑\n- 编写测试\n- 代码审查\n- 集成部署",
            IsBuiltIn = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var bugList = new TaskTemplate
        {
            Id = BuiltInIds.BugList,
            Name = "Bug 列表",
            Description = "逐个分析 bug，自动区分可修复项与待补充项，最后输出报告。",
            BaseKind = TaskGraphTemplateKind.BugList,
            DefaultInput = "- Bug 1: 登录页面在 Safari 下白屏\n- Bug 2: 导出 CSV 时中文字符乱码\n- Bug 3: 并发请求时出现死锁",
            IsBuiltIn = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await SaveAsync(taskList).ConfigureAwait(false);
        await SaveAsync(featureDev).ConfigureAwait(false);
        await SaveAsync(bugList).ConfigureAwait(false);
    }

    private string GetFilePath(string id)
        => Path.Combine(_templatesDirectory, $"{id}.json");

    private static void NormalizeTemplate(TaskTemplate template)
    {
        template.Id = string.IsNullOrWhiteSpace(template.Id) ? Guid.NewGuid().ToString("N") : template.Id;
        template.Name = string.IsNullOrWhiteSpace(template.Name) ? "未命名模板" : template.Name;
        template.Description ??= string.Empty;
        template.DefaultInput ??= string.Empty;
    }
}
