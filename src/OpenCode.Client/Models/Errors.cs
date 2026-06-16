namespace OpenCode.Client.Models;

public sealed record ApiError
{
    public string Name { get; init; } = "APIError";
    public required ApiErrorData Data { get; init; }
}

public sealed record ApiErrorData
{
    public required string Message { get; init; }
    public long? StatusCode { get; init; }
    public bool IsRetryable { get; init; }
    public IReadOnlyDictionary<string, string>? ResponseHeaders { get; init; }
    public string? ResponseBody { get; init; }
}

public sealed record ProviderAuthError
{
    public string Name { get; init; } = "ProviderAuthError";
    public required ProviderAuthErrorData Data { get; init; }
}

public sealed record ProviderAuthErrorData
{
    public required string ProviderID { get; init; }
    public required string Message { get; init; }
}

public sealed record UnknownError
{
    public string Name { get; init; } = "UnknownError";
    public required UnknownErrorData Data { get; init; }
}

public sealed record UnknownErrorData
{
    public required string Message { get; init; }
}

public sealed record MessageOutputLengthError
{
    public string Name { get; init; } = "MessageOutputLengthError";
    public IReadOnlyDictionary<string, System.Text.Json.JsonElement>? Data { get; init; }
}

public sealed record MessageAbortedError
{
    public string Name { get; init; } = "MessageAbortedError";
    public required MessageAbortedErrorData Data { get; init; }
}

public sealed record MessageAbortedErrorData
{
    public required string Message { get; init; }
}
