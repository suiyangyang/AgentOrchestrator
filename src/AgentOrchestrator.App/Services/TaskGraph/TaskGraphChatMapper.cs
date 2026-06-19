using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AgentOrchestrator.App.Models.Chat;
using AgentOrchestrator.App.Models.Sidebar;
using AgentOrchestrator.App.ViewModels;

namespace AgentOrchestrator.App.Services.TaskGraph;

public static class TaskGraphChatMapper
{
    public static ChatMessageViewModel MapRemoteMessage(RemoteMessage msg, string? workingDirectory)
    {
        var author = msg.Role == ChatRole.User ? "你" : "Codex";
        var vm = new ChatMessageViewModel(msg.Id, msg.Role, author);
        foreach (var block in msg.Blocks)
        {
            vm.Blocks.Add(CreateRemoteBlock(block, workingDirectory));
        }

        return vm;
    }

    public static void ApplyChunk(ChatMessageViewModel assistant, Services.Agent.ChatStreamChunk chunk, string? workingDirectory)
    {
        var block = FindBlockForChunk(assistant, chunk);
        if (block is null)
        {
            block = CreateBlockForChunk(chunk, workingDirectory);
            if (block is null)
            {
                return;
            }

            assistant.Blocks.Add(block);
        }

        switch (chunk.Kind)
        {
            case ChatBlockKind.Text:
            case ChatBlockKind.Thought:
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    block.Text = (block.Text ?? string.Empty) + chunk.Content;
                }
                break;

            case ChatBlockKind.Image:
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    block.Text = chunk.Content;
                }
                break;

            case ChatBlockKind.Tool:
            case ChatBlockKind.Task:
                var (toolName, toolState, toolOutput) = ParseToolChunk(block, chunk);
                block.ToolName = toolName;
                block.ToolState = toolState;
                block.ToolOutput = toolOutput;
                break;
        }
    }

    public static ChatMessageViewModel EnsureAssistantMessage(
        IList<ChatMessageViewModel> messages,
        string messageId)
    {
        var existing = messages.LastOrDefault(x => string.Equals(x.Id, messageId, StringComparison.Ordinal));
        if (existing is not null)
        {
            return existing;
        }

        var created = new ChatMessageViewModel(messageId, ChatRole.Assistant, "Codex")
        {
            IsStreaming = true,
        };
        messages.Add(created);
        return created;
    }

    private static ChatBlockViewModel CreateRemoteBlock(RemoteBlock block, string? workingDirectory)
        => block.Kind switch
        {
            ChatBlockKind.Text => new ChatBlockViewModel(ChatBlockKind.Text, block.PartId, block.Text),
            ChatBlockKind.Thought => new ChatBlockViewModel(ChatBlockKind.Thought, block.PartId, block.Text, isExpanded: false),
            ChatBlockKind.Image => new ChatBlockViewModel(ChatBlockKind.Image, block.PartId, block.Text),
            ChatBlockKind.Tool => CreateToolBlock(ChatBlockKind.Tool, block, workingDirectory),
            ChatBlockKind.Task => CreateToolBlock(ChatBlockKind.Task, block, workingDirectory),
            _ => new ChatBlockViewModel(ChatBlockKind.Text, block.PartId, block.Text),
        };

    private static ChatBlockViewModel CreateToolBlock(
        ChatBlockKind kind,
        RemoteBlock block,
        string? workingDirectory)
    {
        var vm = new ChatBlockViewModel(
            kind,
            block.PartId,
            kind == ChatBlockKind.Task ? block.ToolName ?? "Task" : block.ToolName ?? "tool",
            block.ToolState ?? ToolState.Completed,
            block.ToolOutput,
            isExpanded: false)
        {
            ToolWorkingDirectory = workingDirectory,
            ToolInput = block.ToolInput,
            ToolQuestion = block.Question,
        };

        return vm;
    }

    private static ChatBlockViewModel? CreateBlockForChunk(
        Services.Agent.ChatStreamChunk chunk,
        string? workingDirectory)
    {
        return chunk.Kind switch
        {
            ChatBlockKind.Text => new ChatBlockViewModel(ChatBlockKind.Text, chunk.PartId, string.Empty),
            ChatBlockKind.Thought => new ChatBlockViewModel(ChatBlockKind.Thought, chunk.PartId, string.Empty, isExpanded: false),
            ChatBlockKind.Tool => new ChatBlockViewModel(
                ChatBlockKind.Tool,
                chunk.PartId,
                ExtractToolNameFromChunk(chunk.Content),
                ToolState.Running,
                string.Empty,
                isExpanded: false)
            {
                ToolWorkingDirectory = workingDirectory,
            },
            ChatBlockKind.Task => new ChatBlockViewModel(
                ChatBlockKind.Task,
                chunk.PartId,
                ExtractToolNameFromChunk(chunk.Content),
                ToolState.Running,
                string.Empty,
                isExpanded: false)
            {
                ToolWorkingDirectory = workingDirectory,
            },
            ChatBlockKind.Image => new ChatBlockViewModel(ChatBlockKind.Image, chunk.PartId, chunk.Content),
            _ => null,
        };
    }

    private static ChatBlockViewModel? FindBlockForChunk(ChatMessageViewModel assistant, Services.Agent.ChatStreamChunk chunk)
    {
        if (!string.IsNullOrEmpty(chunk.PartId))
        {
            var matchByPart = assistant.Blocks.LastOrDefault(b => string.Equals(b.PartId, chunk.PartId, StringComparison.Ordinal));
            if (matchByPart is not null)
            {
                return matchByPart;
            }
        }

        var last = assistant.Blocks.LastOrDefault();
        if (last is null || last.Kind != chunk.Kind)
        {
            return null;
        }

        if (chunk.Kind is not (ChatBlockKind.Tool or ChatBlockKind.Task))
        {
            return last;
        }

        return string.Equals(last.ToolName ?? string.Empty, ExtractToolNameFromChunk(chunk.Content), StringComparison.Ordinal)
            ? last
            : null;
    }

    private static (string name, ToolState state, string? output) ParseToolChunk(
        ChatBlockViewModel block,
        Services.Agent.ChatStreamChunk chunk)
    {
        var name = block.ToolName ?? ExtractToolNameFromChunk(chunk.Content);
        var state = block.ToolState;
        var output = block.ToolOutput;
        if (chunk.Content.StartsWith("[", StringComparison.Ordinal))
        {
            var rb = chunk.Content.IndexOf(']');
            if (rb > 0)
            {
                var stateStr = chunk.Content.Substring(1, rb - 1);
                if (Enum.TryParse<ToolState>(stateStr, out var parsed))
                {
                    state = parsed;
                }

                var rest = chunk.Content[(rb + 1)..].TrimStart('\n', ' ');
                var nl = rest.IndexOf('\n');
                if (nl > 0)
                {
                    if (string.IsNullOrEmpty(name))
                    {
                        name = rest[..nl].Trim();
                    }

                    output = rest[(nl + 1)..];
                }
                else if (string.IsNullOrEmpty(name))
                {
                    name = rest.Trim();
                }
            }
        }

        return (string.IsNullOrWhiteSpace(name) ? "tool" : name, state, output);
    }

    private static string ExtractToolNameFromChunk(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "tool";
        }

        if (content.StartsWith("[", StringComparison.Ordinal))
        {
            var rb = content.IndexOf(']');
            if (rb > 0)
            {
                var rest = content[(rb + 1)..].TrimStart('\n', ' ');
                var nl = rest.IndexOf('\n');
                var name = nl > 0 ? rest[..nl] : rest;
                return string.IsNullOrWhiteSpace(name) ? "tool" : name.Trim();
            }
        }

        var nlFallback = content.IndexOf('\n');
        var fallback = (nlFallback > 0 ? content[..nlFallback] : content).Trim();
        return string.IsNullOrWhiteSpace(fallback) ? "tool" : fallback;
    }
}
