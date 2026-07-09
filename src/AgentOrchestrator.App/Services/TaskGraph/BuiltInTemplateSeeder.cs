using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.TaskGraph;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

/// <summary>
/// Seeds the 4 built-in template <see cref="TaskGraph"/> documents into the
/// <see cref="ITaskGraphStore"/> on first run, if they do not already exist.
/// Idempotent — running twice is a no-op.
///
/// The 4th template (<c>builtin.auto-orchestration</c>) is a hidden system
/// template used by the Chat workspace's "自动编排" mode and is NOT shown in
/// the user-facing template list.
/// </summary>
public sealed class BuiltInTemplateSeeder
{
    private readonly ITaskGraphStore _store;

    // Source of truth for built-in template ids. ChatWorkspaceViewModel.AutoOrchestrationTemplateId
    // must match BuiltInIds.AutoOrchestration.
    public static class BuiltInIds
    {
        public const string TaskList = "builtin.task-list";
        public const string FeatureDevelopment = "builtin.feature-dev";
        public const string BugList = "builtin.bug-list";

        /// <summary>
        /// Hidden system template for Chat "自动编排" mode. Must match
        /// <c>ChatWorkspaceViewModel.AutoOrchestrationTemplateId</c>.
        /// </summary>
        public const string AutoOrchestration = "builtin.auto-orchestration";
    }

    public BuiltInTemplateSeeder(ITaskGraphStore store)
    {
        _store = store;
    }

    public async Task SeedIfMissingAsync(CancellationToken ct = default)
    {
        var existing = await _store.ListTemplatesAsync(ct).ConfigureAwait(false);
        var existingIds = existing.Select(x => x.Id).ToHashSet();

        await SeedIfMissingAsync(
            BuiltInIds.TaskList,
            "任务列表",
            "- 需求分析\n- 实现功能\n- 测试验证\n- 代码审查\n- 部署上线",
            TaskGraphTemplateKind.TaskList,
            existingIds,
            ct).ConfigureAwait(false);

        await SeedIfMissingAsync(
            BuiltInIds.FeatureDevelopment,
            "功能开发",
            "- 分析需求并生成方案\n- 等待用户确认\n- 实现核心逻辑\n- 编写测试\n- 代码审查\n- 集成部署",
            TaskGraphTemplateKind.FeatureDevelopment,
            existingIds,
            ct).ConfigureAwait(false);

        await SeedIfMissingAsync(
            BuiltInIds.BugList,
            "Bug 列表",
            "- Bug 1: 登录页面在 Safari 下白屏\n- Bug 2: 导出 CSV 时中文字符乱码\n- Bug 3: 并发请求时出现死锁",
            TaskGraphTemplateKind.BugList,
            existingIds,
            ct).ConfigureAwait(false);

        await SeedAutoOrchestrationIfMissingAsync(existingIds, ct).ConfigureAwait(false);
    }

    private async Task SeedIfMissingAsync(
        string id,
        string name,
        string defaultInput,
        TaskGraphTemplateKind kind,
        System.Collections.Generic.HashSet<string> existingIds,
        CancellationToken ct)
    {
        if (existingIds.Contains(id))
        {
            return;
        }

        var graph = TaskGraphTemplateBuilder.Build(kind, defaultInput, TaskGraphDocumentKind.Template);
        graph.Id = id;
        graph.Name = name;
        graph.IsBuiltInTemplate = true;
        graph.CreatedAt = System.DateTimeOffset.UtcNow;
        graph.UpdatedAt = graph.CreatedAt;

        await _store.SaveAsync(graph, ct).ConfigureAwait(false);
    }

    private async Task SeedAutoOrchestrationIfMissingAsync(
        System.Collections.Generic.HashSet<string> existingIds,
        CancellationToken ct)
    {
        if (existingIds.Contains(BuiltInIds.AutoOrchestration))
        {
            return;
        }

        var graph = BuildAutoOrchestrationTemplate();
        graph.CreatedAt = System.DateTimeOffset.UtcNow;
        graph.UpdatedAt = graph.CreatedAt;

        await _store.SaveAsync(graph, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the hidden <c>builtin.auto-orchestration</c> system template.
    /// Has 1 fixed "input" node + 1 dynamic zone that generates per-line
    /// Execute nodes from the user input. When the user types "task1\ntask2\ntask3"
    /// and triggers auto-orchestration, the instantiated runtime graph has:
    ///   input → dyn_1 → dyn_2 → dyn_3
    /// </summary>
    private static TaskGraphModel BuildAutoOrchestrationTemplate()
    {
        var inputNode = new TaskNode
        {
            Id = "auto_input",
            Title = "解析用户请求",
            Description = "捕获并整理用户在 Chat 中提交的原始输入，作为后续任务的统一上下文。",
            Kind = TaskNodeKind.Plan,
            Prompt = "你正在解析 Chat 中用户提交的请求。请将以下输入整理为精炼的执行背景，不要扩写结论。\n\n{{user_input}}",
            Status = TaskNodeStatus.Pending,
            IsTemplateLocked = true,
        };

        var graph = new TaskGraphModel
        {
            Id = BuiltInIds.AutoOrchestration,
            Name = "自动编排",
            DocumentKind = TaskGraphDocumentKind.Template,
            IsBuiltInTemplate = true,
            TemplateNotes = "隐藏系统模板，供 Chat 的「自动编排」使用。",
            TemplatePlannerPrompt = "Plan the execution steps in order.",
            TemplateKind = TaskGraphTemplateKind.Custom,
            TemplateMetadata = new TaskGraphTemplateMetadata
            {
                AllowDynamicExpansion = true,
                FixedNodeIds = ["auto_input"],
                DynamicZones =
                [
                    new DynamicZoneDefinition
                    {
                        Name = "任务执行区",
                        AnchorNodeId = "auto_input",
                        InsertAfterNodeId = "auto_input",
                        MaxGeneratedNodeCount = 12,
                        GenerationInstruction = "为每一行用户输入生成一个串行 Execute 节点，自动连接到上一节点。",
                    },
                ],
            },
        };
        graph.Nodes.Add(inputNode);
        return graph;
    }
}
