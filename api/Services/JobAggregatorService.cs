namespace CareerCoach.Services;

/// <summary>
/// Fans out job searches to multiple APIs in parallel and returns deduplicated results.
/// Priority order for deduplication: JSearch → Adzuna → The Muse → Remotive.
/// </summary>
public class JobAggregatorService
{
    private readonly JSearchClient _jSearch;
    private readonly AdzunaClient _adzuna;
    private readonly TheMuseClient _theMuse;
    private readonly RemotiveClient _remotive;

    public JobAggregatorService(
        JSearchClient jSearch,
        AdzunaClient adzuna,
        TheMuseClient theMuse,
        RemotiveClient remotive)
    {
        _jSearch = jSearch;
        _adzuna = adzuna;
        _theMuse = theMuse;
        _remotive = remotive;
    }

    public async Task<JobSearchResult> SearchAllAsync(
        string query,
        string? location = null,
        bool remoteOnly = false,
        string? experienceLevel = null,
        string? employmentType = null)
    {
        // Build task list — Remotive is remote-only so only include it when appropriate
        var tasks = new List<Task<JobSearchResult>>
        {
            _jSearch.SearchAsync(query, remoteOnly: remoteOnly, employmentType: employmentType),
            _adzuna.SearchAsync(query, location, remoteOnly),
            _theMuse.SearchAsync(query, experienceLevel)
        };

        if (remoteOnly)
            tasks.Add(_remotive.SearchAsync(query));

        // Wrap each task so a single source failure never wipes the others
        var safe = tasks.Select(async t =>
        {
            try { return await t; }
            catch (Exception ex)
            {
                Console.WriteLine($"[JobAggregator] Source error: {ex.Message}");
                return new JobSearchResult(0, Array.Empty<JobResult>());
            }
        });

        var results = await Task.WhenAll(safe);

        // Merge and deduplicate — first source wins for a given title+company pair
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<JobResult>();

        foreach (var result in results)
        {
            foreach (var job in result.Jobs)
            {
                var key = MakeKey(job.Title, job.Company);
                if (seen.Add(key))
                    merged.Add(job);
            }
        }

        // Post-filter by employment type for sources that don't natively support it
        if (!string.IsNullOrEmpty(employmentType))
        {
            merged = merged.Where(j =>
                NormalizeType(j.EmploymentType).Contains(NormalizeType(employmentType),
                    StringComparison.OrdinalIgnoreCase)
            ).ToList();
        }

        // Post-filter remote-only for sources that may return mixed results
        if (remoteOnly)
            merged = merged.Where(j => j.IsRemote).ToList();

        Console.WriteLine($"[JobAggregator] Merged {merged.Count} unique jobs from {results.Length} sources");
        return new JobSearchResult(merged.Count, merged);
    }

    /// <summary>
    /// Builds a deduplication key by normalizing title and company to lowercase alphanumeric only.
    /// e.g. "Senior .NET Developer" @ "Acme Corp" → "senior net developer|acme corp"
    /// </summary>
    // Normalize employment type strings so "FULLTIME", "full_time", "Full Time" all match "FULLTIME"
    private static string NormalizeType(string s) =>
        new string(s.ToUpperInvariant().Where(char.IsLetter).ToArray());

    private static string MakeKey(string title, string company)
    {
        static string Normalize(string s) =>
            new string(s.ToLowerInvariant()
                .Where(c => char.IsLetterOrDigit(c) || c == ' ')
                .ToArray())
                .Trim();

        return $"{Normalize(title)}|{Normalize(company)}";
    }
}
