using System.Text.Json;

namespace CareerCoach.Services;

public class TheMuseClient
{
    private readonly IHttpClientFactory _factory;

    public TheMuseClient(IHttpClientFactory factory)
    {
        _factory = factory;
    }

    public async Task<JobSearchResult> SearchAsync(string query, string? experienceLevel = null, string? category = null)
    {
        var apiKey = Environment.GetEnvironmentVariable("THE_MUSE_API_KEY") ?? "";

        var qs = $"descending=true&page=1&per_page=10&query={Uri.EscapeDataString(query)}";

        // Restrict to a category when provided (e.g. "Technology" keeps results tech-relevant)
        if (!string.IsNullOrEmpty(category))
            qs += $"&category={Uri.EscapeDataString(category)}";
        if (!string.IsNullOrEmpty(apiKey))
            qs += $"&api_key={Uri.EscapeDataString(apiKey)}";

        // Map experience level to Muse levels
        if (!string.IsNullOrEmpty(experienceLevel))
        {
            var museLevel = experienceLevel switch
            {
                "entry" => "Entry Level",
                "mid" => "Mid Level",
                "senior" => "Senior Level",
                "lead" => "Senior Level",
                _ => null
            };
            if (museLevel != null)
                qs += $"&level={Uri.EscapeDataString(museLevel)}";
        }

        var http = _factory.CreateClient();
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync($"https://www.themuse.com/api/public/jobs?{qs}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TheMuse] Request failed: {ex.Message}");
            return new JobSearchResult(0, Array.Empty<JobResult>());
        }

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[TheMuse] Non-success status: {response.StatusCode}");
            return new JobSearchResult(0, Array.Empty<JobResult>());
        }

        try
        {
            var bytes = await response.Content.ReadAsByteArrayAsync();
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            return ParseResponse(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TheMuse] Read error: {ex.Message}");
            return new JobSearchResult(0, Array.Empty<JobResult>());
        }
    }

    private static JobSearchResult ParseResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("results", out var results))
                return new JobSearchResult(0, Array.Empty<JobResult>());

            var jobs = new List<JobResult>();
            foreach (var item in results.EnumerateArray())
            {
                string Get(string key) =>
                    item.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String
                        ? el.GetString() ?? "" : "";

                var title = Get("name");
                var applyLink = Get("refs") is var _ &&
                    item.TryGetProperty("refs", out var refs) &&
                    refs.TryGetProperty("landing_page", out var lp) &&
                    lp.ValueKind == JsonValueKind.String
                    ? lp.GetString() ?? "" : Get("landing_page");

                if (string.IsNullOrEmpty(title)) continue;

                var company = "";
                if (item.TryGetProperty("company", out var compEl) &&
                    compEl.TryGetProperty("name", out var compName))
                    company = compName.GetString() ?? "";

                // Locations array
                var locationStr = "";
                if (item.TryGetProperty("locations", out var locs) &&
                    locs.ValueKind == JsonValueKind.Array)
                {
                    var locParts = locs.EnumerateArray()
                        .Where(l => l.TryGetProperty("name", out _))
                        .Select(l => l.GetProperty("name").GetString() ?? "")
                        .Where(s => !string.IsNullOrEmpty(s))
                        .Take(2)
                        .ToList();
                    locationStr = string.Join(" / ", locParts);
                }

                var isRemote = locationStr.Contains("remote", StringComparison.OrdinalIgnoreCase) ||
                               locationStr.Contains("anywhere", StringComparison.OrdinalIgnoreCase);

                // Contents (description)
                var desc = "";
                if (item.TryGetProperty("contents", out var contentsEl) &&
                    contentsEl.ValueKind == JsonValueKind.String)
                {
                    // Strip HTML tags for snippet
                    desc = System.Text.RegularExpressions.Regex.Replace(
                        contentsEl.GetString() ?? "", "<[^>]+>", " ").Trim();
                }
                var snippet = desc.Length > 220 ? desc[..220].TrimEnd() + "…" : desc;

                var postedAt = "";
                if (item.TryGetProperty("publication_date", out var pubEl) &&
                    pubEl.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(pubEl.GetString(), out var dt))
                    postedAt = dt.ToString("MMM d, yyyy");

                jobs.Add(new JobResult(
                    Title: title,
                    Company: company,
                    LogoUrl: null,
                    Location: locationStr,
                    IsRemote: isRemote,
                    EmploymentType: "FULLTIME",
                    MinSalary: null,
                    MaxSalary: null,
                    SalaryCurrency: null,
                    SalaryPeriod: null,
                    DescriptionSnippet: snippet,
                    ApplyLink: applyLink,
                    PostedAt: postedAt
                ));
            }

            return new JobSearchResult(jobs.Count, jobs);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TheMuse] Parse error: {ex.Message}");
            return new JobSearchResult(0, Array.Empty<JobResult>());
        }
    }
}
