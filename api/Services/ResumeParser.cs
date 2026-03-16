using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using iTextSharp.text.pdf;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CareerCoach.Services;

/// <summary>
/// Service for parsing resume files (PDF, DOCX) and extracting text content
/// </summary>
public class ResumeParser
{
    private static readonly Regex EmailRegex = new(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PhoneRegex = new(@"(?<!\d)(\+?\d{1,2}[\s.-]?)?(\(?\d{3}\)?[\s.-]?){2}\d{4}(?!\d)", RegexOptions.Compiled);
    private static readonly Regex LinkedInRegex = new(@"linkedin\.com/[a-z0-9\-/]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PortfolioRegex = new(@"(github|behance|dribbble|medium|portfolio|bitbucket)\.com/[^\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BulletRegex = new(@"(^|\n)\s*([\-*•●‣▪◦])\s+\S+", RegexOptions.Compiled);
    private static readonly Regex MetricsRegex = new(@"\b\d{1,3}(?:[,.\s]\d{3})*(?:\s?(?:%|percent|pts|k|m|\+|x|\$|years?))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PdfTextTokenRegex = new(@"\((?<literal>(?:\\.|[^\\)])*)\)|<(?<hex>[0-9A-Fa-f\s]+)>", RegexOptions.Compiled);
    private static readonly string[] ActionVerbs =
    {
        "achieved", "built", "created", "delivered", "designed", "developed", "drove", "implemented",
        "improved", "increased", "launched", "led", "managed", "optimized", "owned", "shipped"
    };

    /// <summary>
    /// Parse a resume file and extract text based on file extension
    /// </summary>
    public async Task<ResumeParseResult> ParseResumeAsync(Stream fileStream, string fileName)
    {
        var extension = System.IO.Path.GetExtension(fileName).ToLowerInvariant();

        try
        {
            string text = extension switch
            {
                ".pdf" => await ParsePdfAsync(fileStream),
                ".docx" => await ParseDocxAsync(fileStream),
                ".doc" => throw new NotSupportedException("Legacy .doc format is not supported. Please convert to .docx"),
                _ => throw new NotSupportedException($"File type {extension} is not supported. Please upload PDF or DOCX.")
            };

            // Analyze the extracted text
            var analysis = AnalyzeResumeStructure(text);
            var wordCount = CountWords(text);
            var atsScore = CalculateAtsScore(wordCount, analysis);

            return new ResumeParseResult
            {
                Success = true,
                Text = text,
                WordCount = wordCount,
                CharacterCount = text.Length,
                HasContactInfo = analysis.HasContactInfo,
                HasSections = analysis.HasSections,
                DetectedSections = analysis.DetectedSections,
                AtsScore = atsScore,
                FileName = fileName,
                FileType = extension
            };
        }
        catch (Exception ex)
        {
            return new ResumeParseResult
            {
                Success = false,
                ErrorMessage = $"Failed to parse resume: {ex.Message}",
                FileName = fileName,
                FileType = extension
            };
        }
    }

    /// <summary>
    /// Extract text from PDF file using built-in text extraction (handles contact info reliably)
    /// </summary>
    private Task<string> ParsePdfAsync(Stream stream)
    {
        return Task.Run(() =>
        {
            stream.Position = 0;
            var text = new StringBuilder();

            using var reader = new PdfReader(stream);
            for (var page = 1; page <= reader.NumberOfPages; page++)
            {
                var pageBytes = reader.GetPageContent(page);
                if (pageBytes == null) continue;
                text.AppendLine(ExtractTextFromPdfContent(pageBytes));
            }

            return CleanupExtractedText(text.ToString());
        });
    }

    /// <summary>
    /// Extract text from DOCX file
    /// </summary>
    private Task<string> ParseDocxAsync(Stream stream)
    {
        return Task.Run(() =>
        {
            var text = new StringBuilder();

            using var doc = WordprocessingDocument.Open(stream, false);
            var body = doc.MainDocumentPart?.Document?.Body;

            if (body == null)
            {
                throw new InvalidOperationException("Document body is empty or invalid");
            }

            foreach (var paragraph in body.Descendants<Paragraph>())
            {
                text.AppendLine(paragraph.InnerText);
            }

            return CleanupExtractedText(text.ToString());
        });
    }

    /// <summary>
    /// Analyze resume structure to detect sections and content
    /// </summary>
    private ResumeStructureAnalysis AnalyzeResumeStructure(string text)
    {
        var normalizedText = CleanupExtractedText(text);
        var lowerText = normalizedText.ToLowerInvariant();
        var sections = new List<string>();

        // Common resume sections
        var sectionKeywords = new Dictionary<string, string[]>
        {
            ["Experience"] = new[] { "experience", "work history", "employment", "professional experience" },
            ["Education"] = new[] { "education", "academic", "degree", "university", "college" },
            ["Skills"] = new[] { "skills", "technical skills", "competencies", "expertise" },
            ["Summary"] = new[] { "summary", "objective", "profile", "about" },
            ["Projects"] = new[] { "projects", "portfolio" },
            ["Certifications"] = new[] { "certifications", "certificates", "licenses" },
            ["Awards"] = new[] { "awards", "honors", "achievements" }
        };

        foreach (var section in sectionKeywords)
        {
            if (section.Value.Any(keyword => lowerText.Contains(keyword)))
            {
                sections.Add(section.Key);
            }
        }

        // Check for contact information
        var hasEmail = EmailRegex.IsMatch(normalizedText);
        var hasPhone = PhoneRegex.IsMatch(normalizedText);
        var hasLinkedIn = LinkedInRegex.IsMatch(lowerText);
        var hasPortfolio = PortfolioRegex.IsMatch(lowerText);

        var bulletCount = BulletRegex.Matches(normalizedText).Count;
        var metricsCount = MetricsRegex.Matches(normalizedText).Count;
        var keywordHits = ActionVerbs.Count(verb => lowerText.Contains(verb));

        return new ResumeStructureAnalysis
        {
            HasEmail = hasEmail,
            HasPhone = hasPhone,
            HasLinkedIn = hasLinkedIn,
            HasPortfolio = hasPortfolio,
            HasContactInfo = hasEmail || hasPhone || hasLinkedIn || hasPortfolio,
            HasSections = sections.Count > 0,
            DetectedSections = sections,
            BulletCount = bulletCount,
            MetricCount = metricsCount,
            KeywordHits = keywordHits
        };
    }

    private static string CleanupExtractedText(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var sanitized = input.Replace('\u0000', ' ');
        sanitized = Regex.Replace(sanitized, @"[ \t]+", " ");
        sanitized = Regex.Replace(sanitized, @"\r\n|\r", "\n");
        sanitized = Regex.Replace(sanitized, @"\n{3,}", "\n\n");
        return sanitized.Trim();
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static string ExtractTextFromPdfContent(byte[] pageBytes)
    {
        var raw = Encoding.UTF8.GetString(pageBytes);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (Match match in PdfTextTokenRegex.Matches(raw))
        {
            if (match.Groups["literal"].Success)
            {
                builder.Append(UnescapePdfLiteral(match.Groups["literal"].Value));
            }
            else if (match.Groups["hex"].Success)
            {
                builder.Append(DecodeHexString(match.Groups["hex"].Value));
            }

            builder.Append(' ');
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
                    case '\\':
                    case '(':
                    case ')':
                        result.Append(next);
                        break;
                    default:
                        if (next >= '0' && next <= '7')
                        {
                            var octal = new StringBuilder().Append(next);
                            var octalCount = 0;
                            while (octalCount < 2 && i + 1 < literal.Length && literal[i + 1] >= '0' && literal[i + 1] <= '7')
                            {
                                octal.Append(literal[++i]);
                                octalCount++;
                            }

                            result.Append((char)Convert.ToInt32(octal.ToString(), 8));
                        }
                        else
                        {
                            result.Append(next);
                        }
                        break;
                }
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    private static string DecodeHexString(string rawHex)
    {
        var cleaned = Regex.Replace(rawHex, @"\s+", "");
        if (cleaned.Length % 2 == 1)
        {
            cleaned += "0";
        }

        var bytes = new byte[cleaned.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            var hexByte = cleaned.Substring(i * 2, 2);
            bytes[i] = byte.Parse(hexByte, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private int CalculateAtsScore(int wordCount, ResumeStructureAnalysis analysis)
    {
        double formatScore = wordCount switch
        {
            < 250 => 4,
            < 350 => 10,
            <= 700 => 20,
            <= 1100 => 15,
            _ => 10
        };

        double contactScore = 0;
        if (analysis.HasEmail) contactScore += 8;
        if (analysis.HasPhone) contactScore += 6;
        if (analysis.HasLinkedIn) contactScore += 4;
        if (analysis.HasPortfolio) contactScore += 2;
        contactScore = Math.Min(contactScore, 20);

        var sectionScore = Math.Min(analysis.DetectedSections.Count, 5) / 5d * 20;
        var bulletScore = Math.Min(analysis.BulletCount, 25) / 25d * 15;
        var metricsScore = Math.Min(analysis.MetricCount, 12) / 12d * 15;
        var keywordScore = Math.Min(analysis.KeywordHits, ActionVerbs.Length) / ActionVerbs.Length * 10;

        var rawScore = formatScore + contactScore + sectionScore + bulletScore + metricsScore + keywordScore;
        return (int)Math.Round(Math.Max(0, Math.Min(100, rawScore)));
    }
}

/// <summary>
/// Result of parsing a resume file
/// </summary>
public class ResumeParseResult
{
    public bool Success { get; set; }
    public string Text { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public int WordCount { get; set; }
    public int CharacterCount { get; set; }
    public bool HasContactInfo { get; set; }
    public bool HasSections { get; set; }
    public List<string> DetectedSections { get; set; } = new();
    public string FileName { get; set; } = "";
    public string FileType { get; set; } = "";
    public int AtsScore { get; set; }
}

/// <summary>
/// Internal analysis of resume structure
/// </summary>
internal class ResumeStructureAnalysis
{
    public bool HasContactInfo { get; set; }
    public bool HasSections { get; set; }
    public List<string> DetectedSections { get; set; } = new();
    public bool HasEmail { get; set; }
    public bool HasPhone { get; set; }
    public bool HasLinkedIn { get; set; }
    public bool HasPortfolio { get; set; }
    public int BulletCount { get; set; }
    public int MetricCount { get; set; }
    public int KeywordHits { get; set; }
}
