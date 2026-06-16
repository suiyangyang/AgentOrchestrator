namespace OpenCode.Client.Models;

public sealed record BadRequestError
{
    public string Name { get; init; } = "BadRequest";
    public required BadRequestErrorData Data { get; init; }
}

public sealed record BadRequestErrorData
{
    public required string Message { get; init; }
    public string? Kind { get; init; }
}

public sealed record NotFoundError
{
    public string Name { get; init; } = "NotFoundError";
    public required NotFoundErrorData Data { get; init; }
}

public sealed record NotFoundErrorData
{
    public required string Message { get; init; }
}
