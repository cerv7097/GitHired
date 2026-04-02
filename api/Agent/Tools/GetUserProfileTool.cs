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
        "Retrieves the user's profile information including skills, experience, resume analysis results, " +
        "assessment scores, and recent job search history. Use this to personalize recommendations.";

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

    public override async Task<string> ExecuteAsync(string parameters)
    {
        var parsed = JsonDocument.Parse(parameters);
        var userId = parsed.RootElement.GetProperty("user_id").GetString() ?? "";

        try
        {
            var profile = await _db.GetUserProfileAsync(userId);

            if (profile == null)
            {
                return JsonSerializer.Serialize(new
                {
                    user_id = userId,
                    found = false,
                    message = "No profile found. The user has not uploaded a resume yet."
                });
            }

            // Parse assessment scores and recent search history from JSONB strings
            object assessmentScores;
            try { assessmentScores = JsonDocument.Parse(profile.AssessmentScores).RootElement; }
            catch { assessmentScores = new { }; }

            var recentSearches = new List<string>();
            try
            {
                using var histDoc = JsonDocument.Parse(profile.SearchHistory);
                recentSearches = histDoc.RootElement.EnumerateArray()
                    .TakeLast(10)
                    .Select(e => e.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "")
                    .Where(q => !string.IsNullOrEmpty(q))
                    .ToList();
            }
            catch { /* leave empty */ }

            object educationData;
            try { educationData = JsonDocument.Parse(profile.Education).RootElement; }
            catch { educationData = new object[] { }; }

            return JsonSerializer.Serialize(new
            {
                user_id = userId,
                found = true,
                name = $"{profile.FirstName} {profile.LastName}",
                email = profile.Email,
                skills = profile.Skills,
                tools = profile.Tools,
                roles = profile.Roles,
                experience_level = profile.ExperienceLevel,
                summary = profile.Summary,
                education = educationData,
                ats_score = profile.AtsScore,
                assessment_scores = assessmentScores,
                recent_searches = recentSearches,
                profile_updated = profile.UpdatedAt.ToString("MMM d, yyyy"),
                resume_text = profile.ResumeText != null
                    ? (profile.ResumeText.Length > 3000
                        ? profile.ResumeText[..3000] + "\n[truncated]"
                        : profile.ResumeText)
                    : null
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"Failed to retrieve user profile: {ex.Message}"
            });
        }
    }
}
