using System.Collections.Generic;
using System.Text.Json;
using OpenCode.Client.Internal.Json;
using OpenCode.Client.Models;
using Xunit;

namespace AgentOrchestrator.App.Tests;

public sealed class OpenCodeJsonSerializerContextTests
{
    [Fact]
    public void Deserialize_QuestionRequestReadOnlyList_UsesGeneratedMetadata()
    {
        const string json =
            """
            [
              {
                "id": "req-1",
                "sessionID": "session-1",
                "messageID": "msg-1",
                "title": "Need input",
                "questions": [
                  {
                    "header": "Scope",
                    "id": "q-1",
                    "question": "Choose one",
                    "options": [
                      {
                        "label": "A",
                        "description": "Option A",
                        "value": "a"
                      }
                    ],
                    "multiple": false,
                    "custom": false
                  }
                ],
                "time": {
                  "created": 1710000000
                }
              }
            ]
            """;

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var context = new OpenCodeJsonSerializerContext(options);

        var result = JsonSerializer.Deserialize<IReadOnlyList<QuestionRequest>>(json, context.Options);

        Assert.NotNull(result);
        var request = Assert.Single(result);
        Assert.Equal("req-1", request.Id);
        Assert.Equal("session-1", request.SessionID);
        Assert.Equal("Need input", request.Title);
        var question = Assert.Single(request.Questions);
        Assert.Equal("q-1", question.Id);
        Assert.Single(question.Options);
    }

    [Fact]
    public void Deserialize_TodoReadOnlyList_UsesGeneratedMetadata()
    {
        const string json =
            """
            [
              {
                "id": "todo-1",
                "content": "实现输入区 Todo 条",
                "status": "in_progress",
                "priority": "high"
              }
            ]
            """;

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var context = new OpenCodeJsonSerializerContext(options);

        var result = JsonSerializer.Deserialize<IReadOnlyList<Todo>>(json, context.Options);

        Assert.NotNull(result);
        var todo = Assert.Single(result);
        Assert.Equal("todo-1", todo.Id);
        Assert.Equal("实现输入区 Todo 条", todo.Content);
        Assert.Equal("in_progress", todo.Status);
        Assert.Equal("high", todo.Priority);
    }

    [Fact]
    public void Deserialize_TodoWithoutId_AllowsOpenCodeRuntimePayload()
    {
        const string json =
            """
            [
              {
                "content": "实现输入区 Todo 条",
                "status": "in_progress",
                "priority": "high"
              }
            ]
            """;

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var context = new OpenCodeJsonSerializerContext(options);

        var result = JsonSerializer.Deserialize<IReadOnlyList<Todo>>(json, context.Options);

        Assert.NotNull(result);
        var todo = Assert.Single(result);
        Assert.Null(todo.Id);
        Assert.Equal("实现输入区 Todo 条", todo.Content);
    }

    [Fact]
    public void Deserialize_CommandReadOnlyList_AllowsObjectTemplatePayload()
    {
        const string json =
            """
            [
              {
                "name": "revert",
                "description": "Revert the session",
                "agent": "codex",
                "model": "gpt-5",
                "template": {
                  "type": "form",
                  "fields": [
                    {
                      "name": "message"
                    }
                  ]
                },
                "subtask": false,
                "builtIn": true
              }
            ]
            """;

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var context = new OpenCodeJsonSerializerContext(options);

        var result = JsonSerializer.Deserialize<IReadOnlyList<Command>>(json, context.Options);

        Assert.NotNull(result);
        var command = Assert.Single(result);
        Assert.Equal("revert", command.Name);
        Assert.True(command.Template.HasValue);
        Assert.Equal(JsonValueKind.Object, command.Template.Value.ValueKind);
        Assert.True(command.BuiltIn);
    }
}
