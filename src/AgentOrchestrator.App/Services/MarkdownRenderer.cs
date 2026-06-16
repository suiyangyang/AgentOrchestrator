using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
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

                    // Detect task list item by checking for a TaskList inline
                    // in the first paragraph. (Markdig 0.38 stores the checked
                    // state as a Markdig.Extensions.TaskLists.TaskList LeafInline.)
                    bool? isChecked = null;
                    if (item.Count > 0 && item[0] is ParagraphBlock firstPara
                        && firstPara.Inline?.FirstChild is TaskList tl)
                    {
                        isChecked = tl.Checked;
                    }

                    Control prefix;
                    if (isChecked.HasValue)
                    {
                        prefix = RenderCheckbox(isChecked.Value);
                    }
                    else
                    {
                        var bullet = list.IsOrdered ? $"{counter}." : "•";
                        prefix = new TextBlock
                        {
                            Text = bullet,
                            FontSize = 12,
                            Margin = new Thickness(0, 2, 8, 0),
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
                        };
                    }
                    Grid.SetColumn(prefix, 0);
                    grid.Children.Add(prefix);

                    var itemPanel = new StackPanel { Spacing = 4 };
                    var isFirstChild = true;
                    foreach (Block child in item)
                    {
                        Control? rendered;
                        if (isFirstChild && isChecked.HasValue && child is LeafBlock leaf)
                        {
                            // Strip the task-list marker prefix ("[ ] " / "[x] ")
                            // from the raw text so we don't render it twice.
                            var text = GetBlockText(leaf);
                            if (text.Length >= 4 && text[0] == '[' && text[2] == ']' && text[3] == ' ')
                                text = text.Substring(4);
                            var tb = new TextBlock
                            {
                                TextWrapping = TextWrapping.Wrap,
                                LineHeight = 18,
                                Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x28))
                            };
                            tb.Inlines!.AddRange(ParseInlines(text));
                            rendered = tb;
                        }
                        else
                        {
                            rendered = RenderBlock(child);
                        }
                        if (rendered is not null)
                            itemPanel.Children.Add(rendered);
                        isFirstChild = false;
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

            case Table table:
                return RenderTable(table);

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

    private static Control RenderCheckbox(bool isChecked)
    {
        // Unchecked: 14x14 white box with a thin gray border.
        // Checked  : 14x14 green box with a white checkmark drawn as a Path.
        var border = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(3),
            BorderBrush = isChecked
                ? new SolidColorBrush(Color.FromRgb(0x18, 0xA5, 0x58))
                : new SolidColorBrush(Color.FromRgb(0xC5, 0xC8, 0xCE)),
            BorderThickness = new Thickness(1.25),
            Background = isChecked
                ? new SolidColorBrush(Color.FromRgb(0x18, 0xA5, 0x58))
                : Brushes.White,
            Margin = new Thickness(0, 2, 8, 0)
        };

        if (isChecked)
        {
            border.Child = new Path
            {
                Data = StreamGeometry.Parse("M 3,7 L 6,10 L 11,4"),
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(1)
            };
        }

        return border;
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

    private static Control RenderTable(Table table)
    {
        var colCount = Math.Max(1, table.ColumnDefinitions.Count);
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE5, 0xEA)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.White
        };

        var grid = new Grid();
        for (var c = 0; c < colCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        var rowIndex = 0;
        foreach (Block rowBlock in table)
        {
            if (rowBlock is not TableRow row) continue;

            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var colIndex = 0;
            foreach (Block cellBlock in row)
            {
                if (cellBlock is not TableCell cell) continue;

                var cellPanel = new StackPanel { Spacing = 4 };
                foreach (Block child in cell)
                {
                    var rendered = RenderBlock(child);
                    if (rendered is not null)
                        cellPanel.Children.Add(rendered);
                }

                // Header row gets a tinted background + bold
                var isHeader = rowIndex == 0 || row.IsHeader;
                if (isHeader)
                {
                    cellPanel.Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF6, 0xF8));
                    // promote any TextBlocks to bold for header readability
                    foreach (var child in cellPanel.Children)
                    {
                        if (child is TextBlock tb)
                            tb.FontWeight = FontWeight.SemiBold;
                    }
                }

                // Padding wrapper
                var cellContainer = new Border
                {
                    Padding = new Thickness(10, 8),
                    Child = cellPanel
                };
                Grid.SetRow(cellContainer, rowIndex);
                Grid.SetColumn(cellContainer, Math.Min(colIndex, colCount - 1));
                grid.Children.Add(cellContainer);

                colIndex++;
            }
            rowIndex++;
        }
        border.Child = grid;
        return border;
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

            // ── Strikethrough: ~~text~~ ──────────────────────────────────────
            if (i + 1 < len && text[i] == '~' && text[i + 1] == '~')
            {
                var end = text.IndexOf("~~", i + 2);
                if (end > i)
                {
                    var inner = text.Substring(i + 2, end - i - 2);
                    var strike = new Span { TextDecorations = TextDecorations.Strikethrough };
                    strike.Inlines.AddRange(ParseInlines(inner));
                    inlines.Add(strike);
                    i = end + 2;
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
                        var url = text.Substring(closeBracket + 2, closeParen - closeBracket - 2);
                        // Avalonia 12 has no Documents.Hyperlink — use an
                        // InlineUIContainer wrapping a HyperlinkButton instead.
                        var linkBtn = new HyperlinkButton
                        {
                            Content = linkText
                        };
                        var capturedUrl = url;
                        linkBtn.Click += (_, _) => OpenUrl(capturedUrl);
                        inlines.Add(new InlineUIContainer(linkBtn));
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
                if (ch is '*' or '_' or '`' or '[' or '~' or '\n')
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

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Swallow — links in chat are best-effort. The browser may not be available
            // in headless / CI / sandboxed environments.
        }
    }
}
