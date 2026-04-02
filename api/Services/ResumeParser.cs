using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using iTextSharp.text.pdf;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace CareerCoach.Services;

/// <summary>
/// Extracts raw text from resume files (PDF, DOCX).
/// All analysis and scoring is delegated to the LLM via agent tools.
/// </summary>
public class ResumeParser
{
    private static readonly Regex PdfTextTokenRegex = new(
        @"(?<token>\((?:\\.|[^\\)])*\)|<[\da-fA-F\s]+>)\s*(?<op>Tj|TJ|'|"")|(?<lineOp>\bT\*|\bTD|\bTd)\b",
        RegexOptions.Compiled);

    public async Task<ResumeParseResult> ParseResumeAsync(Stream fileStream, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        try
        {
            string text = extension switch
            {
                ".pdf"  => await ParsePdfAsync(fileStream),
                ".docx" => await ParseDocxAsync(fileStream),
                ".doc"  => throw new NotSupportedException("Legacy .doc format is not supported. Please convert to .docx"),
                _       => throw new NotSupportedException($"File type '{extension}' is not supported. Please upload PDF or DOCX.")
            };

            return new ResumeParseResult
            {
                Success       = true,
                Text          = text,
                WordCount     = CountWords(text),
                CharacterCount = text.Length,
                FileName      = fileName,
                FileType      = extension
            };
        }
        catch (Exception ex)
        {
            return new ResumeParseResult
            {
                Success      = false,
                ErrorMessage = $"Failed to parse resume: {ex.Message}",
                FileName     = fileName,
                FileType     = extension
            };
        }
    }

    private async Task<string> ParsePdfAsync(Stream stream)
    {
        stream.Position = 0;
        if (OperatingSystem.IsMacOS())
        {
            var nativeText = await TryExtractPdfTextWithPdfKitAsync(stream);
            if (!string.IsNullOrWhiteSpace(nativeText) && CountWords(nativeText) >= 40)
                return nativeText;
        }

        stream.Position = 0;
        var text = new StringBuilder();
        using var document = UglyToad.PdfPig.PdfDocument.Open(stream);
        foreach (var page in document.GetPages())
        {
            var pageText = page.Text;
            if (!string.IsNullOrWhiteSpace(pageText))
                text.AppendLine(pageText);
        }

        var extracted = CleanupExtractedText(text.ToString());
        if (!string.IsNullOrWhiteSpace(extracted) && CountWords(extracted) >= 20)
            return extracted;

        stream.Position = 0;
        var fallbackText = new StringBuilder();
        using var fallbackReader = new PdfReader(stream);
        for (var page = 1; page <= fallbackReader.NumberOfPages; page++)
        {
            var pageBytes = fallbackReader.GetPageContent(page);
            if (pageBytes != null)
                fallbackText.AppendLine(ExtractTextFromPdfContent(pageBytes));
        }
        return CleanupExtractedText(fallbackText.ToString());
    }

    private Task<string> ParseDocxAsync(Stream stream)
    {
        return Task.Run(() =>
        {
            var text = new StringBuilder();
            using var doc = WordprocessingDocument.Open(stream, false);
            var body = doc.MainDocumentPart?.Document?.Body
                ?? throw new InvalidOperationException("Document body is empty or invalid");

            foreach (var element in body.Elements())
            {
                if (element is Table table)
                {
                    foreach (var row in table.Elements<TableRow>())
                    {
                        var cellTexts = row.Elements<TableCell>()
                            .Select(cell => string.Join(" ",
                                cell.Descendants<Paragraph>().Select(p => p.InnerText.Trim())).Trim())
                            .Where(t => t.Length > 0);
                        var rowText = string.Join("  ", cellTexts).Trim();
                        if (rowText.Length > 0)
                            text.AppendLine(rowText);
                    }
                }
                else
                {
                    foreach (var paragraph in element.Descendants<Paragraph>())
                    {
                        var line = paragraph.InnerText.Trim();
                        if (line.Length > 0)
                            text.AppendLine(line);
                    }
                }
            }
            return CleanupExtractedText(text.ToString());
        });
    }

    private async Task<string?> TryExtractPdfTextWithPdfKitAsync(Stream stream)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "career-coach-pdf", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var pdfPath    = Path.Combine(tempDir, "resume.pdf");
        var scriptPath = Path.Combine(tempDir, "extract.swift");
        var cacheDir   = Path.Combine(tempDir, "module-cache");
        Directory.CreateDirectory(cacheDir);
        try
        {
            stream.Position = 0;
            await using (var fs = File.Create(pdfPath))
                await stream.CopyToAsync(fs);

            await File.WriteAllTextAsync(scriptPath, """
import Foundation
import PDFKit

guard CommandLine.arguments.count > 1 else { fputs("Missing PDF path\n", stderr); exit(1) }
let pdfPath = CommandLine.arguments[1]
guard let document = PDFDocument(url: URL(fileURLWithPath: pdfPath)) else {
    fputs("Unable to open PDF\n", stderr); exit(1)
}
var pages: [String] = []
for i in 0..<document.pageCount {
    if let t = document.page(at: i)?.string?.trimmingCharacters(in: .whitespacesAndNewlines), !t.isEmpty {
        pages.append(t)
    }
}
print(pages.joined(separator: "\n\n"))
""", Encoding.UTF8);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo("/usr/bin/swift")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false
                }
            };
            process.StartInfo.ArgumentList.Add(scriptPath);
            process.StartInfo.ArgumentList.Add(pdfPath);
            process.StartInfo.Environment["CLANG_MODULE_CACHE_PATH"] = cacheDir;

            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask  = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var error = await errorTask;
            if (process.ExitCode != 0)
            {
                Console.WriteLine($"[WARN] PDFKit extraction failed: {error}");
                return null;
            }
            return CleanupExtractedText(await outputTask);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] PDFKit extraction unavailable: {ex.Message}");
            return null;
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
            catch { /* ignore cleanup failures */ }
        }
    }

    private static string ExtractTextFromPdfContent(byte[] pageBytes)
    {
        // PDF content streams are 7-bit ASCII for operators; Latin-1 covers all byte values
        var raw = Encoding.Latin1.GetString(pageBytes);
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var builder = new StringBuilder();
        foreach (Match match in PdfTextTokenRegex.Matches(raw))
        {
            if (match.Groups["lineOp"].Success) { builder.AppendLine(); continue; }

            var token = match.Groups["token"].Value.Trim();
            if (token.Length == 0) continue;

            if (token[0] == '(')       builder.Append(UnescapePdfLiteral(token[1..^1]));
            else if (token[0] == '<')  builder.Append(DecodeHexString(token[1..^1]));

            var op = match.Groups["op"].Value;
            if (op is "'" or "\"" || op == "TJ") builder.AppendLine();
            else                                  builder.Append(' ');
        }
        return builder.ToString();
    }

    private static string UnescapePdfLiteral(string literal)
    {
        var result = new StringBuilder(literal.Length);
        for (var i = 0; i < literal.Length; i++)
        {
            var c = literal[i];
            if (c == '\\' && i + 1 < literal.Length)
            {
                var next = literal[++i];
                switch (next)
                {
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case '\\': case '(': case ')': result.Append(next); break;
                    default:
                        if (next >= '0' && next <= '7')
                        {
                            var octal = new StringBuilder().Append(next);
                            var count = 0;
                            while (count < 2 && i + 1 < literal.Length
                                   && literal[i + 1] >= '0' && literal[i + 1] <= '7')
                            { octal.Append(literal[++i]); count++; }
                            result.Append((char)Convert.ToInt32(octal.ToString(), 8));
                        }
                        else result.Append(next);
                        break;
                }
            }
            else result.Append(c);
        }
        return result.ToString();
    }

    private static string DecodeHexString(string rawHex)
    {
        var cleaned = Regex.Replace(rawHex, @"\s+", "");
        if (cleaned.Length % 2 == 1) cleaned += "0";

        var bytes = new byte[cleaned.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = byte.Parse(cleaned.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        return Encoding.Latin1.GetString(bytes);
    }

    private static string CleanupExtractedText(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var s = input.Replace('\u0000', ' ').Replace('\u00A0', ' ');
        s = Regex.Replace(s, @"\r\n|\r", "\n");
        s = Regex.Replace(s, @"[ \t]+", " ");
        s = Regex.Replace(s, @"\s*\n\s*", "\n");
        s = Regex.Replace(s, @"\n{3,}", "\n\n");
        s = Regex.Replace(s, @"([A-Za-z])\s{2,}([A-Za-z])", "$1 $2");
        return s.Trim();
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}

public class ResumeParseResult
{
    public bool    Success        { get; set; }
    public string  Text           { get; set; } = "";
    public string? ErrorMessage   { get; set; }
    public int     WordCount      { get; set; }
    public int     CharacterCount { get; set; }
    public string  FileName       { get; set; } = "";
    public string  FileType       { get; set; } = "";
}
