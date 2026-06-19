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
using Markdig.Syntax.Inlines;

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
                    LineHeight = 24,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 4),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x28))
                };
                tb.Inlines!.AddRange(RenderInlines(heading.Inline));
                return tb;
            }

            case ParagraphBlock paragraph:
            {
                var tb = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 24,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x28))
                };
                tb.Inlines!.AddRange(RenderInlines(paragraph.Inline));
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
                    ParagraphBlock? firstParagraph = item.Count > 0 ? item[0] as ParagraphBlock : null;
                    if (firstParagraph?.Inline?.FirstChild is TaskList tl)
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
                            var tb = new TextBlock
                            {
                                TextWrapping = TextWrapping.Wrap,
                                LineHeight = 18,
                                Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x28))
                            };
                            tb.Inlines!.AddRange(RenderInlines(firstParagraph?.Inline, stripTaskListMarker: true));
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

    private static List<Avalonia.Controls.Documents.Inline> RenderInlines(ContainerInline? container, bool stripTaskListMarker = false)
    {
        if (container is null)
        {
            return [];
        }

        var inlines = new List<Avalonia.Controls.Documents.Inline>();
        var skipTaskMarker = stripTaskListMarker;

        for (var current = container.FirstChild; current is not null; current = current.NextSibling)
        {
            switch (current)
            {
                case LiteralInline literal:
                {
                    var text = literal.Content.ToString() ?? string.Empty;
                    if (skipTaskMarker && text.Length >= 4 && text[0] == '[' && text[2] == ']' && text[3] == ' ')
                    {
                        text = text[4..];
                    }

                    if (!string.IsNullOrEmpty(text))
                    {
                        inlines.Add(new Run(text));
                    }

                    skipTaskMarker = false;
                    break;
                }

                case CodeInline code:
                    inlines.Add(new Run(code.Content) {
                        FontFamily = new FontFamily("Cascadia Code, Consolas, Menlo, monospace"),
                        Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF6, 0xF8)),
                        Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x28))
                    });
                    skipTaskMarker = false;
                    break;

                case LineBreakInline:
                    inlines.Add(new LineBreak());
                    skipTaskMarker = false;
                    break;

                case EmphasisInline emphasis:
                {
                    Span span = emphasis.DelimiterChar switch
                    {
                        '~' => new Span { TextDecorations = TextDecorations.Strikethrough },
                        '*' or '_' when emphasis.DelimiterCount >= 2 => new Bold(),
                        '*' or '_' => new Italic(),
                        _ => new Span()
                    };
                    span.Inlines.AddRange(RenderInlines(emphasis));
                    inlines.Add(span);
                    skipTaskMarker = false;
                    break;
                }

                case LinkInline link:
                {
                    var linkText = GetInlinePlainText(link);
                    if (string.IsNullOrWhiteSpace(linkText))
                    {
                        linkText = link.Url ?? string.Empty;
                    }

                    var linkBtn = new HyperlinkButton
                    {
                        Content = linkText
                    };
                    var capturedUrl = link.Url ?? link.GetDynamicUrl?.Invoke() ?? string.Empty;
                    linkBtn.Click += (_, _) => OpenUrl(capturedUrl);
                    inlines.Add(new InlineUIContainer(linkBtn));
                    skipTaskMarker = false;
                    break;
                }

                case ContainerInline nested:
                    inlines.AddRange(RenderInlines(nested));
                    skipTaskMarker = false;
                    break;
            }
        }

        return inlines;
    }

    private static string GetInlinePlainText(ContainerInline container)
    {
        var parts = new List<string>();
        for (var current = container.FirstChild; current is not null; current = current.NextSibling)
        {
            switch (current)
            {
                case LiteralInline literal:
                    parts.Add(literal.Content.ToString() ?? string.Empty);
                    break;
                case CodeInline code:
                    parts.Add(code.Content);
                    break;
                case LineBreakInline:
                    parts.Add("\n");
                    break;
                case ContainerInline nested:
                    parts.Add(GetInlinePlainText(nested));
                    break;
            }
        }

        return string.Concat(parts);
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
