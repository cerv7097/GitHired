using System.Text.Json;

namespace CareerCoach.Agent.Tools;

/// <summary>
/// Tool for retrieving user profile information from the database
/// </summary>
public class GetUserProfileTool : AgentTool
{
    private readonly Db _db;

    public GetUserProfileTool(Db db)
    {
        _db = db;
    }

    public override string Name => "get_user_profile";

    public override string Description =>
        "Retrieves the user's profile information including skills, experience, education, " +
        "resume analysis results, and preferences. Use this to personalize recommendations.";

    public override object ParameterSchema => new
    {
        type = "object",
        properties = new
        {
            user_id = new
            {
                type = "string",
                description = "The unique identifier of the user"
            }
        },
        required = new[] { "user_id" }
    };

    public override Task<string> ExecuteAsync(string parameters)
    {
        var parsed = JsonDocument.Parse(parameters);
        var userId = parsed.RootElement.GetProperty("user_id").GetString() ?? "";

        try
        {
            // In a real implementation, this would query the database
            // For now, return a mock profile structure
            var profile = new
            {
                user_id = userId,
                name = "User Profile",
                email = "user@example.com",
                skills = new[] { "C#", "JavaScript", "SQL", "React" },
                experience_level = "mid",
                current_role = "Software Developer",
                years_of_experience = 3,
                education = new
                {
                    degree = "Bachelor of Science",
                    field = "Computer Science",
                    graduation_year = 2021
                },
                preferred_industries = new[] { "technology", "fintech" },
                preferred_location = "remote",
                resume_analyzed = true,
                ats_score = 85,
                last_assessment_score = 78,
                career_goals = "Advance to senior developer role within 2 years"
            };

            return Task.FromResult(JsonSerializer.Serialize(profile));
        }
        catch (Exception ex)
        {
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                error = $"Failed to retrieve user profile: {ex.Message}"
            }));
        }
    }
}
