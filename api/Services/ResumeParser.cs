using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml;
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
                ".doc"  => await ParseLegacyDocAsync(fileStream, fileName),
                _       => throw new NotSupportedException($"File type '{extension}' is not supported. Please upload PDF, DOCX, or DOC.")
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
        var candidates = new List<(string Source, string Text)>();

        stream.Position = 0;
        if (OperatingSystem.IsMacOS())
        {
            var nativeText = await TryExtractPdfTextWithPdfKitAsync(stream);
            if (!string.IsNullOrWhiteSpace(nativeText))
                candidates.Add(("pdfkit", nativeText));
        }

        stream.Position = 0;
        var text = new StringBuilder();
        using (var document = UglyToad.PdfPig.PdfDocument.Open(stream))
        {
            foreach (var page in document.GetPages())
            {
                var pageText = page.Text;
                if (!string.IsNullOrWhiteSpace(pageText))
                    text.AppendLine(pageText);
            }
        }

        var extracted = CleanupExtractedText(text.ToString());
        if (!string.IsNullOrWhiteSpace(extracted))
            candidates.Add(("pdfpig", extracted));

        stream.Position = 0;
        var fallbackText = new StringBuilder();
        using (var fallbackReader = new PdfReader(stream))
        {
            for (var page = 1; page <= fallbackReader.NumberOfPages; page++)
            {
                var pageBytes = fallbackReader.GetPageContent(page);
                if (pageBytes != null)
                    fallbackText.AppendLine(ExtractTextFromPdfContent(pageBytes));
            }
        }

        var fallback = CleanupExtractedText(fallbackText.ToString());
        if (!string.IsNullOrWhiteSpace(fallback))
            candidates.Add(("itext-fallback", fallback));

        if (candidates.Count == 0)
            return string.Empty;

        foreach (var candidate in candidates)
        {
            Console.WriteLine($"[PDF_PARSE] source={candidate.Source} words={CountWords(candidate.Text)} chars={candidate.Text.Length}");
        }

        var selected = SelectBestPdfExtraction(candidates);
        Console.WriteLine($"[PDF_PARSE] selected={selected.Source} words={CountWords(selected.Text)} chars={selected.Text.Length}");
        return selected.Text;
    }

    private Task<string> ParseDocxAsync(Stream stream)
    {
        return Task.Run(() =>
        {
            stream.Position = 0;
            var text = new StringBuilder();
            using var doc = WordprocessingDocument.Open(stream, false);
            var body = doc.MainDocumentPart?.Document?.Body
                ?? throw new InvalidOperationException("Document body is empty or invalid");

            AppendOpenXmlText(body, text);

            if (doc.MainDocumentPart != null)
            {
                foreach (var header in doc.MainDocumentPart.HeaderParts)
                    AppendOpenXmlText(header.Header, text);
                foreach (var footer in doc.MainDocumentPart.FooterParts)
                    AppendOpenXmlText(footer.Footer, text);
                if (doc.MainDocumentPart.FootnotesPart?.Footnotes != null)
                    AppendOpenXmlText(doc.MainDocumentPart.FootnotesPart.Footnotes, text);
                if (doc.MainDocumentPart.EndnotesPart?.Endnotes != null)
                    AppendOpenXmlText(doc.MainDocumentPart.EndnotesPart.Endnotes, text);
            }

            return CleanupExtractedText(text.ToString());
        });
    }

    private async Task<string> ParseLegacyDocAsync(Stream stream, string fileName)
    {
        var converted = await TryConvertLegacyDocWithTextUtilAsync(stream, fileName);
        if (!string.IsNullOrWhiteSpace(converted))
            return converted;

        stream.Position = 0;
        converted = await TryConvertLegacyDocWithLibreOfficeAsync(stream, fileName);
        if (!string.IsNullOrWhiteSpace(converted))
            return converted;

        stream.Position = 0;
        var fallback = ExtractLikelyTextFromLegacyDoc(stream);
        if (!string.IsNullOrWhiteSpace(fallback) && CountWords(fallback) >= 20)
            return fallback;

        throw new NotSupportedException("Could not extract text from this legacy .doc file. Please save it as .docx and upload again.");
    }

    private static void AppendOpenXmlText(OpenXmlElement root, StringBuilder text)
    {
        foreach (var table in root.Descendants<Table>())
        {
            foreach (var row in table.Elements<TableRow>())
            {
                var cellTexts = row.Elements<TableCell>()
                    .Select(cell => string.Join(" ",
                        cell.Descendants<Paragraph>().Select(ParagraphText).Where(t => t.Length > 0)).Trim())
                    .Where(t => t.Length > 0);
                var rowText = string.Join("  ", cellTexts).Trim();
                if (rowText.Length > 0)
                    text.AppendLine(rowText);
            }
        }

        foreach (var paragraph in root.Descendants<Paragraph>().Where(p => !p.Ancestors<Table>().Any()))
        {
            var line = ParagraphText(paragraph);
            if (line.Length > 0)
                text.AppendLine(line);
        }
    }

    private static string ParagraphText(Paragraph paragraph)
    {
        var text = new StringBuilder();
        foreach (var child in paragraph.Descendants())
        {
            switch (child)
            {
                case Text t:
                    text.Append(t.Text);
                    break;
                case TabChar:
                    text.Append(' ');
                    break;
                case Break:
                    text.AppendLine();
                    break;
            }
        }
        return Regex.Replace(text.ToString(), @"[ \t]+", " ").Trim();
    }

    private async Task<string?> TryConvertLegacyDocWithTextUtilAsync(Stream stream, string fileName)
    {
        if (!OperatingSystem.IsMacOS())
            return null;

        var textUtilPath = "/usr/bin/textutil";
        if (!File.Exists(textUtilPath))
            return null;

        var tempDir = Path.Combine(Path.GetTempPath(), "career-coach-doc", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var docPath = Path.Combine(tempDir, Path.ChangeExtension(Path.GetFileName(fileName), ".doc"));
        var txtPath = Path.ChangeExtension(docPath, ".txt");
        try
        {
            stream.Position = 0;
            await using (var fs = File.Create(docPath))
                await stream.CopyToAsync(fs);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo(textUtilPath)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            process.StartInfo.ArgumentList.Add("-convert");
            process.StartInfo.ArgumentList.Add("txt");
            process.StartInfo.ArgumentList.Add("-output");
            process.StartInfo.ArgumentList.Add(txtPath);
            process.StartInfo.ArgumentList.Add(docPath);

            process.Start();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0 || !File.Exists(txtPath))
            {
                Console.WriteLine($"[WARN] textutil .doc extraction failed: {await errorTask}");
                return null;
            }

            return CleanupExtractedText(await File.ReadAllTextAsync(txtPath));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] textutil .doc extraction unavailable: {ex.Message}");
            return null;
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
            catch { /* ignore cleanup failures */ }
        }
    }

    private async Task<string?> TryConvertLegacyDocWithLibreOfficeAsync(Stream stream, string fileName)
    {
        var soffice = FindExecutable("soffice") ?? FindExecutable("libreoffice");
        if (soffice == null)
            return null;

        var tempDir = Path.Combine(Path.GetTempPath(), "career-coach-doc", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var docPath = Path.Combine(tempDir, Path.ChangeExtension(Path.GetFileName(fileName), ".doc"));
        var txtPath = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(docPath) + ".txt");
        try
        {
            stream.Position = 0;
            await using (var fs = File.Create(docPath))
                await stream.CopyToAsync(fs);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo(soffice)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            process.StartInfo.ArgumentList.Add("--headless");
            process.StartInfo.ArgumentList.Add("--convert-to");
            process.StartInfo.ArgumentList.Add("txt:Text");
            process.StartInfo.ArgumentList.Add("--outdir");
            process.StartInfo.ArgumentList.Add(tempDir);
            process.StartInfo.ArgumentList.Add(docPath);

            process.Start();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0 || !File.Exists(txtPath))
            {
                Console.WriteLine($"[WARN] LibreOffice .doc extraction failed: {await errorTask}");
                return null;
            }

            return CleanupExtractedText(await File.ReadAllTextAsync(txtPath));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] LibreOffice .doc extraction unavailable: {ex.Message}");
            return null;
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
            catch { /* ignore cleanup failures */ }
        }
    }

    private static string? FindExecutable(string name)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        foreach (var dir in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static string ExtractLikelyTextFromLegacyDoc(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();
        var strings = new List<string>();

        strings.AddRange(ExtractAsciiRuns(bytes));
        strings.AddRange(ExtractUtf16Runs(bytes));

        return CleanupExtractedText(string.Join("\n", strings));
    }

    private static IEnumerable<string> ExtractAsciiRuns(byte[] bytes)
    {
        var current = new StringBuilder();
        foreach (var b in bytes)
        {
            if (b is >= 32 and <= 126)
            {
                current.Append((char)b);
            }
            else
            {
                if (current.Length >= 4)
                    yield return current.ToString();
                current.Clear();
            }
        }
        if (current.Length >= 4)
            yield return current.ToString();
    }

    private static IEnumerable<string> ExtractUtf16Runs(byte[] bytes)
    {
        var current = new StringBuilder();
        for (var i = 0; i + 1 < bytes.Length; i += 2)
        {
            var value = BitConverter.ToUInt16(bytes, i);
            if (value is >= 32 and <= 126)
            {
                current.Append((char)value);
            }
            else
            {
                if (current.Length >= 4)
                    yield return current.ToString();
                current.Clear();
            }
        }
        if (current.Length >= 4)
            yield return current.ToString();
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
        s = CollapseLetterSpacedWords(s);
        s = RemoveConsecutiveDuplicateLines(s);
        return s.Trim();
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return Regex.Matches(text, @"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*").Count;
    }

    private static (string Source, string Text) SelectBestPdfExtraction(IEnumerable<(string Source, string Text)> candidates)
    {
        (string Source, string Text)? best = null;
        double bestScore = double.MinValue;

        foreach (var candidate in candidates.Where(c => !string.IsNullOrWhiteSpace(c.Text)))
        {
            var score = ScorePdfExtraction(candidate.Text);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best ?? ("none", string.Empty);
    }

    private static double ScorePdfExtraction(string text)
    {
        var words = CountWords(text);
        if (words == 0) return double.MinValue;

        var nonEmptyLines = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToArray();
        if (nonEmptyLines.Length == 0) return words;

        var normalizedLines = nonEmptyLines
            .Select(l => Regex.Replace(l.ToLowerInvariant(), @"\s+", " "))
            .ToArray();

        var uniqueLineRatio = normalizedLines.Distinct(StringComparer.Ordinal).Count() / (double)normalizedLines.Length;
        var spacedWordArtifacts = Regex.Matches(text, @"(?<![\p{L}\p{N}])(?:[\p{L}\p{N}]\s+){3,}[\p{L}\p{N}](?![\p{L}\p{N}])").Count;
        var spacedArtifactPenalty = Math.Min(0.3, spacedWordArtifacts / (double)Math.Max(1, words));

        // Favor high-content extraction but heavily penalize duplicate-line artifacts.
        return words * uniqueLineRatio * uniqueLineRatio * (1.0 - spacedArtifactPenalty);
    }

    private static string CollapseLetterSpacedWords(string input)
    {
        // Some PDF extractors emit words as single letters separated by spaces: "E x p e r i e n c e".
        // Rejoin these runs to avoid inflated counts.
        return Regex.Replace(
            input,
            @"(?<![\p{L}\p{N}])(?:[\p{L}\p{N}]\s+){2,}[\p{L}\p{N}](?![\p{L}\p{N}])",
            m => Regex.Replace(m.Value, @"\s+", ""));
    }

    private static string RemoveConsecutiveDuplicateLines(string input)
    {
        var lines = input.Split('\n');
        if (lines.Length <= 1) return input;

        var output = new List<string>(lines.Length);
        string? previous = null;
        foreach (var line in lines)
        {
            var normalized = line.Trim();
            if (normalized.Length == 0)
            {
                output.Add(line);
                previous = null;
                continue;
            }

            if (previous != null && string.Equals(previous, normalized, StringComparison.OrdinalIgnoreCase))
                continue;

            output.Add(line);
            previous = normalized;
        }

        return string.Join('\n', output);
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
