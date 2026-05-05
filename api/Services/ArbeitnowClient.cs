using System.Text.Json;
using System.Text.RegularExpressions;

namespace CareerCoach.Services;

/// <summary>
/// Free job board API at https://www.arbeitnow.com/api/job-board-api.
/// No key required. Heavy on tech roles, leans European but includes remote-globally
/// listings. The endpoint does not accept a search keyword — it returns a paginated
/// list of all current postings — so we filter client-side against the user's query.
///
/// Documented response shape (verified against Arbeitnow's public docs):
///   {
///     "data": [
///       { "slug": "...", "company_name": "...", "title": "...", "description": "...",
///         "remote": true, "url": "...", "tags": ["react"], "job_types": ["full-time"],
///         "location": "Berlin", "created_at": 1700000000 }
///     ],
///     "links": {...}, "meta": {...}
///   }
/// Every field access below is defensive — if the actual response differs from this
/// expectation we return 0 results rather than throwing.
/// </summary>
public class ArbeitnowClient
{
    private const string Endpoint = "https://www.arbeitnow.com/api/job-board-api";
    private const int MaxResults = 12; // cap output so one source can't dominate the merged list
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);
    // German gender notation suffixes common in European job postings (m/w/d, w/m/d, etc.)
    private static readonly Regex GenderNotationRegex = new(@"\(([mwfd]\/){1,3}[mwfd]\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IHttpClientFactory _factory;

    public ArbeitnowClient(IHttpClientFactory factory)
    {
        _factory = factory;
    }

    public async Task<JobSearchResult> SearchAsync(
        string query,
        bool remoteOnly = false,
        string? location = null)
    {
        var http = _factory.CreateClient();
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(Endpoint);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Arbeitnow] Request failed: {ex.Message}");
            return new JobSearchResult(0, Array.Empty<JobResult>());
        }

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[Arbeitnow] Non-success status: {response.StatusCode}");
            return new JobSearchResult(0, Array.Empty<JobResult>());
        }

        string json;
        try
        {
            json = await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Arbeitnow] Read error: {ex.Message}");
            return new JobSearchResult(0, Array.Empty<JobResult>());
        }

        return ParseAndFilter(json, query, remoteOnly, location);
    }

    private static JobSearchResult ParseAndFilter(string json, string query, bool remoteOnly, string? location)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Array)
            {
                Console.WriteLine("[Arbeitnow] Response had no `data` array — skipping.");
                return new JobSearchResult(0, Array.Empty<JobResult>());
            }

            // Tokenize the query into lowercase words ≥3 chars for client-side keyword
            // matching. We only need a coarse filter — the aggregator's tech filter and
            // dedup pass clean up afterward.
            var queryTokens = (query ?? "")
                .Split(new[] { ' ', ',', '/', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().ToLowerInvariant())
                .Where(t => t.Length >= 3)
                .ToArray();

            var scored = new List<(int Score, JobResult Job)>();

            foreach (var item in dataEl.EnumerateArray())
            {
                var job = TryParseJob(item);
                if (job == null) continue;

                // Arbeitnow skews heavily European — only surface remote listings
                if (!job.IsRemote) continue;

                if (!IsEnglish(job.Title, job.DescriptionSnippet)) continue;

                var score = ScoreMatch(job, queryTokens, location);
                // A score of 0 means no token overlap with title/tags/description — drop it
                // to keep the noise floor low. If the caller passed an empty query, all
                // jobs score 0 and we keep them.
                if (queryTokens.Length > 0 && score == 0) continue;

                scored.Add((score, job));
            }

            var top = scored
                .OrderByDescending(x => x.Score)
                .Select(x => x.Job)
                .Take(MaxResults)
                .ToList();

            return new JobSearchResult(top.Count, top);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Arbeitnow] Parse error: {ex.Message}");
            return new JobSearchResult(0, Array.Empty<JobResult>());
        }
    }

    private static JobResult? TryParseJob(JsonElement item)
    {
        string GetString(string key) =>
            item.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString() ?? "" : "";

        bool GetBool(string key) =>
            item.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.True;

        IReadOnlyList<string> GetStringArray(string key)
        {
            if (!item.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();
            return el.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString() ?? "")
                .Where(s => s.Length > 0)
                .ToArray();
        }

        var title = GetString("title");
        var url = GetString("url");
        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(url))
            return null;

        var company = GetString("company_name");
        var location = GetString("location");
        var isRemote = GetBool("remote");
        var description = GetString("description");
        var jobTypes = GetStringArray("job_types");
        var tags = GetStringArray("tags");

        // Strip HTML and trim to a snippet length consistent with other sources.
        var plain = HtmlTagRegex.Replace(description, " ").Trim();
        plain = Regex.Replace(plain, @"\s+", " ");
        var snippet = plain.Length > 220 ? plain[..220].TrimEnd() + "…" : plain;

        // created_at can be a unix integer or an ISO string depending on API version;
        // try both and skip if neither parses.
        var postedAt = "";
        if (item.TryGetProperty("created_at", out var createdEl))
        {
            if (createdEl.ValueKind == JsonValueKind.Number && createdEl.TryGetInt64(out var unix))
            {
                postedAt = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime.ToString("MMM d, yyyy");
            }
            else if (createdEl.ValueKind == JsonValueKind.String &&
                     DateTime.TryParse(createdEl.GetString(), out var parsed))
            {
                postedAt = parsed.ToString("MMM d, yyyy");
            }
        }

        var locationDisplay = string.IsNullOrWhiteSpace(location)
            ? (isRemote ? "Remote" : "")
            : location;

        return new JobResult(
            Title: title,
            Company: company,
            LogoUrl: null,
            Location: locationDisplay,
            IsRemote: isRemote,
            EmploymentType: jobTypes.FirstOrDefault() ?? "",
            MinSalary: null,
            MaxSalary: null,
            SalaryCurrency: null,
            SalaryPeriod: null,
            DescriptionSnippet: snippet,
            ApplyLink: url,
            PostedAt: postedAt
        );
    }

    private static int ScoreMatch(JobResult job, string[] queryTokens, string? location)
    {
        if (queryTokens.Length == 0) return 1;

        var title = (job.Title ?? "").ToLowerInvariant();
        var snippet = (job.DescriptionSnippet ?? "").ToLowerInvariant();

        var score = 0;
        foreach (var token in queryTokens)
        {
            // Title hits are worth more than snippet hits since title relevance
            // is a far stronger signal of role fit.
            if (title.Contains(token)) score += 3;
            else if (snippet.Contains(token)) score += 1;
        }

        // Light location boost (additive) so a Berlin user sees Berlin Arbeitnow jobs
        // surfaced first; not a hard filter, since the JobAggregatorService also boosts
        // by location and Arbeitnow's global remote roles can still be relevant.
        if (!string.IsNullOrWhiteSpace(location))
        {
            var locLower = location.ToLowerInvariant();
            if ((job.Location ?? "").ToLowerInvariant().Contains(locLower))
                score += 2;
        }

        return score;
    }

    private static bool IsEnglish(string? title, string? snippet)
    {
        var t = title ?? "";
        // German gender notation is a reliable non-English signal
        if (GenderNotationRegex.IsMatch(t)) return false;

        // If the title itself has non-ASCII characters it's almost certainly not English
        if (t.Any(c => c > 127)) return false;

        // Check the description snippet for a high ratio of non-ASCII characters.
        // A threshold of 3% catches French/German/Spanish text while ignoring the
        // occasional accented character in a company or product name.
        var text = snippet ?? "";
        if (text.Length > 20)
        {
            var nonAscii = text.Count(c => c > 127);
            if ((double)nonAscii / text.Length > 0.03) return false;
        }

        return true;
    }
}
