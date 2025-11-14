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
            }
        },
        required = new[] { "resume_text" }
    };

    public override async Task<string> ExecuteAsync(string parameters)
    {
        var parsed = JsonDocument.Parse(parameters);
        var resumeText = parsed.RootElement.GetProperty("resume_text").GetString() ?? "";

        var systemPrompt = @"You are an ATS (Applicant Tracking System) and resume optimization expert specializing in modern hiring practices.

ATS SCORING CRITERIA (score 0-100):
1. FORMATTING (20 points)
   - Simple, clean layout without tables/graphics/text boxes
   - Standard fonts (Arial, Calibri, Times New Roman)
   - No headers/footers containing critical info
   - Standard section headings

2. CONTACT INFORMATION (15 points)
   - Full name at top
   - Phone number
   - Professional email
   - Location (city, state)
   - LinkedIn URL

3. KEYWORDS & SKILLS (25 points)
   - Industry-specific keywords
   - Technical skills listed explicitly
   - Action verbs (led, managed, developed, implemented)
   - Measurable achievements with numbers

4. STRUCTURE & SECTIONS (20 points)
   - Clear section headers: EXPERIENCE, EDUCATION, SKILLS
   - Chronological work history
   - Consistent formatting
   - No unexplained gaps

5. CONTENT QUALITY (20 points)
   - Specific accomplishments, not just duties
   - Quantified results (increased by X%, reduced by Y)
   - Relevant to target role
   - No typos or grammatical errors

PROVIDE DETAILED ANALYSIS:
- Calculate exact score with breakdown by category
- List specific issues with severity (critical/moderate/minor)
- Provide actionable recommendations prioritized by impact
- Identify missing keywords for target roles
- Suggest specific improvements with examples

Return response as detailed JSON:
{
  ""overall_score"": 75,
  ""category_scores"": {
    ""formatting"": 18,
    ""contact_info"": 12,
    ""keywords_skills"": 20,
    ""structure"": 15,
    ""content_quality"": 10
  },
  ""issues"": [
    {""severity"": ""critical"", ""issue"": ""Missing Skills section"", ""impact"": ""ATS may not identify your technical capabilities""},
    {""severity"": ""moderate"", ""issue"": ""Duties listed instead of achievements"", ""impact"": ""Doesn't demonstrate value/impact""}
  ],
  ""recommendations"": [
    {""priority"": ""high"", ""recommendation"": ""Add a dedicated SKILLS section with technical and soft skills"", ""example"": ""SKILLS\n• Programming: Python, Java, C#\n• Tools: Git, Docker, AWS""},
    {""priority"": ""high"", ""recommendation"": ""Quantify achievements"", ""example"": ""Instead of 'Improved system performance', write 'Improved system performance by 40%, reducing load time from 5s to 3s'""}
  ],
  ""missing_keywords"": [""agile"", ""scrum"", ""CI/CD"", ""leadership""],
  ""ats_friendly"": true,
  ""pass_rate_estimate"": ""75%"",
  ""summary"": ""Resume is mostly ATS-compatible but needs stronger keywords and quantified achievements to stand out.""
}";

        var userPrompt = $"Resume to analyze:\n\n{resumeText}";

        try
        {
            var response = await _llm.ChatAsync(systemPrompt, userPrompt, maxTokens: 2000);
            return response;
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
}
