// Argha - 2026-02-26 - Phase 8.1: programmatic minimal document factory for tests

using System.Text;

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

        var streamContent = $"BT /F1 12 Tf 50 750 Td ({escapedText}) Tj ET";
        int streamLength = Encoding.Latin1.GetByteCount(streamContent);

        // Argha - 2026-02-26 - build each object as a complete string; offsets computed dynamically
        var obj1 = "1 0 obj\n<</Type /Catalog /Pages 2 0 R>>\nendobj\n";
        var obj2 = "2 0 obj\n<</Type /Pages /Kids [3 0 R] /Count 1>>\nendobj\n";
        var obj3 = "3 0 obj\n<</Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources <</Font <</F1 5 0 R>>>>>>\nendobj\n";
        var obj4 = $"4 0 obj\n<</Length {streamLength}>>\nstream\n{streamContent}\nendstream\nendobj\n";
        var obj5 = "5 0 obj\n<</Type /Font /Subtype /Type1 /BaseFont /Helvetica>>\nendobj\n";

        var allObjects = new[] { obj1, obj2, obj3, obj4, obj5 };
        var header = "%PDF-1.4\n";

        // Compute byte offset of each object
        var objOffsets = new long[5];
        long offset = Encoding.Latin1.GetByteCount(header);
        for (int i = 0; i < allObjects.Length; i++)
        {
            objOffsets[i] = offset;
            offset += Encoding.Latin1.GetByteCount(allObjects[i]);
        }

        long xrefOffset = offset;

        // Build xref table — each entry is exactly 20 bytes (10 digit offset + space + 5 digit gen + space + 'f'/'n' + space + LF)
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
    /// Writes a minimal PDF to a temp file and returns the path.
    /// Caller is responsible for deleting the file.
    /// </summary>
    public static string WriteTempPdf(string pageText = "Hello World Sample Document")
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, CreateMinimalPdf(pageText));
        return path;
    }
}
