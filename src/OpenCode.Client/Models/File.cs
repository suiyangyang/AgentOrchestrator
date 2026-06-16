namespace OpenCode.Client.Models;

public sealed record FileNode
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Absolute { get; init; }
    public required string Type { get; init; }
    public bool Ignored { get; init; }
}

public sealed record FileContent
{
    public required string Type { get; init; }
    public required string Content { get; init; }
    public string? Diff { get; init; }
    public FileContentPatch? Patch { get; init; }
    public string? Encoding { get; init; }
    public string? MimeType { get; init; }
}

public sealed record FileContentPatch
{
    public required string OldFileName { get; init; }
    public required string NewFileName { get; init; }
    public string? OldHeader { get; init; }
    public string? NewHeader { get; init; }
    public required IReadOnlyList<PatchHunk> Hunks { get; init; }
    public string? Index { get; init; }
}

public sealed record PatchHunk
{
    public long OldStart { get; init; }
    public long OldLines { get; init; }
    public long NewStart { get; init; }
    public long NewLines { get; init; }
    public required IReadOnlyList<string> Lines { get; init; }
}

/// <summary>
/// File status (from /file/status). This is a separate type from FileNode and FileContent.
/// Note: "File" is a reserved keyword pattern; fully-qualified usage preferred.
/// </summary>
public sealed record FileStatus
{
    public required string Path { get; init; }
    public long Added { get; init; }
    public long Removed { get; init; }
    public required string Status { get; init; }
}

public sealed record Symbol
{
    public required string Name { get; init; }
    public long Kind { get; init; }
    public required SymbolLocation Location { get; init; }
}

public sealed record SymbolLocation
{
    public required string Uri { get; init; }
    public required Range Range { get; init; }
}
