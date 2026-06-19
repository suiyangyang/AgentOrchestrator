using System.Text.Json;
using System.Text.RegularExpressions;
using AgentOrchestrator.App.Models.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

public sealed class JsonPlanningParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public PlannerSchema ParseOrThrow(string rawText)
    {
        if (TryParse(rawText, out var schema))
        {
            return schema;
        }

        throw new PlannerParseException("无法解析 LLM 输出为有效 JSON。", rawText);
    }

    public bool TryParse(string rawText, out PlannerSchema schema)
    {
        if (TryDeserialize(rawText, out schema))
        {
            return true;
        }

        var fenceMatch = Regex.Match(rawText, @"```(?:json)?\s*(\{[\s\S]*?\})\s*```", RegexOptions.IgnoreCase);
        if (fenceMatch.Success && TryDeserialize(fenceMatch.Groups[1].Value, out schema))
        {
            return true;
        }

        var start = rawText.IndexOf('{');
        var end = rawText.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            var slice = rawText[start..(end + 1)];
            if (TryDeserialize(slice, out schema))
            {
                return true;
            }
        }

        schema = new PlannerSchema();
        return false;
    }

    private static bool TryDeserialize(string json, out PlannerSchema schema)
    {
        try
        {
            schema = JsonSerializer.Deserialize<PlannerSchema>(json, Options) ?? new PlannerSchema();
            if (schema.Nodes.Count == 0)
            {
                return false;
            }

            return true;
        }
        catch
        {
            schema = new PlannerSchema();
            return false;
        }
    }
}
