using System.Text.Json;

namespace CareerCoach.Services;

public record JobResult(
    string Title,
    string Company,
    string? LogoUrl,
    string Location,
    bool IsRemote,
    string EmploymentType,
    string? MinSalary,
    string? MaxSalary,
    string? SalaryCurrency,
    string? SalaryPeriod,
    string DescriptionSnippet,
    string ApplyLink,
    string PostedAt);

public record JobSearchResult(int TotalResults, IReadOnlyList<JobResult> Jobs);

public class JSearchClient
{
    private readonly IHttpClientFactory _factory;

    public JSearchClient(IHttpClientFactory factory)
    {
        _factory = factory;
    }

    public async Task<JobSearchResult> SearchAsync(
        string query,
        int page = 1,
        bool remoteOnly = false,
        string? employmentType = null)
    {
        var apiKey = Environment.GetEnvironmentVariable("JSEARCH_API_KEY") ?? "";
        if (string.IsNullOrEmpty(apiKey))
            return new JobSearchResult(0, Array.Empty<JobResult>());

        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-RapidAPI-Key", apiKey);
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-RapidAPI-Host", "jsearch.p.rapidapi.com");

        var qs = $"query={Uri.EscapeDataString(query)}&page={page}&num_pages=1";
        if (remoteOnly) qs += "&remote_jobs_only=true";
        if (!string.IsNullOrEmpty(employmentType))
            qs += $"&employment_types={Uri.EscapeDataString(employmentType)}";

        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync($"https://jsearch.p.rapidapi.com/search?{qs}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[JSearch] Request failed: {ex.Message}");
            return new JobSearchResult(0, Array.Empty<JobResult>());
        }

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[JSearch] Non-success status: {response.StatusCode}");
            return new JobSearchResult(0, Array.Empty<JobResult>());
        }

        var json = await response.Content.ReadAsStringAsync();
        return ParseResponse(json);
    }

    private static JobSearchResult ParseResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("data", out var data))
                return new JobSearchResult(0, Array.Empty<JobResult>());

            var jobs = new List<JobResult>();
            foreach (var item in data.EnumerateArray())
            {
                string Get(string key) =>
                    item.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String
                        ? el.GetString() ?? "" : "";

                bool GetBool(string key) =>
                    item.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.True;

                var isRemote = GetBool("job_is_remote");
                var city = Get("job_city");
                var state = Get("job_state");
                var country = Get("job_country");
                var location = isRemote
                    ? "Remote"
                    : string.Join(", ", new[] { city, state, country }.Where(s => !string.IsNullOrEmpty(s)));

                var desc = Get("job_description");
                var snippet = desc.Length > 220 ? desc[..220].TrimEnd() + "…" : desc;

                string? minSalary = null, maxSalary = null, salaryCurrency = null, salaryPeriod = null;
                if (item.TryGetProperty("job_min_salary", out var minEl) && minEl.ValueKind == JsonValueKind.Number)
                    minSalary = minEl.GetDecimal().ToString("N0");
                if (item.TryGetProperty("job_max_salary", out var maxEl) && maxEl.ValueKind == JsonValueKind.Number)
                    maxSalary = maxEl.GetDecimal().ToString("N0");
                if (item.TryGetProperty("job_salary_currency", out var currEl) && currEl.ValueKind == JsonValueKind.String)
                    salaryCurrency = currEl.GetString();
                if (item.TryGetProperty("job_salary_period", out var periodEl) && periodEl.ValueKind == JsonValueKind.String)
                    salaryPeriod = periodEl.GetString();

                var postedAt = "";
                if (item.TryGetProperty("job_posted_at_datetime_utc", out var postedEl) && postedEl.ValueKind == JsonValueKind.String)
                    if (DateTime.TryParse(postedEl.GetString(), out var dt))
                        postedAt = dt.ToString("MMM d, yyyy");

                var logoUrl = Get("employer_logo");
                jobs.Add(new JobResult(
                    Title: Get("job_title"),
                    Company: Get("employer_name"),
                    LogoUrl: logoUrl.Length > 0 ? logoUrl : null,
                    Location: location,
                    IsRemote: isRemote,
                    EmploymentType: Get("job_employment_type"),
                    MinSalary: minSalary,
                    MaxSalary: maxSalary,
                    SalaryCurrency: salaryCurrency,
                    SalaryPeriod: salaryPeriod,
                    DescriptionSnippet: snippet,
                    ApplyLink: Get("job_apply_link"),
                    PostedAt: postedAt
                ));
            }

            return new JobSearchResult(jobs.Count, jobs);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[JSearch] Parse error: {ex.Message}");
            return new JobSearchResult(0, Array.Empty<JobResult>());
        }
    }
}
