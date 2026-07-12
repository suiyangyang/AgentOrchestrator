using System;
using AgentOrchestrator.App.Models.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

public sealed class DuplicateTaskGraphNameException(string name, TaskGraphDocumentKind documentKind)
    : InvalidOperationException(documentKind == TaskGraphDocumentKind.Template
        ? $"任务模板名称“{name}”已存在。"
        : $"任务图名称“{name}”已存在。")
{
}
