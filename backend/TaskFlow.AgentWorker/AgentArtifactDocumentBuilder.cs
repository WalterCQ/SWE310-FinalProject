using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace TaskFlow.AgentWorker;

public static class AgentArtifactDocumentBuilder
{
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

    private static W.Paragraph Paragraph(string text, bool bold, int fontSize)
    {
        return new W.Paragraph(new W.Run(
            new W.RunProperties(
                new W.Bold { Val = bold },
                new W.FontSize { Val = fontSize.ToString() }),
            new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

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
