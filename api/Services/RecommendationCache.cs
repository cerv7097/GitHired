using System.Text.Json;

namespace CareerCoach.Services;

public static class RecommendationCache
{
    public const int MaxAgeDays = 14;

    public static string NormalizeLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return "";
        var trimmed = location.Trim();
        return trimmed.Equals("remote", StringComparison.OrdinalIgnoreCase)
            ? "remote"
            : trimmed.ToLowerInvariant();
    }

    public static bool TryGetFreshDashboardPayload(
        string? cachedJson,
        string normalizedLocation,
        DateTime profileUpdatedAt,
        out string payloadJson)
    {
        payloadJson = "";
        if (string.IsNullOrWhiteSpace(cachedJson)) return false;

        try
        {
            using var doc = JsonDocument.Parse(cachedJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("cache", out var cache)) return false;

            var cachedLocation = GetString(cache, "location");
            if (!string.Equals(cachedLocation, normalizedLocation, StringComparison.OrdinalIgnoreCase))
                return false;

            var generatedRaw = GetString(cache, "generatedAt");
            if (!DateTime.TryParse(generatedRaw, out var generatedAt))
                return false;

            generatedAt = generatedAt.ToUniversalTime();
            var profileUpdatedUtc = profileUpdatedAt.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(profileUpdatedAt, DateTimeKind.Utc)
                : profileUpdatedAt.ToUniversalTime();

            // A resume/profile update invalidates cached recommendations even if
            // the 14-day TTL has not elapsed, because the ranking inputs changed.
            if (generatedAt < profileUpdatedUtc)
                return false;

            if (DateTime.UtcNow - generatedAt > TimeSpan.FromDays(MaxAgeDays))
                return false;

            if (!root.TryGetProperty("jobs", out var jobs) || jobs.ValueKind != JsonValueKind.Array)
                return false;

            payloadJson = cachedJson;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string ToToolPayload(string dashboardPayloadJson)
    {
        using var doc = JsonDocument.Parse(dashboardPayloadJson);
        var root = doc.RootElement;
        var matchedOn = root.TryGetProperty("matchedOn", out var matched) ? matched : default;

        var roles = matchedOn.ValueKind == JsonValueKind.Object && matchedOn.TryGetProperty("roles", out var rolesEl)
            ? rolesEl.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString() ?? "")
                .Where(v => v.Length > 0)
                .ToArray()
            : Array.Empty<string>();

        var skills = matchedOn.ValueKind == JsonValueKind.Object && matchedOn.TryGetProperty("skills", out var skillsEl)
            ? skillsEl.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString() ?? "")
                .Where(v => v.Length > 0)
                .ToArray()
            : Array.Empty<string>();

        var jobs = root.TryGetProperty("jobs", out var jobsEl) && jobsEl.ValueKind == JsonValueKind.Array
            ? jobsEl.EnumerateArray().Select(job => new
            {
                title = GetString(job, "title"),
                company = GetString(job, "company"),
                location = GetString(job, "location"),
                is_remote = GetBool(job, "isRemote"),
                employment_type = GetString(job, "employmentType"),
                seniority_level = GetString(job, "seniorityLevel", "unclear"),
                seniority_fit = GetString(job, "seniorityFit"),
                salary = BuildSalary(job),
                description_snippet = GetString(job, "descriptionSnippet"),
                apply_link = GetString(job, "applyLink"),
                posted_at = GetString(job, "postedAt")
            }).ToArray()
            : Array.Empty<object>();

        return JsonSerializer.Serialize(new
        {
            found = true,
            cached = true,
            matched_on = new
            {
                role = roles.FirstOrDefault() ?? "software engineer",
                skills,
                experience_level = GetString(matchedOn, "experienceLevel", "entry"),
                experience_level_uncertain = GetBool(matchedOn, "experienceLevelUncertain"),
                experience_level_reason = GetString(matchedOn, "experienceLevelReason")
            },
            total_results = root.TryGetProperty("totalResults", out var total) && total.TryGetInt32(out var count)
                ? count
                : jobs.Length,
            jobs
        });
    }

    private static string BuildSalary(JsonElement job)
    {
        var min = GetString(job, "minSalary");
        var max = GetString(job, "maxSalary");
        if (string.IsNullOrWhiteSpace(min) || string.IsNullOrWhiteSpace(max))
            return "Not specified";

        var currency = GetString(job, "salaryCurrency");
        var period = GetString(job, "salaryPeriod");
        return $"{currency} {min}-{max}/{period}".Trim();
    }

    private static string GetString(JsonElement element, string property, string fallback = "") =>
        element.ValueKind != JsonValueKind.Undefined &&
        element.ValueKind != JsonValueKind.Null &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static bool GetBool(JsonElement element, string property) =>
        element.ValueKind != JsonValueKind.Undefined &&
        element.ValueKind != JsonValueKind.Null &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.True;
}
