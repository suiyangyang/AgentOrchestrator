using System.Text.Json;

namespace OpenCode.Client.Models;

public sealed record QuestionRequest
{
    public required string Id { get; init; }
    public required string SessionID { get; init; }
    public string? MessageID { get; init; }
    public required string Title { get; init; }
    public required IReadOnlyList<QuestionItem> Questions { get; init; }
    public required QuestionTime Time { get; init; }
}

public sealed record QuestionItem
{
    public required string Header { get; init; }
    public required string Id { get; init; }
    public required string Question { get; init; }
    public required IReadOnlyList<QuestionOption> Options { get; init; }
    public bool Multiple { get; init; }
    public bool Custom { get; init; }
}

public sealed record QuestionOption
{
    public required string Label { get; init; }
    public string? Description { get; init; }
    public JsonElement? Value { get; init; }
}

public sealed record QuestionReplyRequest
{
    public required IReadOnlyList<IReadOnlyList<string>> Answers { get; init; }
}

public sealed record QuestionTime
{
    public long Created { get; init; }
}
