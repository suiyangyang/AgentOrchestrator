namespace OpenCode.Client.Models;

public abstract record FilePartSource;

public sealed record FileSource : FilePartSource
{
    public string Type { get; init; } = "file";
    public required FilePartSourceText Text { get; init; }
    public required string Path { get; init; }
}

public sealed record SymbolSource : FilePartSource
{
    public string Type { get; init; } = "symbol";
    public required FilePartSourceText Text { get; init; }
    public required string Path { get; init; }
    public required Range Range { get; init; }
    public required string Name { get; init; }
    public long Kind { get; init; }
}

public sealed record FilePartSourceText
{
    public required string Value { get; init; }
    public long Start { get; init; }
    public long End { get; init; }
}

public sealed record Range
{
    public required Position Start { get; init; }
    public required Position End { get; init; }
}

public sealed record Position
{
    public long Line { get; init; }
    public long Character { get; init; }
}
