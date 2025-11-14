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
        var targetRole = parsed.RootElement.TryGetProperty("target_role", out var roleProp)
            ? roleProp.GetString() ?? ""
            : "";
        var targetIndustry = parsed.RootElement.TryGetProperty("target_industry", out var indProp)
            ? indProp.GetString() ?? ""
            : "";

        var systemPrompt = $@"You are a professional resume coach specializing in making candidates job-ready and marketable.
Your goal is to provide SPECIFIC, ACTIONABLE suggestions that will significantly improve their chances of landing interviews.

IMPROVEMENT FOCUS AREAS:

1. **Achievement Quantification**
   - Convert duties into measurable achievements
   - Add numbers, percentages, dollar amounts
   - Show impact and results, not just responsibilities
   - Example: ""Managed team"" → ""Led team of 8 developers, delivering 12 projects ahead of schedule with 95% client satisfaction""

2. **Keyword Optimization**
   - Identify missing industry-standard keywords
   - Suggest role-specific technical terms
   - Add relevant tools, technologies, methodologies
   - Ensure keywords match job postings

3. **Content Enhancement**
   - Strengthen weak bullet points
   - Remove vague statements
   - Add context and scope to achievements
   - Highlight leadership and initiative

4. **Formatting & Structure**
   - Improve section organization
   - Suggest better ordering of information
   - Recommend removing irrelevant content
   - Ensure consistency

5. **Marketability Boosters**
   - Add missing certifications or courses
   - Suggest portfolio projects to build
   - Recommend skills to highlight
   - Identify unique selling points

{(string.IsNullOrEmpty(targetRole) ? "" : $"Target Role: {targetRole} - Tailor suggestions to this role.")}
{(string.IsNullOrEmpty(targetIndustry) ? "" : $"Target Industry: {targetIndustry} - Focus on industry-specific improvements.")}

For EACH suggestion:
- Identify the CURRENT text/section
- Explain WHY it needs improvement
- Provide SPECIFIC rewrite with exact wording
- Show BEFORE and AFTER examples

Return response as detailed JSON:
{{
  ""overall_assessment"": ""High-level summary of resume strengths and weaknesses"",
  ""marketability_score"": 65,
  ""improvements"": [
    {{
      ""category"": ""Achievement Quantification"",
      ""priority"": ""high"",
      ""current_text"": ""Developed web applications"",
      ""issue"": ""Too vague, no measurable impact"",
      ""improved_text"": ""Developed 5 high-traffic web applications serving 50K+ daily users, improving page load time by 60% and increasing user engagement by 35%"",
      ""impact"": ""Shows scale, technical achievement, and business value"",
      ""keywords_added"": [""high-traffic"", ""performance optimization"", ""user engagement""]
    }}
  ],
  ""missing_keywords"": [
    {{""keyword"": ""agile"", ""relevance"": ""Essential for most tech roles"", ""where_to_add"": ""Skills section or project descriptions""}},
    {{""keyword"": ""CI/CD"", ""relevance"": ""Important for DevOps/development roles"", ""where_to_add"": ""Technical Skills section""}}
  ],
  ""sections_to_add"": [
    {{""section"": ""Technical Skills"", ""reason"": ""ATS and recruiters look for explicit skills list"", ""template"": ""TECHNICAL SKILLS\n• Languages: Python, Java, JavaScript\n• Frameworks: React, Node.js, Django""}}
  ],
  ""sections_to_remove"": [""Hobbies""],
  ""quick_wins"": [
    ""Add LinkedIn URL to contact info"",
    ""Change 'Responsibilities' header to 'Professional Experience'"",
    ""Move Education section below Experience""
  ],
  ""long_term_suggestions"": [
    ""Consider getting AWS certification to boost cloud credibility"",
    ""Build a portfolio website showcasing your projects"",
    ""Contribute to open-source projects to demonstrate collaboration""
  ],
  ""target_job_alignment"": ""This resume is 60% aligned with {targetRole} positions. Adding cloud technologies, leadership examples, and system design experience would increase alignment to 85%.""
}}";

        var userPrompt = $"Resume to improve:\n\n{resumeText}";

        try
        {
            var response = await _llm.ChatAsync(systemPrompt, userPrompt, maxTokens: 2500);
            return response;
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
}
