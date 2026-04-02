using System.Text.Json;

namespace CareerCoach.Agent.Tools;

/// <summary>
/// Tool for providing detailed resume improvement suggestions
/// Focuses on making candidates job-ready and marketable
/// </summary>
public class ImproveResumeTool : AgentTool
{
    private readonly GradientClient _llm;

    public ImproveResumeTool(GradientClient llm)
    {
        _llm = llm;
    }

    public override string Name => "improve_resume";

    public override string Description =>
        "Provides specific, actionable suggestions to improve a resume and make the candidate more marketable. " +
        "Focuses on content enhancement, achievement quantification, keyword optimization, and formatting improvements.";

    public override object ParameterSchema => new
    {
        type = "object",
        properties = new
        {
            resume_text = new
            {
                type = "string",
                description = "The full text content of the resume to improve"
            },
            parser_context = new
            {
                type = "string",
                description = "Optional parser diagnostics, extraction quality notes, and detected sections from the uploaded resume"
            },
            target_role = new
            {
                type = "string",
                description = "Optional: Target job role to tailor suggestions (e.g., 'Senior Software Engineer', 'Data Analyst')"
            },
            target_industry = new
            {
                type = "string",
                description = "Optional: Target industry (e.g., 'technology', 'finance', 'healthcare')"
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
        var targetRole = parsed.RootElement.TryGetProperty("target_role", out var roleProp)
            ? roleProp.GetString() ?? ""
            : "";
        var targetIndustry = parsed.RootElement.TryGetProperty("target_industry", out var indProp)
            ? indProp.GetString() ?? ""
            : "";

        var systemPrompt = $@"You are a professional resume coach. Your job is to give SPECIFIC feedback on the ACTUAL resume text provided.

CRITICAL RULES:
- Read every line of the resume before writing suggestions.
- Only suggest adding something if it is genuinely absent from the resume.
- Only suggest improving a bullet if it is actually weak — do not suggest quantifying bullets that are already quantified.
- Do not suggest adding a section that already exists.
- Do not list a keyword as missing if it already appears in the resume.
- Every improvement must quote actual text from the resume and show a concrete rewrite.

{(string.IsNullOrEmpty(targetRole) ? "" : $"Target Role: {targetRole}")}
{(string.IsNullOrEmpty(targetIndustry) ? "" : $"Target Industry: {targetIndustry}")}

Return ONLY valid JSON (no markdown fences, no commentary outside the JSON):
{{
  ""overall_assessment"": ""<honest 2-3 sentence summary of this specific resume's strengths and actual gaps>"",
  ""marketability_score"": <0-100 integer>,
  ""improvements"": [
    {{
      ""category"": ""<Achievement Quantification|Keyword Optimization|Content Enhancement|Formatting|Marketability>"",
      ""priority"": ""high|medium|low"",
      ""current_text"": ""<exact quote from the resume>"",
      ""issue"": ""<specific reason this particular line needs work>"",
      ""improved_text"": ""<concrete rewrite>"",
      ""impact"": ""<why this change helps>"",
      ""keywords_added"": [""<keyword>""]
    }}
  ],
  ""missing_keywords"": [
    {{""keyword"": ""<keyword genuinely absent>"", ""relevance"": ""<why it matters>"", ""where_to_add"": ""<specific section>""}}
  ],
  ""sections_to_add"": [
    {{""section"": ""<section genuinely missing>"", ""reason"": ""<why it would help>"", ""template"": ""<example content>""}}
  ],
  ""sections_to_remove"": [""<section that hurts more than it helps>""],
  ""quick_wins"": [""<small specific change with high impact>""],
  ""long_term_suggestions"": [""<career development suggestions>""],
  ""target_job_alignment"": ""<alignment assessment if target role was provided, otherwise omit>""
}}";

        var userPrompt = new List<string>();
        if (!string.IsNullOrWhiteSpace(parserContext))
        {
            userPrompt.Add($"Parser diagnostics:\n{parserContext}");
        }

        userPrompt.Add($"Resume to improve:\n\n{resumeText}");

        try
        {
            var response = await _llm.ChatAsync(systemPrompt, string.Join("\n\n", userPrompt), model: "openai-gpt-4o-mini", maxTokens: 4000);
            return ExtractAssistantJson(response);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"Failed to generate improvement suggestions: {ex.Message}",
                marketability_score = 0
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
            Console.WriteLine($"[ERROR] improve_resume JSON parse failed: {ex.Message}");
            Console.WriteLine($"[ERROR] Raw content ({content.Length} chars): {content[..Math.Min(500, content.Length)]}");
            throw;
        }
    }
}
