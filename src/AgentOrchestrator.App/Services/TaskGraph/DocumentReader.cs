using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AgentOrchestrator.App.Services.TaskGraph;

public sealed class DocumentReader : IDocumentReader
{
    public async Task<string> ReadAsync(string filePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new TaskGraphValidationException("请选择需要编排的文档。");
        }

        if (!File.Exists(filePath))
        {
            throw new TaskGraphValidationException($"文档不存在: {filePath}");
        }

        var extension = Path.GetExtension(filePath);
        if (!string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase))
        {
            throw new TaskGraphValidationException("当前仅支持导入 .md 和 .txt 文档。");
        }

        return await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
    }
}
