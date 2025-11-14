using iTextSharp.text.pdf;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text;

namespace CareerCoach.Services;

/// <summary>
/// Service for parsing resume files (PDF, DOCX) and extracting text content
/// </summary>
public class ResumeParser
{
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

            return new ResumeParseResult
            {
                Success = true,
                Text = text,
                WordCount = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length,
                CharacterCount = text.Length,
                HasContactInfo = analysis.HasContactInfo,
                HasSections = analysis.HasSections,
                DetectedSections = analysis.DetectedSections,
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
    /// Extract text from PDF file using basic byte extraction
    /// </summary>
    private Task<string> ParsePdfAsync(Stream stream)
    {
        return Task.Run(() =>
        {
            var text = new StringBuilder();

            using var reader = new PdfReader(stream);
            for (int page = 1; page <= reader.NumberOfPages; page++)
            {
                // Get the page content stream bytes and convert to text
                var pageBytes = reader.GetPageContent(page);
                if (pageBytes != null)
                {
                    // This is a simplified extraction - in production consider using a better library
                    var pageText = System.Text.Encoding.UTF8.GetString(pageBytes);
                    // Remove PDF operators and clean the text
                    pageText = System.Text.RegularExpressions.Regex.Replace(pageText, @"\(([^)]+)\)", "$1");
                    pageText = System.Text.RegularExpressions.Regex.Replace(pageText, @"[^\w\s@.,()-]", " ");
                    text.AppendLine(pageText);
                }
            }

            return text.ToString();
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

            return text.ToString();
        });
    }

    /// <summary>
    /// Analyze resume structure to detect sections and content
    /// </summary>
    private ResumeStructureAnalysis AnalyzeResumeStructure(string text)
    {
        var lowerText = text.ToLowerInvariant();
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
        var hasEmail = lowerText.Contains("@") && lowerText.Contains(".");
        var hasPhone = System.Text.RegularExpressions.Regex.IsMatch(text, @"\d{3}[-.\s]?\d{3}[-.\s]?\d{4}");

        return new ResumeStructureAnalysis
        {
            HasContactInfo = hasEmail || hasPhone,
            HasSections = sections.Count > 0,
            DetectedSections = sections
        };
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
}

/// <summary>
/// Internal analysis of resume structure
/// </summary>
internal class ResumeStructureAnalysis
{
    public bool HasContactInfo { get; set; }
    public bool HasSections { get; set; }
    public List<string> DetectedSections { get; set; } = new();
}
