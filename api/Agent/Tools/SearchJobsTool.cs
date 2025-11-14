using System.Text.Json;

namespace CareerCoach.Agent.Tools;

/// <summary>
/// Tool for searching and recommending jobs based on user profile
/// </summary>
public class SearchJobsTool : AgentTool
{
    public override string Name => "search_jobs";

    public override string Description =>
        "Searches for job recommendations based on skills, experience level, location, and industry. " +
        "Returns a list of matching job titles and descriptions.";

    public override object ParameterSchema => new
    {
        type = "object",
        properties = new
        {
            skills = new
            {
                type = "array",
                items = new { type = "string" },
                description = "List of skills to match (e.g., ['C#', 'React', 'SQL'])"
            },
            experience_level = new
            {
                type = "string",
                description = "Experience level: 'entry', 'mid', 'senior', or 'lead'",
                @enum = new[] { "entry", "mid", "senior", "lead" }
            },
            industry = new
            {
                type = "string",
                description = "Target industry (e.g., 'software', 'healthcare', 'finance')"
            },
            location = new
            {
                type = "string",
                description = "Preferred location or 'remote'"
            }
        },
        required = new[] { "skills", "experience_level" }
    };

    public override Task<string> ExecuteAsync(string parameters)
    {
        var parsed = JsonDocument.Parse(parameters);
        var root = parsed.RootElement;

        var skills = new List<string>();
        if (root.TryGetProperty("skills", out var skillsProp))
        {
            // Handle both string and array formats
            if (skillsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var skill in skillsProp.EnumerateArray())
                {
                    skills.Add(skill.GetString() ?? "");
                }
            }
            else if (skillsProp.ValueKind == JsonValueKind.String)
            {
                // If it's a string, split by common delimiters
                var skillsStr = skillsProp.GetString() ?? "";
                skills = skillsStr.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }
        }

        var experienceLevel = root.TryGetProperty("experience_level", out var expProp)
            ? expProp.GetString() ?? "mid"
            : "mid";

        var industry = root.TryGetProperty("industry", out var indProp)
            ? indProp.GetString() ?? "technology"
            : "technology";

        var location = root.TryGetProperty("location", out var locProp)
            ? locProp.GetString() ?? "remote"
            : "remote";

        // Mock job recommendations (in production, this would call a real job API)
        var jobs = GenerateMockJobs(skills, experienceLevel, industry, location);

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            total_results = jobs.Count,
            jobs = jobs
        }));
    }

    private List<object> GenerateMockJobs(List<string> skills, string experienceLevel, string industry, string location)
    {
        // This is a simplified mock. In production, integrate with:
        // - LinkedIn Jobs API
        // - Indeed API
        // - GitHub Jobs
        // - Your own job database

        var jobs = new List<object>
        {
            new
            {
                title = $"{GetLevelPrefix(experienceLevel)} Software Engineer",
                company = "Tech Innovators Inc.",
                location = location,
                skills_match = skills.Take(3).ToList(),
                match_score = 92,
                salary_range = GetSalaryRange(experienceLevel),
                description = $"Looking for a {experienceLevel} developer with expertise in {string.Join(", ", skills.Take(2))}.",
                url = "https://example.com/job/1"
            },
            new
            {
                title = $"{GetLevelPrefix(experienceLevel)} Full Stack Developer",
                company = "Digital Solutions Corp",
                location = location,
                skills_match = skills.Take(4).ToList(),
                match_score = 88,
                salary_range = GetSalaryRange(experienceLevel),
                description = $"We need a talented {experienceLevel} developer proficient in {string.Join(", ", skills.Take(3))}.",
                url = "https://example.com/job/2"
            },
            new
            {
                title = $"{industry.ToUpper()} {GetLevelPrefix(experienceLevel)} Developer",
                company = "Industry Leaders LLC",
                location = location,
                skills_match = skills.Take(2).ToList(),
                match_score = 85,
                salary_range = GetSalaryRange(experienceLevel),
                description = $"Join our {industry} team as a {experienceLevel} developer.",
                url = "https://example.com/job/3"
            }
        };

        return jobs;
    }

    private string GetLevelPrefix(string level)
    {
        return level switch
        {
            "entry" => "Junior",
            "mid" => "Mid-Level",
            "senior" => "Senior",
            "lead" => "Lead",
            _ => "Mid-Level"
        };
    }

    private string GetSalaryRange(string level)
    {
        return level switch
        {
            "entry" => "$60k - $80k",
            "mid" => "$80k - $120k",
            "senior" => "$120k - $160k",
            "lead" => "$150k - $200k+",
            _ => "$80k - $120k"
        };
    }
}
