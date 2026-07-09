using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.Agent;
using AgentOrchestrator.App.Services.TaskGraph;
using AgentOrchestrator.App.ViewModels;
using Xunit;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Tests;

public sealed class ChatAutoOrchestrationTemplateTests : IDisposable
{
    private readonly string _tempDir;

    public ChatAutoOrchestrationTemplateTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AgentOrchestratorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    /// <summary>
    /// Stub controller: non-null (so the VM passes the null-guard), but throws
    /// on <see cref="ITaskGraphExecutionController.StartAsync"/> so the test
    /// can verify the runtime graph was persisted BEFORE execution starts.
    /// </summary>
    private sealed class ThrowingStartController : ITaskGraphExecutionController
    {
        public Task StartAsync(TaskGraphModel graph, TaskGraphExecutionRequest request, CancellationToken ct = default)
            => throw new InvalidOperationException("No executor wired (test stub).");

        public Task ResumeAsync(string graphId, GraphContinueDecision decision, CancellationToken ct = default)
            => throw new System.NotImplementedException();

        public Task PauseAsync(string graphId, CancellationToken ct = default)
            => throw new System.NotImplementedException();

        public Task CancelAsync(string graphId, CancellationToken ct = default)
            => throw new System.NotImplementedException();
    }

    private static ChatWorkspaceViewModel CreateChatVm(ITaskGraphStore store)
    {
        var agent = new NotImplementedAgentGateway();
        return new ChatWorkspaceViewModel(
            agent,
            null!,
            new SidebarViewModel(null!, store),
            store,
            graphController: new ThrowingStartController(),
            runtimeHub: null);
    }

    // ── Test: TriggerAutoTaskGraph loads and instantiates auto-orch template ──
    [Fact]
    public async Task ChatWorkspaceViewModel_TriggerAutoTaskGraph_LoadsBuiltinAutoOrchestrationTemplate()
    {
        var store = new JsonTaskGraphStore(_tempDir);
        var seeder = new BuiltInTemplateSeeder(store);
        await seeder.SeedIfMissingAsync();

        var vm = CreateChatVm(store);

        // TriggerAutoTaskGraphAsync should throw because _graphController is null,
        // but it first loads+instantiates the template via the store, saving the
        // runtime graph. Catch the expected exception.
        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await vm.TriggerAutoTaskGraphAsync("task1\ntask2\ntask3", CancellationToken.None));

        // Verify the runtime graph was persisted.
        var runtimes = await store.ListRuntimeGraphsAsync();
        Assert.NotEmpty(runtimes);

        var runtimeId = runtimes[0].Id;
        var runtime = await store.LoadAsync(runtimeId);
        Assert.NotNull(runtime);
        Assert.Equal(TaskGraphDocumentKind.Runtime, runtime.DocumentKind);
        Assert.NotEmpty(runtime.Nodes);
    }

    // ── Test: TriggerTemplateTaskGraph instantiates from a template id ──
    [Fact]
    public async Task ChatWorkspaceViewModel_TriggerTemplateTaskGraph_InstantiatesFromTemplateId()
    {
        var store = new JsonTaskGraphStore(_tempDir);

        // Pre-seed a user template (NOT a built-in).
        var template = new Models.TaskGraph.TaskGraph
        {
            Id = "test-template",
            Name = "Test Template",
            DocumentKind = TaskGraphDocumentKind.Template,
            TemplateMetadata = new TaskGraphTemplateMetadata
            {
                AllowDynamicExpansion = true,
                FixedNodeIds = ["tn1"],
                DynamicZones =
                [
                    new DynamicZoneDefinition
                    {
                        Name = "测试动态区",
                        AnchorNodeId = "tn1",
                        InsertAfterNodeId = "tn1",
                        MaxGeneratedNodeCount = 5,
                        GenerationInstruction = "Generate per-line Execute nodes.",
                    },
                ],
            },
        };
        template.Nodes.Add(new TaskNode
        {
            Id = "tn1",
            Title = "入口",
            Kind = TaskNodeKind.Plan,
            Prompt = "{{user_input}}",
            IsTemplateLocked = true,
        });
        await store.SaveAsync(template);

        var vm = CreateChatVm(store);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await vm.TriggerTemplateTaskGraphAsync("test-template", "line1\nline2", CancellationToken.None));

        var runtimes = await store.ListRuntimeGraphsAsync();
        Assert.NotEmpty(runtimes);

        var runtime = await store.LoadAsync(runtimes[0].Id);
        Assert.NotNull(runtime);
        Assert.Equal(TaskGraphDocumentKind.Runtime, runtime.DocumentKind);
    }
}
