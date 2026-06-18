using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentOrchestrator.App.Services;

public sealed record ToolDisplayInfo(
    string PrimaryText,
    string? FilePath = null,
    string? LineRangeText = null,
    string? CodeText = null)
{
    public string DisplayTitle =>
        string.IsNullOrWhiteSpace(FilePath)
            ? PrimaryText
            : string.IsNullOrWhiteSpace(LineRangeText)
                ? $"{PrimaryText} {FilePath}"
                : $"{PrimaryText} {FilePath}, {LineRangeText}";
}

public static class ToolDisplayParser
{
    private static readonly string[] FilePathKeys =
    [
        "path",
        "file",
        "filePath",
        "filepath",
        "filename",
        "target",
        "targetPath",
        "targetFile",
        "target_file",
        "newPath",
        "newFile",
        "new_file",
        "oldPath",
        "oldFile",
        "old_file"
    ];

    public static ToolDisplayInfo Parse(
        string? toolKey,
        string? rawTitle,
        string? output,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, JsonElement>? input = null,
        IReadOnlyDictionary<string, JsonElement>? metadata = null)
    {
        var normalizedTitle = NormalizeLineEndings(rawTitle);
        var normalizedOutput = NormalizeLineEndings(output);

        var titleBits = ParseTitleBits(normalizedTitle);
        var numberedCode = TryExtractNumberedCode(normalizedOutput, out var firstLine, out var lastLine);
        var fencedCode = numberedCode is null ? TryExtractFencedCode(normalizedOutput) : null;

        var primaryText = !string.IsNullOrWhiteSpace(titleBits.ActionText)
            ? titleBits.ActionText!
            : HumanizeVerb(toolKey);

        var filePath = TryFindFilePath(input)
            ?? TryFindFilePath(metadata)
            ?? titleBits.FilePath
            ?? TryExtractFilePathFromOutput(normalizedOutput);

        var displayPath = BuildDisplayPath(filePath, titleBits.FilePath, workingDirectory);

        var lineRange = titleBits.LineRangeText;
        if (string.IsNullOrWhiteSpace(lineRange) && firstLine is not null)
        {
            lineRange = BuildLineRange(firstLine.Value, lastLine ?? firstLine.Value);
        }

        return new ToolDisplayInfo(
            PrimaryText: primaryText,
            FilePath: string.IsNullOrWhiteSpace(displayPath) ? null : displayPath,
            LineRangeText: string.IsNullOrWhiteSpace(lineRange) ? null : lineRange,
            CodeText: numberedCode ?? fencedCode);
    }

    public static ToolDisplayInfo Parse(string? toolName, string? output)
        => Parse(toolName, toolName, output, workingDirectory: null);

    private static (string? ActionText, string? FileName, string? FilePath, string? LineRangeText) ParseTitleBits(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return (null, null, null, null);
        }

        var singleLine = title
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(singleLine))
        {
            return (null, null, null, null);
        }

        var rangeMatch = Regex.Match(singleLine, @"\b(lines?|rows?)\s+(\d+)(?:\s*(?:-|to)\s*(\d+))?\b", RegexOptions.IgnoreCase);
        string? lineRange = null;
        var titleWithoutRange = singleLine;
        if (rangeMatch.Success)
        {
            var start = int.Parse(rangeMatch.Groups[2].Value);
            var end = rangeMatch.Groups[3].Success ? int.Parse(rangeMatch.Groups[3].Value) : start;
            lineRange = BuildLineRange(start, end);
            titleWithoutRange = singleLine.Remove(rangeMatch.Index, rangeMatch.Length).Trim().TrimEnd(',', ':');
        }

        var actionAndFile = Regex.Match(titleWithoutRange, @"^(?<action>[A-Za-z][A-Za-z/_-]*)\s+(?<file>.+)$");
        if (!actionAndFile.Success)
        {
            return (singleLine.Trim(), null, null, lineRange);
        }

        var action = HumanizeVerb(actionAndFile.Groups["action"].Value);
        var fileSegment = actionAndFile.Groups["file"].Value.Trim().Trim(',', ':');
        if (!LooksLikeFilePath(fileSegment))
        {
            return (action, null, null, lineRange);
        }

        return (action, Path.GetFileName(fileSegment), fileSegment, lineRange);
    }

    private static string HumanizeVerb(string? toolKey)
    {
        if (string.IsNullOrWhiteSpace(toolKey))
        {
            return "Tool";
        }

        var verb = toolKey
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim()
            ?? toolKey.Trim();
        if (string.IsNullOrWhiteSpace(verb))
        {
            return "Tool";
        }

        var first = verb[0];
        if (char.IsLower(first))
        {
            return char.ToUpperInvariant(first) + verb[1..];
        }

        return verb;
    }

    private static string? TryExtractNumberedCode(string? output, out int? firstLine, out int? lastLine)
    {
        firstLine = null;
        lastLine = null;
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var lines = output.Split('\n');
        var firstCodeIndex = -1;
        var numberedLineRegex = new Regex(@"^\s*(\d+):\s?", RegexOptions.Compiled);
        for (var i = 0; i < lines.Length; i++)
        {
            var match = numberedLineRegex.Match(lines[i]);
            if (!match.Success)
            {
                continue;
            }

            firstCodeIndex = i;
            firstLine = int.Parse(match.Groups[1].Value);
            break;
        }

        if (firstCodeIndex < 0)
        {
            return null;
        }

        for (var i = lines.Length - 1; i >= firstCodeIndex; i--)
        {
            var match = numberedLineRegex.Match(lines[i]);
            if (!match.Success)
            {
                continue;
            }

            lastLine = int.Parse(match.Groups[1].Value);
            break;
        }

        return string.Join("\n", lines[firstCodeIndex..]).TrimEnd();
    }

    private static string? TryExtractFencedCode(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var match = Regex.Match(
            output,
            "```(?:[A-Za-z0-9_+-]+)?\\n(?<code>[\\s\\S]*?)\\n```",
            RegexOptions.Compiled);
        return match.Success ? match.Groups["code"].Value.TrimEnd() : null;
    }

    private static string? TryFindFilePath(IReadOnlyDictionary<string, JsonElement>? values)
    {
        if (values is null)
        {
            return null;
        }

        foreach (var key in FilePathKeys)
        {
            foreach (var candidate in values)
            {
                if (!string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var extracted = TryReadPathValue(candidate.Value);
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    return extracted;
                }
            }
        }

        foreach (var pair in values)
        {
            var nested = pair.Value.ValueKind switch
            {
                JsonValueKind.Object => TryFindFilePath(pair.Value.EnumerateObject().ToDictionary(x => x.Name, x => x.Value, StringComparer.OrdinalIgnoreCase)),
                JsonValueKind.Array => TryFindFilePathInArray(pair.Value),
                _ => null
            };
            if (!string.IsNullOrWhiteSpace(nested))
            {
                return nested;
            }
        }

        return null;
    }

    private static string? TryFindFilePathInArray(JsonElement array)
    {
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                var nested = TryFindFilePath(item.EnumerateObject().ToDictionary(x => x.Name, x => x.Value, StringComparer.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string? TryReadPathValue(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = element.GetString()?.Trim();
        return LooksLikeFilePath(text) ? text : null;
    }

    private static string? TryExtractFilePathFromOutput(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (LooksLikeFilePath(trimmed))
            {
                return trimmed;
            }
        }

        return null;
    }

    private static bool LooksLikeFilePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Contains('\\', StringComparison.Ordinal) || value.Contains('/', StringComparison.Ordinal))
        {
            return true;
        }

        return Regex.IsMatch(value, @"\.[A-Za-z0-9]{1,10}$", RegexOptions.Compiled);
    }

    private static string? BuildDisplayPath(string? resolvedPath, string? fallbackPath, string? workingDirectory)
    {
        var candidate = string.IsNullOrWhiteSpace(resolvedPath) ? fallbackPath : resolvedPath;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var trimmed = candidate.Trim();
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return trimmed;
        }

        try
        {
            if (Path.IsPathRooted(trimmed))
            {
                return Path.GetRelativePath(workingDirectory, trimmed);
            }
        }
        catch
        {
            return trimmed;
        }

        return trimmed;
    }

    private static string BuildLineRange(int start, int end)
        => start == end ? $"line {start}" : $"lines {start} to {end}";

    private static string? NormalizeLineEndings(string? text)
        => text?.Replace("\r\n", "\n").Replace('\r', '\n');
}
