namespace CareerCoach.Services;

/// <summary>
/// Fans out job searches to multiple APIs in parallel and returns deduplicated results.
/// Priority order for deduplication: JSearch → Adzuna → The Muse → Arbeitnow → Remotive.
/// Results are sorted by relevance: query match in title/description, then location proximity,
/// then seniority compatibility with the user's experience level.
/// </summary>
public class JobAggregatorService
{
    private readonly JSearchClient _jSearch;
    private readonly AdzunaClient _adzuna;
    private readonly TheMuseClient _theMuse;
    private readonly RemotiveClient _remotive;
    private readonly ArbeitnowClient _arbeitnow;

    public JobAggregatorService(
        JSearchClient jSearch,
        AdzunaClient adzuna,
        TheMuseClient theMuse,
        RemotiveClient remotive,
        ArbeitnowClient arbeitnow)
    {
        _jSearch = jSearch;
        _adzuna = adzuna;
        _theMuse = theMuse;
        _remotive = remotive;
        _arbeitnow = arbeitnow;
    }

    public async Task<JobSearchResult> SearchAllAsync(
        string query,
        string? location = null,
        bool remoteOnly = false,
        string? experienceLevel = null,
        string? employmentType = null,
        string? museCategory = null,
        int? radiusMiles = null,
        SeniorityLevel? userSeniorityLevel = null,
        // When true, jobs whose Location string can't be reconciled with the user's
        // location are dropped (hard filter). When false, only the upstream APIs apply
        // their native radius — surviving jobs are then passed through to the relevance
        // scorer without being post-filtered. Recommendations pass false so passive
        // suggestions can include in-state-but-distant, remote, and out-of-state roles.
        bool hardLocationFilter = true)
    {
        // Build task list — Arbeitnow has both remote and on-site roles so we always include it.
        // Remotive is remote-only so it's only included when appropriate.
        var tasks = new List<Task<JobSearchResult>>
        {
            _jSearch.SearchAsync(query, remoteOnly: remoteOnly, employmentType: employmentType, location: location, radiusMiles: radiusMiles),
            _adzuna.SearchAsync(query, location, remoteOnly, radiusMiles: radiusMiles),
            _theMuse.SearchAsync(query, experienceLevel, museCategory),
            _arbeitnow.SearchAsync(query, remoteOnly: remoteOnly, location: location)
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

        // HARD location filter. Only applied when the caller explicitly asked for it
        // (the Jobs-tab search does, recommendations don't). The relevance scorer below
        // already gives location-matching jobs a higher score so out-of-state results
        // still rank lower without being eliminated. Skipped on remoteOnly searches and
        // when the user picks "Anywhere" (radius=0).
        //
        // ALSO skipped when the radius is > 100 miles — at that distance the user is
        // explicitly opting into nearby states (200mi from San Francisco reaches into
        // Nevada and Oregon), and a state-only filter would defeat the radius widening.
        // For tight radii (≤100mi), the filter keeps the obvious cross-country leaks
        // (Bangalore, Boston) out without harming the spread the user asked for.
        var withinTightRadius = !radiusMiles.HasValue || (radiusMiles.Value > 0 && radiusMiles.Value <= 100);
        if (hardLocationFilter && withinTightRadius && !string.IsNullOrWhiteSpace(location) && !remoteOnly)
        {
            var beforeCount = merged.Count;
            merged = merged
                .Where(j => LocationFilter.Matches(j, location, radiusMiles))
                .ToList();
            Console.WriteLine($"[JobAggregator] Location filter '{location}' kept {merged.Count}/{beforeCount} jobs");
        }
        else if (hardLocationFilter && !string.IsNullOrWhiteSpace(location) && !remoteOnly)
        {
            Console.WriteLine($"[JobAggregator] Skipping hard location filter for radius {radiusMiles}mi — relying on upstream radius constraint");
        }

        // Resolve the seniority level to score against — prefer explicit override, fall back
        // to parsing the experienceLevel string (e.g. "senior", "entry") if provided.
        var seniorityForScoring = userSeniorityLevel ?? SeniorityMatcher.Normalize(experienceLevel);

        // Score every job by relevance: query match + location proximity + seniority fit.
        // This ensures page 1 is always the most relevant and later pages progressively
        // less so, regardless of which source each job came from.
        var queryTokens = (query ?? "")
            .Split(new[] { ' ', ',', '/', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length >= 3)
            .ToArray();

        var scored = merged
            .Select(j => (Score: RelevanceScore(j, queryTokens, location, seniorityForScoring), Job: j))
            .OrderByDescending(x => x.Score)
            .Select(x => x.Job)
            .ToList();

        Console.WriteLine($"[JobAggregator] Merged {scored.Count} unique jobs from {results.Length} sources");
        return new JobSearchResult(scored.Count, scored);
    }

    /// <summary>
    /// Builds a deduplication key by normalizing title and company to lowercase alphanumeric only.
    /// e.g. "Senior .NET Developer" @ "Acme Corp" → "senior net developer|acme corp"
    /// </summary>
    // Normalize employment type strings so "FULLTIME", "full_time", "Full Time" all match "FULLTIME"
    private static int RelevanceScore(JobResult job, string[] queryTokens, string? location, SeniorityLevel? userLevel)
    {
        var score = 0;
        var title   = (job.Title              ?? "").ToLowerInvariant();
        var snippet = (job.DescriptionSnippet ?? "").ToLowerInvariant();

        // Query relevance — title hits outweigh description hits
        foreach (var token in queryTokens)
        {
            if (title.Contains(token))   score += 5;
            else if (snippet.Contains(token)) score += 2;
        }

        // Location relevance
        if (!string.IsNullOrWhiteSpace(location))
        {
            var locLower  = location.Trim().ToLowerInvariant();
            var jobLocLow = (job.Location ?? "").ToLowerInvariant();
            if (jobLocLow.Contains(locLower))
                score += 10;
            else
            {
                // Partial match on the state/region token (e.g. "tx" from "Austin, TX")
                var lastPart = locLower.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                if (lastPart is { Length: >= 2 } && jobLocLow.Contains(lastPart))
                    score += 4;
            }
        }

        // Remote listings get a small bonus — relevant in any location context
        if (job.IsRemote) score += 3;

        // Seniority fit — reuse the existing compatibility score (range −100 to +35).
        // Divide by 4 to keep it proportional to the other signals without overwhelming them.
        if (userLevel.HasValue)
        {
            var fit = SeniorityMatcher.AssessJob(job, userLevel.Value);
            score += fit.CompatibilityScore / 4;
        }

        return score;
    }

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
