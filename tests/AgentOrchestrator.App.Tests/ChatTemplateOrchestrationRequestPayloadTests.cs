using AgentOrchestrator.App.Models.Chat;
using Xunit;

namespace AgentOrchestrator.App.Tests;

public sealed class ChatTemplateOrchestrationRequestPayloadTests
{
    // ── Test: Record carries all properties correctly ──
    [Fact]
    public void TemplateOrchestrationRequest_RecordCarriesPromptAndContext()
    {
        var request = new TemplateOrchestrationRequest(
            Prompt: "分析用户需求",
            AgentSessionId: "session-abc",
            WorkingDirectory: "/home/user/project");

        Assert.Equal("分析用户需求", request.Prompt);
        Assert.Equal("session-abc", request.AgentSessionId);
        Assert.Equal("/home/user/project", request.WorkingDirectory);
    }

    // ── Test: Null fields are accepted ──
    [Fact]
    public void TemplateOrchestrationRequest_NullFieldsAreAccepted()
    {
        var request = new TemplateOrchestrationRequest(
            Prompt: "test",
            AgentSessionId: null,
            WorkingDirectory: null);

        Assert.Equal("test", request.Prompt);
        Assert.Null(request.AgentSessionId);
        Assert.Null(request.WorkingDirectory);
    }
}
