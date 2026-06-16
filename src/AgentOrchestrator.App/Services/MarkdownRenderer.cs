using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Markdig;
using Markdig.Syntax;

namespace AgentOrchestrator.App.Services;

public static class MarkdownRenderer
{
    /// <summary>Renders markdown into a StackPanel (Spacing=10).</summary>
    public static Control Render(string? markdown) => RenderCore(markdown, spacing: 10);

    /// <summary>Renders markdown into a compact StackPanel (Spacing=0).</summary>
    public static Control RenderInline(string? markdown) => RenderCore(markdown, spacing: 0);

    private static Control RenderCore(string? markdown, double spacing)
    {
        var panel = new StackPanel { Spacing = spacing };
        var source = markdown ?? string.Empty;
        if (string.IsNullOrWhiteSpace(source))
            return panel;

        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        var doc = Markdown.Parse(source, pipeline);

        foreach (Block block in doc)
        {
            var rendered = RenderBlock(block);
            if (rendered is not null)
                panel.Children.Add(rendered);
        }

        return panel;
    }

    private static Control? RenderBlock(Block block)
    {
        switch (block)
        {
            case HeadingBlock heading:
            {
                var fontSize = heading.Level switch
                {
                    1 => 18.0,
                    2 => 16.0,
                    3 => 14.0,
                    4 => 13.0,
                    _ => 12.0
                };
                var tb = new TextBlock
                {
                    FontWeight = FontWeight.Bold,
                    FontSize = fontSize,
                    LineHeight = 22,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 4),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x28))
                };
                tb.Inlines!.AddRange(ParseInlines(GetBlockText(heading)));
                return tb;
            }

            case ParagraphBlock paragraph:
            {
                var tb = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 18,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x28))
                };
                tb.Inlines!.AddRange(ParseInlines(GetBlockText(paragraph)));
                return tb;
            }

            case FencedCodeBlock fenced:
                return RenderCodeBlock(fenced);

            case CodeBlock code:
                return RenderCodeBlock(code);

            case ListBlock list:
            {
                var listPanel = new StackPanel { Spacing = 4 };
                var counter = 0;
                foreach (ListItemBlock item in list)
                {
                    counter++;
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                    grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

                    var bullet = list.IsOrdered ? $"{counter}." : "•";
                    var bulletTb = new TextBlock
                    {
                        Text = bullet,
                        FontSize = 12,
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    Grid.SetColumn(bulletTb, 0);
                    grid.Children.Add(bulletTb);

                    var itemPanel = new StackPanel { Spacing = 4 };
                    foreach (Block child in item)
                    {
                        var rendered = RenderBlock(child);
                        if (rendered is not null)
                            itemPanel.Children.Add(rendered);
                    }
                    Grid.SetColumn(itemPanel, 1);
                    grid.Children.Add(itemPanel);

                    listPanel.Children.Add(grid);
                }
                return listPanel;
            }

            case QuoteBlock quote:
            {
                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0xE1, 0xE8)),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(12, 4, 8, 4),
                    Background = Brushes.Transparent
                };
                var quotePanel = new StackPanel { Spacing = 3 };
                foreach (Block child in quote)
                {
                    var rendered = RenderBlock(child);
                    if (rendered is not null)
                        quotePanel.Children.Add(rendered);
                }
                border.Child = quotePanel;
                return border;
            }

            case ThematicBreakBlock:
            {
                return new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(Color.FromRgb(0xE2, 0xE5, 0xEA)),
                    Margin = new Thickness(0, 8)
                };
            }

            case HtmlBlock:
            {
                // Skip raw HTML blocks.
                return new StackPanel();
            }

            default:
            {
                if (block is LeafBlock leaf)
                {
                    var fallback = GetBlockText(leaf);
                    if (string.IsNullOrEmpty(fallback))
                        return null;
                    return new TextBlock
                    {
                        Text = fallback,
                        TextWrapping = TextWrapping.Wrap
                    };
                }
                return null;
            }
        }
    }

    private static Control RenderCodeBlock(CodeBlock code)
    {
        var outer = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF6, 0xF8)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10)
        };

        var panel = new StackPanel { Spacing = 0 };

        if (code is FencedCodeBlock fenced && !string.IsNullOrEmpty(fenced.Info))
        {
            panel.Children.Add(new TextBlock
            {
                Text = fenced.Info,
                Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0x96, 0xA0)),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6)
            });
        }

        var codeTb = new TextBlock
        {
            Text = GetCodeBlockText(code),
            TextWrapping = TextWrapping.Wrap
        };
        codeTb.Classes.Add("code-text");
        panel.Children.Add(codeTb);

        outer.Child = panel;
        return outer;
    }

    // ── Block text helpers ────────────────────────────────────────────────

    /// <summary>Gets the textual content of a leaf block by joining its lines with a space.</summary>
    private static string GetBlockText(LeafBlock block)
    {
        // StringLineGroup.Count returns -1 (lazy-init sentinel) for leaf blocks
        // whose inlines haven't been processed by Markdig yet — typical after
        // Markdown.Parse without a render pass. Math.Max keeps the List capacity
        // non-negative; in that case the loop below yields no parts and the
        // caller gets an empty string.
        var count = Math.Max(0, block.Lines.Count);
        if (count == 0)
            return string.Empty;

        var parts = new List<string>(count);
        foreach (var line in block.Lines)
            parts.Add(line.ToString() ?? string.Empty);
        return string.Join(" ", parts);
    }

    /// <summary>Gets the textual content of a code block, joining lines with newlines.</summary>
    private static string GetCodeBlockText(CodeBlock code)
    {
        // See note in GetBlockText about the -1 sentinel.
        var count = Math.Max(0, code.Lines.Count);
        if (count == 0)
            return string.Empty;

        var parts = new List<string>(count);
        foreach (var line in code.Lines)
            parts.Add(line.ToString() ?? string.Empty);
        return string.Join("\n", parts);
    }

    // ── Inline parser ─────────────────────────────────────────────────────

    /// <summary>
    /// Parses a markdown text string into a list of Avalonia <see cref="Inline"/> elements,
    /// supporting bold (**text** or __text__), italic (*text* or _text_), inline code (`text`),
    /// links ([text](url)), and line breaks (\n).
    /// </summary>
    private static List<Inline> ParseInlines(string text)
    {
        var inlines = new List<Inline>();
        if (string.IsNullOrEmpty(text)) return inlines;

        var len = text.Length;
        var i = 0;

        while (i < len)
        {
            // ── Inline code: `text` ────────────────────────────────────────
            if (text[i] == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end > i)
                {
                    var code = end > i + 1 ? text.Substring(i + 1, end - i - 1) : string.Empty;
                    inlines.Add(new Run(code)
                    {
                        FontFamily = new FontFamily("Cascadia Code, Consolas, Menlo, monospace"),
                        Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF6, 0xF8)),
                        Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x28))
                    });
                    i = end + 1;
                    continue;
                }
            }

            // ── Bold: **text** or __text__ ─────────────────────────────────
            if (i + 1 < len)
            {
                if (text[i] == '*' && text[i + 1] == '*')
                {
                    var end = text.IndexOf("**", i + 2);
                    if (end > i)
                    {
                        var inner = text.Substring(i + 2, end - i - 2);
                        var bold = new Bold();
                        bold.Inlines.AddRange(ParseInlines(inner));
                        inlines.Add(bold);
                        i = end + 2;
                        continue;
                    }
                }
                if (text[i] == '_' && text[i + 1] == '_')
                {
                    var end = text.IndexOf("__", i + 2);
                    if (end > i)
                    {
                        var inner = text.Substring(i + 2, end - i - 2);
                        var bold = new Bold();
                        bold.Inlines.AddRange(ParseInlines(inner));
                        inlines.Add(bold);
                        i = end + 2;
                        continue;
                    }
                }
            }

            // ── Italic: *text* or _text_ (single char, not adjacent to same char)
            if (text[i] == '*' && (i + 1 >= len || text[i + 1] != '*'))
            {
                var end = text.IndexOf('*', i + 1);
                if (end > i && (end + 1 >= len || text[end + 1] != '*'))
                {
                    var inner = text.Substring(i + 1, end - i - 1);
                    var italic = new Italic();
                    italic.Inlines.AddRange(ParseInlines(inner));
                    inlines.Add(italic);
                    i = end + 1;
                    continue;
                }
            }
            if (text[i] == '_' && (i + 1 >= len || text[i + 1] != '_'))
            {
                var end = text.IndexOf('_', i + 1);
                if (end > i && (end + 1 >= len || text[end + 1] != '_'))
                {
                    var inner = text.Substring(i + 1, end - i - 1);
                    var italic = new Italic();
                    italic.Inlines.AddRange(ParseInlines(inner));
                    inlines.Add(italic);
                    i = end + 1;
                    continue;
                }
            }

            // ── Link: [text](url) ──────────────────────────────────────────
            if (text[i] == '[')
            {
                var closeBracket = text.IndexOf(']', i + 1);
                if (closeBracket > i
                    && closeBracket + 1 < len
                    && text[closeBracket + 1] == '(')
                {
                    var closeParen = text.IndexOf(')', closeBracket + 2);
                    if (closeParen > closeBracket)
                    {
                        var linkText = text.Substring(i + 1, closeBracket - i - 1);
                        var underline = new Underline();
                        underline.Inlines.Add(new Run(linkText)
                        {
                            Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x6F, 0xEB))
                        });
                        inlines.Add(underline);
                        i = closeParen + 1;
                        continue;
                    }
                }
            }

            // ── Line break ─────────────────────────────────────────────────
            if (text[i] == '\n')
            {
                inlines.Add(new LineBreak());
                i++;
                continue;
            }

            // ── Plain text (collect until next special character) ──────────
            var next = len;
            for (var j = i + 1; j < len; j++)
            {
                var ch = text[j];
                if (ch is '*' or '_' or '`' or '[' or '\n')
                {
                    next = j;
                    break;
                }
            }
            var plain = text.Substring(i, next - i);
            if (plain.Length > 0)
                inlines.Add(new Run(plain));
            i = next;
        }

        return inlines;
    }
}
