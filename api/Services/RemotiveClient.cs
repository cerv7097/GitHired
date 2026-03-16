using System.Text.Json;

namespace CareerCoach.Services;

public class RemotiveClient
{
    private readonly IHttpClientFactory _factory;

    public RemotiveClient(IHttpClientFactory factory)
    {
        _factory = factory;
    }

    public async Task<JobSearchResult> SearchAsync(string query)
    {
        var qs = $"search={Uri.EscapeDataString(query)}&limit=10";

        var http = _factory.CreateClient();
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync($"https://remotive.com/api/remote-jobs?{qs}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Remotive] Request failed: {ex.Message}");
            return new JobSearchResult(0, Array.Empty<JobResult>());
        }

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[Remotive] Non-success status: {response.StatusCode}");
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
            Console.WriteLine($"[Remotive] Read error: {ex.Message}");
            return new JobSearchResult(0, Array.Empty<JobResult>());
        }
    }

    private static JobSearchResult ParseResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("jobs", out var jobsEl))
                return new JobSearchResult(0, Array.Empty<JobResult>());

            var jobs = new List<JobResult>();
            foreach (var item in jobsEl.EnumerateArray())
            {
                string Get(string key) =>
                    item.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String
                        ? el.GetString() ?? "" : "";

                var title = Get("title");
                var applyLink = Get("url");
                if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(applyLink))
                    continue;

                var desc = System.Text.RegularExpressions.Regex.Replace(
                    Get("description"), "<[^>]+>", " ").Trim();
                var snippet = desc.Length > 220 ? desc[..220].TrimEnd() + "…" : desc;

                var postedAt = "";
                if (item.TryGetProperty("publication_date", out var pubEl) &&
                    pubEl.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(pubEl.GetString(), out var dt))
                    postedAt = dt.ToString("MMM d, yyyy");

                jobs.Add(new JobResult(
                    Title: title,
                    Company: Get("company_name"),
                    LogoUrl: Get("company_logo") is { Length: > 0 } logo ? logo : null,
                    Location: "Remote",
                    IsRemote: true,
                    EmploymentType: Get("job_type"),
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
            Console.WriteLine($"[Remotive] Parse error: {ex.Message}");
            return new JobSearchResult(0, Array.Empty<JobResult>());
        }
    }
}
