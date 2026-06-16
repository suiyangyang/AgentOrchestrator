using OpenCode.Client.Internal;
using OpenCode.Client.Internal.Json;
using OpenCode.Client.Models;
using OpenCode.Client.Requests;

namespace OpenCode.Client.Services;

internal sealed class FileService : HttpServiceBase, IFileService
{
    public FileService(HttpClient http, OpenCodeClientOptions options, OpenCodeJsonSerializerContext json)
        : base(http, options, json) { }

    public Task<IReadOnlyList<FileNode>> ListAsync(string path, string? directory = null, CancellationToken ct = default)
    {
        var query = new List<KeyValuePair<string, string?>> { new("path", path) };
        if (directory is not null) query.Add(new("directory", directory));
        return GetAsync<IReadOnlyList<FileNode>>("/file", query, ct)!;
    }

    public Task<FileContent> ReadAsync(string path, string? directory = null, CancellationToken ct = default)
    {
        var query = new List<KeyValuePair<string, string?>> { new("path", path) };
        if (directory is not null) query.Add(new("directory", directory));
        return GetAsync<FileContent>("/file/content", query, ct)!;
    }

    public Task<IReadOnlyList<FileStatus>> StatusAsync(string? directory = null, CancellationToken ct = default)
        => GetAsync<IReadOnlyList<FileStatus>>("/file/status", QueryHelpers.WithDirectory(directory), ct)!;
}

internal sealed class FindService : HttpServiceBase, IFindService
{
    public FindService(HttpClient http, OpenCodeClientOptions options, OpenCodeJsonSerializerContext json)
        : base(http, options, json) { }

    public Task<IReadOnlyList<FindMatch>> TextAsync(string pattern, string? directory = null, CancellationToken ct = default)
    {
        var query = new List<KeyValuePair<string, string?>> { new("pattern", pattern) };
        if (directory is not null) query.Add(new("directory", directory));
        return GetAsync<IReadOnlyList<FindMatch>>("/find", query, ct)!;
    }

    public Task<IReadOnlyList<string>> FilesAsync(string query, string? directory = null, string? dirs = null, CancellationToken ct = default)
    {
        var q = new List<KeyValuePair<string, string?>> { new("query", query) };
        if (directory is not null) q.Add(new("directory", directory));
        if (dirs is not null) q.Add(new("dirs", dirs));
        return GetAsync<IReadOnlyList<string>>("/find/file", q, ct)!;
    }

    public Task<IReadOnlyList<Symbol>> SymbolsAsync(string query, string? directory = null, CancellationToken ct = default)
    {
        var q = new List<KeyValuePair<string, string?>> { new("query", query) };
        if (directory is not null) q.Add(new("directory", directory));
        return GetAsync<IReadOnlyList<Symbol>>("/find/symbol", q, ct)!;
    }
}

internal sealed class ToolService : HttpServiceBase, IToolService
{
    public ToolService(HttpClient http, OpenCodeClientOptions options, OpenCodeJsonSerializerContext json)
        : base(http, options, json) { }

    public Task<ToolIds> GetIdsAsync(string? directory = null, CancellationToken ct = default)
        => GetAsync<ToolIds>("/experimental/tool/ids", QueryHelpers.WithDirectory(directory), ct)!;

    public Task<IReadOnlyList<ToolListItem>> ListAsync(string provider, string model, string? directory = null, CancellationToken ct = default)
    {
        var query = new List<KeyValuePair<string, string?>>
        {
            new("provider", provider),
            new("model", model),
        };
        if (directory is not null) query.Add(new("directory", directory));
        return GetAsync<IReadOnlyList<ToolListItem>>("/experimental/tool", query, ct)!;
    }
}

internal sealed class LspService : HttpServiceBase, ILspService
{
    public LspService(HttpClient http, OpenCodeClientOptions options, OpenCodeJsonSerializerContext json)
        : base(http, options, json) { }

    public Task<IReadOnlyList<LspStatus>> StatusAsync(string? directory = null, CancellationToken ct = default)
        => GetAsync<IReadOnlyList<LspStatus>>("/lsp", QueryHelpers.WithDirectory(directory), ct)!;
}

internal sealed class FormatterService : HttpServiceBase, IFormatterService
{
    public FormatterService(HttpClient http, OpenCodeClientOptions options, OpenCodeJsonSerializerContext json)
        : base(http, options, json) { }

    public Task<IReadOnlyList<FormatterStatus>> StatusAsync(string? directory = null, CancellationToken ct = default)
        => GetAsync<IReadOnlyList<FormatterStatus>>("/formatter", QueryHelpers.WithDirectory(directory), ct)!;
}
