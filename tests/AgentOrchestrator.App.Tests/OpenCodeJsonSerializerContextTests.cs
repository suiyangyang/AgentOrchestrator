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
}
