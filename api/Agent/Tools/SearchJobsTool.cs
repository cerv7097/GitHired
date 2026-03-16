using System.Text.Json;
using CareerCoach.Services;

namespace CareerCoach.Agent.Tools;

/// <summary>
/// Tool for searching jobs across multiple APIs (JSearch, Adzuna, The Muse, Remotive)
/// and returning deduplicated results.
/// </summary>
public class SearchJobsTool : AgentTool
{
    private readonly JobAggregatorService _aggregator;

    public SearchJobsTool(JobAggregatorService aggregator)
    {
        _aggregator = aggregator;
    }

    public override string Name => "search_jobs";

    public override string Description =>
        "Searches for real job listings based on skills, experience level, location, and industry. " +
        "Returns live job results with titles, companies, locations, and apply links.";

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
                description = "Target industry or role type (e.g., 'software engineer', 'data analyst')"
            },
            location = new
            {
                type = "string",
                description = "Preferred location or 'remote'"
            }
        },
        required = new[] { "skills", "experience_level" }
    };

    public override async Task<string> ExecuteAsync(string parameters)
    {
        var parsed = JsonDocument.Parse(parameters);
        var root = parsed.RootElement;

        var skills = new List<string>();
        if (root.TryGetProperty("skills", out var skillsProp))
        {
            if (skillsProp.ValueKind == JsonValueKind.Array)
                foreach (var skill in skillsProp.EnumerateArray())
                    skills.Add(skill.GetString() ?? "");
            else if (skillsProp.ValueKind == JsonValueKind.String)
                skills = (skillsProp.GetString() ?? "")
                    .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
        }

        var experienceLevel = root.TryGetProperty("experience_level", out var expProp)
            ? expProp.GetString() ?? "mid" : "mid";

        var industry = root.TryGetProperty("industry", out var indProp)
            ? indProp.GetString() ?? "software engineer" : "software engineer";

        var location = root.TryGetProperty("location", out var locProp)
            ? locProp.GetString() ?? "" : "";

        var levelLabel = experienceLevel switch
        {
            "entry" => "junior",
            "senior" => "senior",
            "lead" => "lead",
            _ => ""
        };

        var queryParts = new List<string>();
        if (!string.IsNullOrEmpty(levelLabel)) queryParts.Add(levelLabel);
        queryParts.Add(industry);
        if (skills.Count > 0) queryParts.Add(string.Join(" ", skills.Take(2)));
        var query = string.Join(" ", queryParts);

        var isRemote = location.Equals("remote", StringComparison.OrdinalIgnoreCase);
        if (!isRemote && !string.IsNullOrEmpty(location))
            query += $" in {location}";

        var result = await _aggregator.SearchAllAsync(
            query,
            location: isRemote ? null : location,
            remoteOnly: isRemote,
            experienceLevel: experienceLevel);

        return JsonSerializer.Serialize(new
        {
            total_results = result.TotalResults,
            jobs = result.Jobs.Select(j => new
            {
                title = j.Title,
                company = j.Company,
                location = j.Location,
                is_remote = j.IsRemote,
                employment_type = j.EmploymentType,
                salary = j.MinSalary != null && j.MaxSalary != null
                    ? $"{j.SalaryCurrency} {j.MinSalary}–{j.MaxSalary}/{j.SalaryPeriod}"
                    : "Not specified",
                description_snippet = j.DescriptionSnippet,
                apply_link = j.ApplyLink,
                posted_at = j.PostedAt
            })
        });
    }
}
