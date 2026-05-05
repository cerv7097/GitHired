using System.Text.Json;

namespace CareerCoach.Services;

public class AdzunaClient
{
    private readonly IHttpClientFactory _factory;

    public AdzunaClient(IHttpClientFactory factory)
    {
        _factory = factory;
    }

    public async Task<JobSearchResult> SearchAsync(
        string query,
        string? location = null,
        bool remoteOnly = false,
        int page = 1,
        int? radiusMiles = null)
    {
        var appId = Environment.GetEnvironmentVariable("ADZUNA_APP_ID") ?? "";
        var appKey = Environment.GetEnvironmentVariable("ADZUNA_APP_KEY") ?? "";
        if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(appKey))
            return new JobSearchResult(0, Array.Empty<JobResult>());

        // Adzuna bills per HTTP request, not per result, so requesting up to 25 results
        // per call is free in terms of quota. Bumping from 10 to 25 widens the pool we
        // can dedupe + filter against. Per their docs the cap is 50; 25 leaves room
        // without risking a quirky tier limit on some plans.
        var qs = $"app_id={Uri.EscapeDataString(appId)}&app_key={Uri.EscapeDataString(appKey)}" +
                 $"&results_per_page=25&what={Uri.EscapeDataString(query)}";

        if (!string.IsNullOrEmpty(location) && !remoteOnly)
        {
            qs += $"&where={Uri.EscapeDataString(location)}";

            // radiusMiles == null  -> 100 mi default (preserves prior behavior)
            // radiusMiles <= 0     -> "anywhere", omit distance so Adzuna returns nationwide results
            // radiusMiles > 0      -> convert miles to km
            var miles = radiusMiles ?? 100;
            if (miles > 0)
            {
                var km = (int)Math.Round(miles * 1.609);
                qs += $"&distance={km}";
            }
        }

        var http = _factory.CreateClient();
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync($"https://api.adzuna.com/v1/api/jobs/us/search/{page}?{qs}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Adzuna] Request failed: {ex.Message}");
            return new JobSearchResult(0, Array.Empty<JobResult>());
        }

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[Adzuna] Non-success status: {response.StatusCode}");
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
            Console.WriteLine($"[Adzuna] Read error: {ex.Message}");
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

                var title = Get("title");
                var applyLink = Get("redirect_url");
                if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(applyLink))
                    continue;

                var company = "";
                if (item.TryGetProperty("company", out var companyEl) &&
                    companyEl.TryGetProperty("display_name", out var companyName))
                    company = companyName.GetString() ?? "";

                var locationStr = "";
                if (item.TryGetProperty("location", out var locEl) &&
                    locEl.TryGetProperty("display_name", out var locName))
                    locationStr = locName.GetString() ?? "";

                var desc = Get("description");
                var snippet = desc.Length > 220 ? desc[..220].TrimEnd() + "…" : desc;

                var postedAt = "";
                if (item.TryGetProperty("created", out var createdEl) &&
                    createdEl.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(createdEl.GetString(), out var dt))
                    postedAt = dt.ToString("MMM d, yyyy");

                string? minSalary = null, maxSalary = null;
                if (item.TryGetProperty("salary_min", out var minEl) && minEl.ValueKind == JsonValueKind.Number)
                    minSalary = minEl.GetDecimal().ToString("N0");
                if (item.TryGetProperty("salary_max", out var maxEl) && maxEl.ValueKind == JsonValueKind.Number)
                    maxSalary = maxEl.GetDecimal().ToString("N0");

                jobs.Add(new JobResult(
                    Title: title,
                    Company: company,
                    LogoUrl: null,
                    Location: locationStr,
                    IsRemote: locationStr.Contains("remote", StringComparison.OrdinalIgnoreCase),
                    EmploymentType: Get("contract_type"),
                    MinSalary: minSalary,
                    MaxSalary: maxSalary,
                    SalaryCurrency: "USD",
                    SalaryPeriod: "year",
                    DescriptionSnippet: snippet,
                    ApplyLink: applyLink,
                    PostedAt: postedAt
                ));
            }

            return new JobSearchResult(jobs.Count, jobs);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Adzuna] Parse error: {ex.Message}");
            return new JobSearchResult(0, Array.Empty<JobResult>());
        }
    }
}
