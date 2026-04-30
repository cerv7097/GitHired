using System.Text.Json;
using CareerCoach.Services;

namespace CareerCoach.Agent.Tools;

/// <summary>
/// Tool that recommends personalized job listings based on the user's stored profile
/// (resume skills/roles, assessment scores, and search history).
/// </summary>
public class RecommendJobsTool : AgentTool
{
    private readonly Db _db;
    private readonly JobAggregatorService _aggregator;

    public RecommendJobsTool(Db db, JobAggregatorService aggregator)
    {
        _db = db;
        _aggregator = aggregator;
    }

    public override string Name => "recommend_jobs";

    public override string Description =>
        "Recommends personalized job listings based on the user's resume skills, roles, " +
        "assessment scores, and job search history. Use this when the user asks for job " +
        "recommendations, 'jobs for me', or 'what jobs match my profile'.";

    public override object ParameterSchema => new
    {
        type = "object",
        properties = new
        {
            user_id = new
            {
                type = "string",
                description = "The unique identifier of the user"
            },
            location = new
            {
                type = "string",
                description = "Optional location filter or 'remote'"
            }
        },
        required = new[] { "user_id" }
    };

    public override async Task<string> ExecuteAsync(string parameters)
    {
        var parsed = JsonDocument.Parse(parameters);
        var root = parsed.RootElement;
        var userId = root.GetProperty("user_id").GetString() ?? "";
        var location = root.TryGetProperty("location", out var locProp) ? locProp.GetString() : null;

        try
        {
            var profile = await _db.GetUserProfileAsync(userId);

            if (profile == null || (profile.Skills.Length == 0 && profile.Roles.Length == 0))
            {
                return JsonSerializer.Serialize(new
                {
                    found = false,
                    message = "No profile found. Please upload your resume first so I can make personalized recommendations."
                });
            }

            // Build query: top role + top skills weighted by assessment scores
            var topRole = profile.Roles.FirstOrDefault() ?? "software engineer";
            var topSkills = GetTopSkills(profile);

            var queryParts = new List<string> { topRole };
            if (topSkills.Any())
                queryParts.AddRange(topSkills.Take(2));

            var query = string.Join(" ", queryParts);
            if (!HasTechTitleKeyword(query))
            {
                var skillFallback = topSkills.FirstOrDefault(HasTechTitleKeyword);
                query = !string.IsNullOrWhiteSpace(skillFallback)
                    ? $"software engineer {skillFallback}"
                    : "software engineer";
            }

            var isRemote = location?.Equals("remote", StringComparison.OrdinalIgnoreCase) == true;
            var profileTerms = BuildProfileTerms(profile);
            var cacheLocation = RecommendationCache.NormalizeLocation(location);
            if (RecommendationCache.TryGetFreshDashboardPayload(
                profile.RecommendedJobs,
                cacheLocation,
                profile.UpdatedAt,
                out var cachedPayload))
            {
                return RecommendationCache.ToToolPayload(cachedPayload);
            }

            var seniority = SeniorityMatcher.InferUserLevel(profile);
            var normalizedExperienceLevel = SeniorityMatcher.ToLabel(seniority.Level);

            var result = await _aggregator.SearchAllAsync(
                query,
                location: isRemote ? null : location,
                remoteOnly: isRemote,
                experienceLevel: normalizedExperienceLevel,
                museCategory: "Technology"
            );

            var matchedJobs = result.Jobs
                .Select(job => new { Job = job, Seniority = SeniorityMatcher.AssessJob(job, seniority.Level) })
                .Where(match => match.Seniority.IsCompatible && IsRelevantTechJob(match.Job))
                .OrderByDescending(match => RecommendationScore(match.Job, match.Seniority, profileTerms))
                .ToList();

            var generatedAt = DateTime.UtcNow;
            var dashboardPayload = new
            {
                cache = new
                {
                    generatedAt = generatedAt.ToString("O"),
                    expiresAt = generatedAt.AddDays(RecommendationCache.MaxAgeDays).ToString("O"),
                    location = cacheLocation,
                    maxAgeDays = RecommendationCache.MaxAgeDays
                },
                matchedOn = new
                {
                    roles = profile.Roles.Take(3),
                    skills = topSkills,
                    experienceLevel = normalizedExperienceLevel,
                    experienceLevelUncertain = seniority.IsUncertain,
                    experienceLevelReason = seniority.Reason,
                    atsScore = profile.AtsScore,
                    searchQueries = new[] { query }
                },
                totalResults = matchedJobs.Count,
                jobs = matchedJobs
                    .Take(5)
                    .Select(match => new
                    {
                        title = match.Job.Title,
                        company = match.Job.Company,
                        logoUrl = match.Job.LogoUrl,
                        location = match.Job.Location,
                        isRemote = match.Job.IsRemote,
                        employmentType = match.Job.EmploymentType,
                        minSalary = match.Job.MinSalary,
                        maxSalary = match.Job.MaxSalary,
                        salaryCurrency = match.Job.SalaryCurrency,
                        salaryPeriod = match.Job.SalaryPeriod,
                        descriptionSnippet = match.Job.DescriptionSnippet,
                        applyLink = match.Job.ApplyLink,
                        postedAt = match.Job.PostedAt,
                        seniorityLevel = match.Seniority.Level is null ? "unclear" : SeniorityMatcher.ToLabel(match.Seniority.Level.Value),
                        seniorityFit = match.Seniority.Reason
                    })
                    .ToList()
            };

            var dashboardJson = JsonSerializer.Serialize(dashboardPayload);
            try
            {
                await _db.SaveRecommendedJobsAsync(userId, dashboardJson);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Could not cache recommendations for user {userId}: {ex.Message}");
            }

            return RecommendationCache.ToToolPayload(dashboardJson);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"Failed to fetch recommendations: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Returns skills ordered by assessment score (if available), falling back to resume order.
    /// </summary>
    private static List<string> GetTopSkills(UserProfileRecord profile)
    {
        try
        {
            using var doc = JsonDocument.Parse(profile.AssessmentScores);
            var scored = doc.RootElement.EnumerateObject()
                .OrderByDescending(p => p.Value.GetDouble())
                .Select(p => p.Name)
                .ToList();

            // Prefer scored skills, then fill from resume skills
            var result = scored.ToList();
            foreach (var s in profile.Skills)
                if (!result.Contains(s, StringComparer.OrdinalIgnoreCase))
                    result.Add(s);

            return result.Take(4).ToList();
        }
        catch
        {
            return profile.Skills.Take(4).ToList();
        }
    }

    private static readonly string[] TechTitleKeywords =
    {
        "engineer", "developer", "software", "data", "analyst", "architect", "devops", "sre",
        "cloud", "backend", "frontend", "fullstack", "full stack", "full-stack", "mobile",
        "machine learning", "ai ", " ai", "ml ", " ml", "security", "cyber", "network",
        "infrastructure", "platform", "systems", "technical", "tech", "qa ", " qa",
        "quality assurance", "database", "dba", "information technology", "application",
        "web ", "api ", "saas", "embedded", "firmware", "ux ", " ux", "ui ", " ui"
    };

    private static bool HasTechTitleKeyword(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        TechTitleKeywords.Any(kw => value.Contains(kw, StringComparison.OrdinalIgnoreCase));

    private static string[] BuildProfileTerms(UserProfileRecord profile) =>
        profile.Roles
            .Concat(profile.Skills)
            .Concat(profile.Tools)
            .SelectMany(v => v.Split(new[] { ' ', '/', '-', ',', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(v => v.Trim())
            .Where(v => v.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsRelevantTechJob(JobResult job) => HasTechTitleKeyword(job.Title);

    private static int RecommendationScore(
        JobResult job,
        JobSeniorityAssessment seniority,
        string[] profileTerms) =>
        seniority.CompatibilityScore +
        ProfileMatchScore(job, profileTerms) +
        EmploymentFitScore(job);

    private static int ProfileMatchScore(JobResult job, string[] profileTerms)
    {
        if (profileTerms.Length == 0) return 8;

        var matches = profileTerms.Count(term =>
            job.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (job.DescriptionSnippet ?? "").Contains(term, StringComparison.OrdinalIgnoreCase));

        return Math.Min(matches * 8, 32);
    }

    private static int EmploymentFitScore(JobResult job)
    {
        var combined = $"{job.Title} {job.EmploymentType}";
        if (combined.Contains("intern", StringComparison.OrdinalIgnoreCase)) return -12;
        if (combined.Contains("new grad", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("graduate", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("junior", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("associate", StringComparison.OrdinalIgnoreCase))
            return 10;
        if (combined.Contains("full", StringComparison.OrdinalIgnoreCase)) return 8;
        return 0;
    }
}
