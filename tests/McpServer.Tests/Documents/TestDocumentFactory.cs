// Argha - 2026-02-26 - Phase 8.1: programmatic minimal document factory for tests
// Argha - 2026-02-27 - Phase 8.2: added Word, Excel, PowerPoint factory methods
// Argha - 2026-02-27 - Phase 8.3: added WriteTempDocxWithTable and WriteTempPdfWithTable

using System.Text;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace McpServer.Tests.Documents;

/// <summary>
/// Creates minimal but structurally valid documents for use in unit and integration tests.
/// </summary>
public static class TestDocumentFactory
{
    /// <summary>
    /// Creates a minimal valid PDF with text-layer content that PdfPig can read.
    /// The PDF has one page containing <paramref name="pageText"/>.
    /// </summary>
    public static byte[] CreateMinimalPdf(string pageText = "Hello World Sample Document")
    {
        // Argha - 2026-02-26 - escape parentheses and backslashes in PDF string literal
        var escapedText = pageText
            .Replace("\\", "\\\\")
            .Replace("(", "\\(")
            .Replace(")", "\\)");

        // Argha - 2026-02-27 - Phase 8.3: delegate to shared BuildPdf helper
        return BuildPdf($"BT /F1 12 Tf 50 750 Td ({escapedText}) Tj ET");
    }

    /// <summary>
    /// Writes a minimal PDF to a temp file and returns the path.
    /// Caller is responsible for deleting the file.
    /// </summary>
    public static string WriteTempPdf(string pageText = "Hello World Sample Document")
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, CreateMinimalPdf(pageText));
        return path;
    }

    /// <summary>
    /// Creates a minimal .docx file and returns the path.
    /// Caller is responsible for deleting the file.
    /// </summary>
    public static string WriteTempDocx(string text = "Hello Word Document")
    {
        // Argha - 2026-02-27 - use OpenXml SDK to create a structurally valid .docx
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.docx");
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new W.Document(
            new W.Body(
                new W.Paragraph(
                    new W.Run(
                        new W.Text(text)))));
        mainPart.Document.Save();
        return path;
    }

    /// <summary>
    /// Creates a minimal .xlsx file with one (or more) sheets and returns the path.
    /// Caller is responsible for deleting the file.
    /// </summary>
    public static string WriteTempXlsx(string sheetName = "Sheet1", string cellValue = "Hello Excel")
    {
        // Argha - 2026-02-27 - use ClosedXML to create a valid workbook with predictable content
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.xlsx");
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(sheetName);
        ws.Cell(1, 1).Value = cellValue;
        ws.Cell(1, 2).Value = "Second Cell";
        ws.Cell(2, 1).Value = "Row 2 Data";
        workbook.SaveAs(path);
        return path;
    }

    /// <summary>
    /// Creates a .docx file containing a simple 2-row × 2-column table.
    /// Row 0: ["Name", "Age"]; Row 1: ["Alice", "30"].
    /// Caller is responsible for deleting the file.
    /// </summary>
    public static string WriteTempDocxWithTable()
    {
        // Argha - 2026-02-27 - Phase 8.3: use OpenXml SDK to create a .docx with a real table element
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.docx");
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new W.Document(
            new W.Body(
                new W.Table(
                    new W.TableProperties(),
                    new W.TableRow(
                        new W.TableCell(new W.Paragraph(new W.Run(new W.Text("Name")))),
                        new W.TableCell(new W.Paragraph(new W.Run(new W.Text("Age"))))),
                    new W.TableRow(
                        new W.TableCell(new W.Paragraph(new W.Run(new W.Text("Alice")))),
                        new W.TableCell(new W.Paragraph(new W.Run(new W.Text("30"))))))));
        mainPart.Document.Save();
        return path;
    }

    /// <summary>
    /// Creates a PDF with a 2×2 table layout using absolute text positioning (Tm operator).
    /// Row 0: ["Name", "Age"] at Y=750; Row 1: ["Alice", "30"] at Y=730.
    /// X positions: col 0 = 50pt, col 1 = 200pt (150pt gap → detected as separate cells).
    /// Caller is responsible for deleting the file.
    /// </summary>
    public static string WriteTempPdfWithTable()
    {
        // Argha - 2026-02-27 - Phase 8.3: Tm sets absolute text position; each word is a distinct Word in PdfPig
        var streamContent =
            "BT /F1 12 Tf " +
            "1 0 0 1 50 750 Tm (Name) Tj " +
            "1 0 0 1 200 750 Tm (Age) Tj " +
            "1 0 0 1 50 730 Tm (Alice) Tj " +
            "1 0 0 1 200 730 Tm (30) Tj " +
            "ET";
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, BuildPdf(streamContent));
        return path;
    }

    // Argha - 2026-02-27 - shared helper: wraps arbitrary stream content into a single-page PDF
    private static byte[] BuildPdf(string streamContent)
    {
        int streamLength = Encoding.Latin1.GetByteCount(streamContent);

        var obj1 = "1 0 obj\n<</Type /Catalog /Pages 2 0 R>>\nendobj\n";
        var obj2 = "2 0 obj\n<</Type /Pages /Kids [3 0 R] /Count 1>>\nendobj\n";
        var obj3 = "3 0 obj\n<</Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources <</Font <</F1 5 0 R>>>>>>\nendobj\n";
        var obj4 = $"4 0 obj\n<</Length {streamLength}>>\nstream\n{streamContent}\nendstream\nendobj\n";
        var obj5 = "5 0 obj\n<</Type /Font /Subtype /Type1 /BaseFont /Helvetica>>\nendobj\n";

        var allObjects = new[] { obj1, obj2, obj3, obj4, obj5 };
        var header = "%PDF-1.4\n";

        var objOffsets = new long[5];
        long offset = Encoding.Latin1.GetByteCount(header);
        for (int i = 0; i < allObjects.Length; i++)
        {
            objOffsets[i] = offset;
            offset += Encoding.Latin1.GetByteCount(allObjects[i]);
        }
        long xrefOffset = offset;

        var xref = new StringBuilder();
        xref.Append("xref\n");
        xref.Append("0 6\n");
        xref.Append("0000000000 65535 f \n");
        for (int i = 0; i < 5; i++)
            xref.Append($"{objOffsets[i]:D10} 00000 n \n");

        var trailer = $"trailer\n<</Size 6 /Root 1 0 R>>\nstartxref\n{xrefOffset}\n%%EOF\n";

        var result = new StringBuilder();
        result.Append(header);
        foreach (var obj in allObjects)
            result.Append(obj);
        result.Append(xref);
        result.Append(trailer);

        return Encoding.Latin1.GetBytes(result.ToString());
    }

    /// <summary>
    /// Creates a minimal .pptx file with one slide per element in <paramref name="slideTexts"/>.
    /// Pass no arguments to get a single slide with "Hello PowerPoint".
    /// Caller is responsible for deleting the file.
    /// </summary>
    public static string WriteTempPptx(params string[] slideTexts)
    {
        // Argha - 2026-02-27 - use OpenXml SDK to create a structurally valid .pptx
        if (slideTexts.Length == 0) slideTexts = new[] { "Hello PowerPoint" };

        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.pptx");

        using var ppt = PresentationDocument.Create(path, PresentationDocumentType.Presentation);
        var presentationPart = ppt.AddPresentationPart();

        // Argha - 2026-02-27 - minimal slide master required by the OOXML spec
        var slideMasterPart = presentationPart.AddNewPart<SlideMasterPart>("rId1");
        var slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>("rId1");
        slideLayoutPart.SlideLayout = new SlideLayout(
            new CommonSlideData(new ShapeTree(
                new NonVisualGroupShapeProperties(
                    new NonVisualDrawingProperties { Id = 1U, Name = "" },
                    new NonVisualGroupShapeDrawingProperties(),
                    new ApplicationNonVisualDrawingProperties()),
                new GroupShapeProperties(new A.TransformGroup()))));
        slideLayoutPart.SlideLayout.Save();

        slideMasterPart.SlideMaster = new SlideMaster(
            new CommonSlideData(new ShapeTree(
                new NonVisualGroupShapeProperties(
                    new NonVisualDrawingProperties { Id = 1U, Name = "" },
                    new NonVisualGroupShapeDrawingProperties(),
                    new ApplicationNonVisualDrawingProperties()),
                new GroupShapeProperties(new A.TransformGroup()))),
            new ColorMap
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
            new SlideLayoutIdList(
                new SlideLayoutId { Id = 2049U, RelationshipId = "rId1" }));
        slideMasterPart.SlideMaster.Save();

        // Argha - 2026-02-27 - create one SlidePart per provided text
        var slideIdItems = new List<SlideId>();
        for (int i = 0; i < slideTexts.Length; i++)
        {
            string relId = $"rId{i + 2}";
            var slidePart = presentationPart.AddNewPart<SlidePart>(relId);
            slidePart.AddPart(slideLayoutPart, "rId1");

            slidePart.Slide = new Slide(
                new CommonSlideData(
                    new ShapeTree(
                        new NonVisualGroupShapeProperties(
                            new NonVisualDrawingProperties { Id = 1U, Name = "" },
                            new NonVisualGroupShapeDrawingProperties(),
                            new ApplicationNonVisualDrawingProperties()),
                        new GroupShapeProperties(new A.TransformGroup()),
                        new Shape(
                            new NonVisualShapeProperties(
                                new NonVisualDrawingProperties { Id = 2U, Name = $"Title {i + 1}" },
                                new NonVisualShapeDrawingProperties(
                                    new A.ShapeLocks { NoGrouping = true }),
                                new ApplicationNonVisualDrawingProperties(new PlaceholderShape())),
                            new ShapeProperties(),
                            new TextBody(
                                new A.BodyProperties(),
                                new A.ListStyle(),
                                new A.Paragraph(
                                    new A.Run(
                                        new A.Text(slideTexts[i]))))))));
            slidePart.Slide.Save();

            slideIdItems.Add(new SlideId { Id = (uint)(256 + i), RelationshipId = relId });
        }

        presentationPart.Presentation = new Presentation(
            new SlideMasterIdList(
                new SlideMasterId { Id = 2147483648U, RelationshipId = "rId1" }),
            new SlideIdList(slideIdItems.ToArray()),
            new SlideSize { Cx = 9144000, Cy = 6858000, Type = SlideSizeValues.Custom },
            new NotesSize { Cx = 6858000, Cy = 9144000 });
        presentationPart.Presentation.Save();

        return path;
    }
}
