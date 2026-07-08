using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using System.Text.RegularExpressions;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace TaskFlow.AgentWorker;

public static class AgentArtifactDocumentBuilder
{
    private const string Ink = "111827";
    private const string Muted = "667085";
    private const string Teal = "0F766E";
    private const string Line = "D0D5DD";
    private const string Surface = "F8FAFC";

    public static byte[] BuildReportDocx(string goal, IReadOnlyCollection<string> sections)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            var body = new W.Body(
                Paragraph("TaskFlow AI Report", true, 32),
                Paragraph($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC", false, 20),
                Paragraph("Objective", true, 26),
                Paragraph(goal, false, 22));

            foreach (var section in sections)
            {
                body.Append(Paragraph(section, false, 22));
            }

            mainPart.Document = new W.Document(body);
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    public static byte[] BuildReportDocxFromMarkdown(string markdown)
    {
        var report = ParseReportMarkdown(markdown);
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new W.Document();
            var body = new W.Body();

            body.Append(
                Paragraph("TaskFlow AI Report", true, 42, Teal),
                Paragraph(report.Title, true, 34, Ink),
                Paragraph($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC", false, 20, Muted),
                Paragraph(string.IsNullOrWhiteSpace(report.Audience) ? "Audience: TaskFlow workspace members" : $"Audience: {report.Audience}", false, 20, Muted),
                Paragraph("Scope and approval", true, 24, Ink),
                Paragraph("This report was generated from approved TaskFlow channel context and indexed sources. Review source coverage and acceptance checks before submission or delivery.", false, 22, Ink),
                PageBreak(),
                Paragraph("Contents", true, 30, Teal));

            foreach (var section in report.Sections)
            {
                body.Append(Paragraph(section.Heading, false, 22, Ink));
            }

            body.Append(
                PageBreak(),
                Paragraph("Requirements Matrix", true, 30, Teal),
                BuildRequirementsMatrix(report),
                Paragraph("Generated Report", true, 30, Teal));

            foreach (var section in report.Sections)
            {
                body.Append(Paragraph(section.Heading, true, 26, Teal));
                foreach (var block in section.Blocks)
                {
                    if (block.Kind == ReportBlockKind.Table)
                    {
                        body.Append(Table(block.Rows));
                    }
                    else
                    {
                        body.Append(Paragraph(block.Text, block.Kind == ReportBlockKind.Bullet, 21, Ink, block.Kind == ReportBlockKind.Bullet));
                    }
                }
            }

            body.Append(
                PageBreak(),
                Paragraph("Source Appendix", true, 30, Teal),
                BuildSourceAppendix(report),
                CreateSectionProperties(mainPart));

            mainPart.Document.Append(body);
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    public static byte[] BuildDeckPptx(string goal, IReadOnlyCollection<(string Title, string Body)> slides)
    {
        using var stream = new MemoryStream();
        using (var document = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            var presentationPart = document.AddPresentationPart();
            presentationPart.Presentation = new P.Presentation();

            var slideMasterPart = presentationPart.AddNewPart<SlideMasterPart>("rId1");
            var slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>("rId1");
            slideLayoutPart.SlideLayout = new P.SlideLayout(
                new P.CommonSlideData(CreateEmptyShapeTree()),
                new P.ColorMapOverride(new A.MasterColorMapping()))
            {
                Preserve = true
            };
            slideLayoutPart.SlideLayout.Save();

            slideMasterPart.SlideMaster = new P.SlideMaster(
                new P.CommonSlideData(CreateEmptyShapeTree()),
                new P.ColorMap
                {
                    Background1 = A.ColorSchemeIndexValues.Light1,
                    Text1 = A.ColorSchemeIndexValues.Dark1,
                    Background2 = A.ColorSchemeIndexValues.Light2,
                    Text2 = A.ColorSchemeIndexValues.Dark2,
                    Accent1 = A.ColorSchemeIndexValues.Accent1,
                    Accent2 = A.ColorSchemeIndexValues.Accent2,
                    Accent3 = A.ColorSchemeIndexValues.Accent3,
                    Accent4 = A.ColorSchemeIndexValues.Accent4,
                    Accent5 = A.ColorSchemeIndexValues.Accent5,
                    Accent6 = A.ColorSchemeIndexValues.Accent6,
                    Hyperlink = A.ColorSchemeIndexValues.Hyperlink,
                    FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink
                },
                new P.SlideLayoutIdList(new P.SlideLayoutId
                {
                    Id = 2147483649U,
                    RelationshipId = slideMasterPart.GetIdOfPart(slideLayoutPart)
                }),
                new P.TextStyles(new P.TitleStyle(), new P.BodyStyle(), new P.OtherStyle()));
            slideMasterPart.SlideMaster.Save();

            presentationPart.Presentation.Append(new P.SlideMasterIdList(new P.SlideMasterId
            {
                Id = 2147483648U,
                RelationshipId = presentationPart.GetIdOfPart(slideMasterPart)
            }));

            var slideIdList = presentationPart.Presentation.AppendChild(new P.SlideIdList());
            var slideId = 256U;
            foreach (var slide in slides.Prepend((Title: "TaskFlow AI Deck", Body: goal)))
            {
                var slidePart = presentationPart.AddNewPart<SlidePart>();
                slidePart.Slide = new P.Slide(new P.CommonSlideData(CreateSlideShapeTree(slide.Title, slide.Body)));
                slidePart.AddPart(slideLayoutPart);
                slidePart.Slide.Save();

                slideIdList.Append(new P.SlideId
                {
                    Id = slideId++,
                    RelationshipId = presentationPart.GetIdOfPart(slidePart)
                });
            }

            presentationPart.Presentation.Append(
                new P.SlideSize { Cx = 9144000, Cy = 5143500, Type = P.SlideSizeValues.Screen16x9 },
                new P.NotesSize { Cx = 6858000, Cy = 9144000 });
            presentationPart.Presentation.Save();
        }

        return stream.ToArray();
    }

    private static W.Paragraph Paragraph(string text, bool bold, int fontSize, string color = Ink, bool bullet = false)
    {
        var normalizedText = bullet ? $"- {text}" : text;
        return new W.Paragraph(new W.Run(
            new W.RunProperties(
                new W.Bold { Val = bold },
                new W.Color { Val = color },
                new W.FontSize { Val = fontSize.ToString() }),
            new W.Text(normalizedText) { Space = SpaceProcessingModeValues.Preserve }))
        {
            ParagraphProperties = new W.ParagraphProperties(
                new W.SpacingBetweenLines { After = bullet ? "80" : "140", Line = "276", LineRule = W.LineSpacingRuleValues.Auto })
        };
    }

    private static W.Paragraph PageBreak()
    {
        return new W.Paragraph(new W.Run(new W.Break { Type = W.BreakValues.Page }));
    }

    private static W.SectionProperties CreateSectionProperties(MainDocumentPart mainPart)
    {
        var headerPart = mainPart.AddNewPart<HeaderPart>();
        headerPart.Header = new W.Header(Paragraph("TaskFlow AI Report", true, 18, Teal));
        headerPart.Header.Save();

        var footerPart = mainPart.AddNewPart<FooterPart>();
        footerPart.Footer = new W.Footer(Paragraph("Generated by TaskFlow Agent from approved workspace context", false, 16, Muted));
        footerPart.Footer.Save();

        return new W.SectionProperties(
            new W.HeaderReference { Type = W.HeaderFooterValues.Default, Id = mainPart.GetIdOfPart(headerPart) },
            new W.FooterReference { Type = W.HeaderFooterValues.Default, Id = mainPart.GetIdOfPart(footerPart) },
            new W.PageSize { Width = 12240, Height = 15840 },
            new W.PageMargin { Top = 1080, Right = 1080, Bottom = 1080, Left = 1080, Header = 540, Footer = 540 });
    }

    private static W.Table BuildRequirementsMatrix(ReportDocument report)
    {
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "Area", "Requirement / Evidence", "Source" }
        };

        foreach (var section in report.Sections.Where(section => !section.Heading.Contains("source appendix", StringComparison.OrdinalIgnoreCase)))
        {
            var evidenceRows = section.Blocks
                .Where(block => block.Kind == ReportBlockKind.Table)
                .SelectMany(block => block.Rows.Skip(1))
                .ToArray();

            if (evidenceRows.Length == 0)
            {
                var summary = section.Blocks.FirstOrDefault(block => block.Kind != ReportBlockKind.Table)?.Text ?? "Review section content.";
                rows.Add(new[] { section.Heading, Shorten(summary, 180), "See section" });
                continue;
            }

            foreach (var evidence in evidenceRows.Take(4))
            {
                rows.Add(new[]
                {
                    section.Heading,
                    evidence.ElementAtOrDefault(0) ?? "Evidence needed",
                    evidence.ElementAtOrDefault(1) ?? "Source pending"
                });
            }
        }

        return Table(rows);
    }

    private static W.Table BuildSourceAppendix(ReportDocument report)
    {
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "Source", "Usage" }
        };

        var sourceLines = report.Sections
            .Where(section => section.Heading.Contains("source appendix", StringComparison.OrdinalIgnoreCase))
            .SelectMany(section => section.Blocks)
            .Where(block => block.Kind != ReportBlockKind.Table)
            .Select(block => block.Text)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct()
            .ToArray();

        if (sourceLines.Length == 0)
        {
            rows.Add(new[] { "No explicit source appendix was present.", "Review generated sections for inline source ids." });
        }
        else
        {
            foreach (var source in sourceLines)
            {
                rows.Add(new[] { source, "Referenced by report claims or evidence rows." });
            }
        }

        return Table(rows);
    }

    private static W.Table Table(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var table = new W.Table(new W.TableProperties(
            new W.TableWidth { Type = W.TableWidthUnitValues.Pct, Width = "5000" },
            new W.TableBorders(
                new W.TopBorder { Val = W.BorderValues.Single, Color = Line, Size = 6 },
                new W.BottomBorder { Val = W.BorderValues.Single, Color = Line, Size = 6 },
                new W.LeftBorder { Val = W.BorderValues.Single, Color = Line, Size = 6 },
                new W.RightBorder { Val = W.BorderValues.Single, Color = Line, Size = 6 },
                new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Color = Line, Size = 4 },
                new W.InsideVerticalBorder { Val = W.BorderValues.Single, Color = Line, Size = 4 })));

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = new W.TableRow();
            foreach (var cellText in rows[rowIndex])
            {
                row.Append(new W.TableCell(
                    new W.TableCellProperties(
                        new W.Shading { Fill = rowIndex == 0 ? Teal : Surface },
                        new W.TableCellMargin(
                            new W.TopMargin { Width = "90", Type = W.TableWidthUnitValues.Dxa },
                            new W.BottomMargin { Width = "90", Type = W.TableWidthUnitValues.Dxa },
                            new W.LeftMargin { Width = "120", Type = W.TableWidthUnitValues.Dxa },
                            new W.RightMargin { Width = "120", Type = W.TableWidthUnitValues.Dxa })),
                    Paragraph(cellText, rowIndex == 0, rowIndex == 0 ? 19 : 18, rowIndex == 0 ? "FFFFFF" : Ink)));
            }
            table.Append(row);
        }

        return table;
    }

    private static ReportDocument ParseReportMarkdown(string markdown)
    {
        var lines = markdown
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.TrimEnd())
            .ToArray();
        lines = StripMarkdownFrontMatter(lines).ToArray();

        var title = lines.Select(TryReadHeading1).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "TaskFlow AI Report";
        var audience = ReadPrefixedLine(lines, "Audience:");
        var confidence = ReadPrefixedLine(lines, "Confidence note:");
        var sections = new List<ReportSection>();
        ReportSection? current = null;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') && TryReadHeading1(line) is not null)
            {
                continue;
            }

            var heading = TryReadHeading2(line);
            if (heading is not null)
            {
                current = new ReportSection(heading);
                sections.Add(current);
                continue;
            }

            current ??= new ReportSection("Overview");
            if (!sections.Contains(current))
            {
                sections.Add(current);
            }

            if (line.StartsWith('|'))
            {
                var tableLines = new List<string> { line };
                while (index + 1 < lines.Length && lines[index + 1].Trim().StartsWith('|'))
                {
                    tableLines.Add(lines[++index].Trim());
                }

                var rows = ParseTableRows(tableLines);
                if (rows.Count > 0)
                {
                    current.Blocks.Add(new ReportBlock(ReportBlockKind.Table, string.Empty, rows));
                }
                continue;
            }

            var bullet = Regex.Replace(line, @"^[-*]\s+", string.Empty).Trim();
            current.Blocks.Add(new ReportBlock(line == bullet ? ReportBlockKind.Paragraph : ReportBlockKind.Bullet, CleanMarkdown(bullet), []));
        }

        if (!string.IsNullOrWhiteSpace(confidence) && sections.Count > 0)
        {
            sections[0].Blocks.Insert(0, new ReportBlock(ReportBlockKind.Paragraph, $"Confidence note: {confidence}", []));
        }

        return new ReportDocument(title, audience, sections.Count == 0 ? [new ReportSection("Overview")] : sections);
    }

    private static IEnumerable<string> StripMarkdownFrontMatter(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0 || lines[0].Trim() != "---")
        {
            return lines;
        }

        for (var index = 1; index < lines.Count; index++)
        {
            if (lines[index].Trim() == "---")
            {
                return lines.Skip(index + 1);
            }
        }

        return lines;
    }

    private static string? TryReadHeading1(string line)
    {
        var match = Regex.Match(line, @"^#\s+(.+)$");
        return match.Success ? CleanMarkdown(match.Groups[1].Value) : null;
    }

    private static string? TryReadHeading2(string line)
    {
        var match = Regex.Match(line, @"^##\s+(.+)$");
        return match.Success ? CleanMarkdown(match.Groups[1].Value) : null;
    }

    private static string ReadPrefixedLine(IReadOnlyList<string> lines, string prefix)
    {
        return lines
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?
            .Substring(prefix.Length)
            .Trim() ?? string.Empty;
    }

    private static IReadOnlyList<IReadOnlyList<string>> ParseTableRows(IReadOnlyList<string> tableLines)
    {
        return tableLines
            .Select(line => line.Trim('|').Split('|').Select(cell => CleanMarkdown(cell.Trim())).ToArray())
            .Where(cells => cells.Any(cell => !Regex.IsMatch(cell, @"^-+$")))
            .Select(cells => (IReadOnlyList<string>)cells)
            .ToArray();
    }

    private static string CleanMarkdown(string value)
    {
        var cleaned = Regex.Replace(value, @"[`*_]+", string.Empty).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Not specified" : cleaned;
    }

    private static string Shorten(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength].TrimEnd() + "...";
    }

    private enum ReportBlockKind
    {
        Paragraph,
        Bullet,
        Table
    }

    private sealed record ReportDocument(
        string Title,
        string Audience,
        IReadOnlyList<ReportSection> Sections);

    private sealed class ReportSection(string heading)
    {
        public string Heading { get; } = heading;
        public List<ReportBlock> Blocks { get; } = [];
    }

    private sealed record ReportBlock(
        ReportBlockKind Kind,
        string Text,
        IReadOnlyList<IReadOnlyList<string>> Rows);

    private static P.ShapeTree CreateEmptyShapeTree()
    {
        return new P.ShapeTree(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = 1U, Name = string.Empty },
                new P.NonVisualGroupShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties(new A.TransformGroup()));
    }

    private static P.ShapeTree CreateSlideShapeTree(string title, string body)
    {
        var shapeTree = CreateEmptyShapeTree();
        shapeTree.Append(
            CreateTextShape(2U, "Title", title, 457200, 365760, 8229600, 685800, 3200, true),
            CreateTextShape(3U, "Content", body, 685800, 1295400, 7772400, 3200400, 2000, false));
        return shapeTree;
    }

    private static P.Shape CreateTextShape(
        uint id,
        string name,
        string text,
        long x,
        long y,
        long cx,
        long cy,
        int fontSize,
        bool bold)
    {
        var paragraphs = text
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .DefaultIfEmpty(string.Empty)
            .Select(line => new A.Paragraph(
                new A.Run(
                    new A.RunProperties { FontSize = fontSize, Bold = bold },
                    new A.Text(line)),
                new A.EndParagraphRunProperties { Language = "en-US" }));

        var textBody = new P.TextBody(
            new A.BodyProperties { Wrap = A.TextWrappingValues.Square },
            new A.ListStyle());
        textBody.Append(paragraphs);

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(new A.Transform2D(
                new A.Offset { X = x, Y = y },
                new A.Extents { Cx = cx, Cy = cy })),
            textBody);
    }
}
