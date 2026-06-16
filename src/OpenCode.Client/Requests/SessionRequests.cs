namespace OpenCode.Client.Requests;

public sealed record SessionCreateRequest
{
    public string? ParentID { get; init; }
    public string? Title { get; init; }
}

public sealed record SessionUpdateRequest
{
    public string? Title { get; init; }
}

public sealed record SessionPromptRequest
{
    public string? MessageID { get; init; }
    public ModelRefRequest? Model { get; init; }
    public string? Agent { get; init; }
    public bool? NoReply { get; init; }
    public string? System { get; init; }
    public IReadOnlyDictionary<string, bool>? Tools { get; init; }
    public required IReadOnlyList<PartInputRequest> Parts { get; init; }
}

public sealed record SessionCommandRequest
{
    public string? MessageID { get; init; }
    public string? Agent { get; init; }
    public string? Model { get; init; }
    public required string Arguments { get; init; }
    public required string Command { get; init; }
}

public sealed record SessionShellRequest
{
    public required string Agent { get; init; }
    public ModelRefRequest? Model { get; init; }
    public required string Command { get; init; }
}

public sealed record SessionSummarizeRequest
{
    public required string ProviderID { get; init; }
    public required string ModelID { get; init; }
}

public sealed record SessionRevertRequest
{
    public required string MessageID { get; init; }
    public string? PartID { get; init; }
}

public sealed record SessionInitRequest
{
    public required string MessageID { get; init; }
    public required string ProviderID { get; init; }
    public required string ModelID { get; init; }
}

public sealed record SessionForkRequest
{
    public string? MessageID { get; init; }
}

public sealed record PostSessionIdPermissionsPermissionIdRequest
{
    public required string Response { get; init; }
}

public sealed record ModelRefRequest
{
    public required string ProviderID { get; init; }
    public required string ModelID { get; init; }
}

public sealed record PartInputRequest
{
    public string? Id { get; init; }
    public required string Type { get; init; }
    public string? Text { get; init; }
    public bool? Synthetic { get; init; }
    public bool? Ignored { get; init; }
    public string? Mime { get; init; }
    public string? Filename { get; init; }
    public string? Url { get; init; }
    public string? Name { get; init; }
    public string? Prompt { get; init; }
    public string? Description { get; init; }
    public string? Agent { get; init; }
    public FilePartSourceRequest? Source { get; init; }
    public PartInputTimeRequest? Time { get; init; }
    public System.Text.Json.JsonElement? Metadata { get; init; }
}

public sealed record FilePartSourceRequest
{
    public required string Type { get; init; }
    public FilePartSourceTextRequest? Text { get; init; }
    public string? Path { get; init; }
    public string? Name { get; init; }
    public long? Kind { get; init; }
    public RangeRequest? Range { get; init; }
}

public sealed record FilePartSourceTextRequest
{
    public required string Value { get; init; }
    public long Start { get; init; }
    public long End { get; init; }
}

public sealed record RangeRequest
{
    public PositionRequest? Start { get; init; }
    public PositionRequest? End { get; init; }
}

public sealed record PositionRequest
{
    public long Line { get; init; }
    public long Character { get; init; }
}

public sealed record PartInputTimeRequest
{
    public long Start { get; init; }
    public long? End { get; init; }
}
