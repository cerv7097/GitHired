using System.Text.Json;

namespace CareerCoach.Agent.Tools;

/// <summary>
/// Tool for analyzing resume ATS (Applicant Tracking System) compatibility
/// </summary>
public class AnalyzeATSTool : AgentTool
{
    private readonly GradientClient _llm;

    public AnalyzeATSTool(GradientClient llm)
    {
        _llm = llm;
    }

    public override string Name => "analyze_ats_compatibility";

    public override string Description =>
        "Analyzes a resume for ATS (Applicant Tracking System) compatibility. " +
        "Returns a score (0-100) and specific recommendations for improvement.";

    public override object ParameterSchema => new
    {
        type = "object",
        properties = new
        {
            resume_text = new
            {
                type = "string",
                description = "The full text content of the resume to analyze"
            },
            parser_context = new
            {
                type = "string",
                description = "Optional parser diagnostics, detected sections, and extraction quality notes from the uploaded resume"
            }
        },
        required = new[] { "resume_text" }
    };

    public override async Task<string> ExecuteAsync(string parameters)
    {
        var parsed = JsonDocument.Parse(parameters);
        var resumeText = parsed.RootElement.GetProperty("resume_text").GetString() ?? "";
        var parserContext = parsed.RootElement.TryGetProperty("parser_context", out var parserProp)
            ? parserProp.GetString() ?? ""
            : "";

        var systemPrompt = @"You are an ATS (Applicant Tracking System) expert. Your job is to evaluate the ACTUAL resume text provided by the user.

CRITICAL RULES:
- Read the resume text carefully before writing anything.
- Only report issues that are GENUINELY ABSENT or GENUINELY WEAK in the provided resume.
- If a section already exists, do NOT say it is missing.
- If achievements are already quantified with numbers, do NOT say they are missing metrics.
- If a keyword already appears in the resume, do NOT list it as missing.
- Do not use generic template advice. Every issue and recommendation must be grounded in what you actually read.

SCORING CRITERIA (0-100):
1. FORMATTING (20 pts) — clean layout, standard headings, no tables/graphics trapping text
2. CONTACT INFORMATION (15 pts) — name, phone, email, location, LinkedIn/portfolio
3. KEYWORDS & SKILLS (25 pts) — industry keywords present, technical skills explicit, action verbs used
4. STRUCTURE & SECTIONS (20 pts) — clear headers, chronological history, no unexplained gaps
5. CONTENT QUALITY (20 pts) — achievements over duties, quantified results, relevant content

Return ONLY valid JSON (no markdown fences, no commentary outside the JSON):
{
  ""overall_score"": <0-100 integer>,
  ""category_scores"": {
    ""formatting"": <0-20>,
    ""contact_info"": <0-15>,
    ""keywords_skills"": <0-25>,
    ""structure"": <0-20>,
    ""content_quality"": <0-20>
  },
  ""issues"": [
    {""severity"": ""critical|moderate|minor"", ""issue"": ""<specific issue found in THIS resume>"", ""impact"": ""<why it matters>""}
  ],
  ""recommendations"": [
    {""priority"": ""high|medium|low"", ""recommendation"": ""<specific actionable change for THIS resume>"", ""example"": ""<concrete rewrite example based on actual content>""}
  ],
  ""missing_keywords"": [""<keyword genuinely absent from the resume>""],
  ""ats_friendly"": <true|false>,
  ""analysis_confidence"": ""high|medium|low"",
  ""pass_rate_estimate"": ""<X%>"",
  ""summary"": ""<honest 1-2 sentence summary based on what you actually read>""
}";

        var userPrompt = string.IsNullOrWhiteSpace(parserContext)
            ? $"Resume to analyze:\n\n{resumeText}"
            : $"Parser diagnostics:\n{parserContext}\n\nResume to analyze:\n\n{resumeText}";

        try
        {
            var response = await _llm.ChatAsync(systemPrompt, userPrompt, model: "openai-gpt-4o-mini", maxTokens: 4000);
            return ExtractAssistantJson(response);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"Failed to analyze ATS compatibility: {ex.Message}",
                score = 0
            });
        }
    }

    private static string ExtractAssistantJson(string rawResponse)
    {
        using var doc = JsonDocument.Parse(rawResponse);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";

        // Find the outermost JSON object, regardless of surrounding text or fences
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            content = content[start..(end + 1)];
        }

        try
        {
            using var parsed = JsonDocument.Parse(content);
            return JsonSerializer.Serialize(parsed.RootElement);
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[ERROR] analyze_ats JSON parse failed: {ex.Message}");
            Console.WriteLine($"[ERROR] Raw content ({content.Length} chars): {content[..Math.Min(500, content.Length)]}");
            throw;
        }
    }
}
