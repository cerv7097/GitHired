using System.Text.Json;

namespace CareerCoach.Agent.Tools;

/// <summary>
/// Tool for generating personalized career path recommendations
/// </summary>
public class GetCareerPathTool : AgentTool
{
    private readonly GradientClient _llm;

    public GetCareerPathTool(GradientClient llm)
    {
        _llm = llm;
    }

    public override string Name => "get_career_path";

    public override string Description =>
        "Generates a personalized career path from current role to target role, " +
        "including skill gaps, recommended courses, timeline, and milestones.";

    public override object ParameterSchema => new
    {
        type = "object",
        properties = new
        {
            current_role = new
            {
                type = "string",
                description = "Current job title or role"
            },
            target_role = new
            {
                type = "string",
                description = "Desired future job title or role"
            },
            current_skills = new
            {
                type = "array",
                items = new { type = "string" },
                description = "List of current skills"
            },
            industry = new
            {
                type = "string",
                description = "Target industry"
            }
        },
        required = new[] { "current_role", "target_role", "current_skills" }
    };

    public override async Task<string> ExecuteAsync(string parameters)
    {
        var parsed = JsonDocument.Parse(parameters);
        var root = parsed.RootElement;

        var currentRole = root.GetProperty("current_role").GetString() ?? "";
        var targetRole = root.GetProperty("target_role").GetString() ?? "";

        var currentSkills = new List<string>();
        if (root.TryGetProperty("current_skills", out var skillsProp))
        {
            // Handle both string and array formats
            if (skillsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var skill in skillsProp.EnumerateArray())
                {
                    currentSkills.Add(skill.GetString() ?? "");
                }
            }
            else if (skillsProp.ValueKind == JsonValueKind.String)
            {
                // If it's a string, split by common delimiters
                var skillsStr = skillsProp.GetString() ?? "";
                currentSkills = skillsStr.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }
        }

        var industry = root.TryGetProperty("industry", out var indProp)
            ? indProp.GetString() ?? "technology"
            : "technology";

        var systemPrompt = @"You are a career advisor specializing in creating personalized career development paths.
Create a detailed career roadmap including:
1. Skill gaps to address
2. Recommended courses or certifications
3. Timeline with milestones (3-month, 6-month, 12-month)
4. Project ideas to build experience
5. Networking strategies

Return response as JSON with this structure:
{
  ""skill_gaps"": [""Cloud Architecture"", ""System Design""],
  ""recommended_learning"": [
    {""topic"": ""AWS Solutions Architect"", ""priority"": ""high"", ""duration"": ""3 months""}
  ],
  ""timeline"": {
    ""3_months"": [""Complete AWS certification""],
    ""6_months"": [""Build 2 cloud-based projects""],
    ""12_months"": [""Apply for senior positions""]
  },
  ""projects"": [""Build a scalable microservices app""],
  ""networking_tips"": [""Attend cloud computing meetups""]
}";

        var userPrompt = $@"Create career path:
Current Role: {currentRole}
Target Role: {targetRole}
Current Skills: {string.Join(", ", currentSkills)}
Industry: {industry}";

        try
        {
            var response = await _llm.ChatAsync(systemPrompt, userPrompt, maxTokens: 1500);
            return response;
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"Failed to generate career path: {ex.Message}"
            });
        }
    }
}
