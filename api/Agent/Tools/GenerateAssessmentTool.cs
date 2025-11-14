using System.Text.Json;

namespace CareerCoach.Agent.Tools;

/// <summary>
/// Tool for generating skills assessments for specific roles and industries
/// </summary>
public class GenerateAssessmentTool : AgentTool
{
    private readonly GradientClient _llm;

    public GenerateAssessmentTool(GradientClient llm)
    {
        _llm = llm;
    }

    public override string Name => "generate_assessment";

    public override string Description =>
        "Generates a customized skills assessment for a specific industry and role. " +
        "Returns multiple-choice and coding questions to evaluate candidate proficiency.";

    public override object ParameterSchema => new
    {
        type = "object",
        properties = new
        {
            industry = new
            {
                type = "string",
                description = "Industry to assess for (e.g., 'software', 'data science', 'finance')"
            },
            role = new
            {
                type = "string",
                description = "Specific role to assess (e.g., 'Full Stack Developer', 'Data Analyst')"
            },
            difficulty = new
            {
                type = "string",
                description = "Difficulty level: 'entry', 'mid', or 'senior'",
                @enum = new[] { "entry", "mid", "senior" }
            },
            num_questions = new
            {
                type = "integer",
                description = "Number of questions to generate (default: 10)"
            }
        },
        required = new[] { "industry", "role" }
    };

    public override async Task<string> ExecuteAsync(string parameters)
    {
        var parsed = JsonDocument.Parse(parameters);
        var root = parsed.RootElement;

        var industry = root.GetProperty("industry").GetString() ?? "technology";
        var role = root.GetProperty("role").GetString() ?? "Software Developer";
        var difficulty = root.TryGetProperty("difficulty", out var diffProp)
            ? diffProp.GetString() ?? "mid"
            : "mid";
        var numQuestions = root.TryGetProperty("num_questions", out var numProp)
            ? numProp.GetInt32()
            : 10;

        var systemPrompt = $@"You are an assessment expert creating {difficulty}-level technical assessments.
Generate {numQuestions} questions for a {role} position in the {industry} industry.

Include a mix of:
- Multiple choice questions (60%)
- Short answer questions (30%)
- Coding/practical questions (10%)

Return response as JSON with this structure:
{{
  ""assessment_id"": ""unique-id"",
  ""title"": ""{role} Skills Assessment"",
  ""difficulty"": ""{difficulty}"",
  ""estimated_time_minutes"": 45,
  ""questions"": [
    {{
      ""id"": 1,
      ""type"": ""multiple_choice"",
      ""question"": ""What is..?"",
      ""options"": [""A"", ""B"", ""C"", ""D""],
      ""correct_answer"": ""A"",
      ""points"": 10
    }}
  ]
}}";

        var userPrompt = $"Generate assessment for {role} in {industry}";

        try
        {
            var response = await _llm.ChatAsync(systemPrompt, userPrompt, maxTokens: 2000);
            return response;
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"Failed to generate assessment: {ex.Message}"
            });
        }
    }
}
