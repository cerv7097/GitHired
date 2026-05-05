using System.Text.RegularExpressions;

namespace CareerCoach.Services;

/// <summary>
/// Decides whether a job listing's location string is a reasonable fit for what the
/// user typed. Used as a HARD filter (not a soft sort) so that searching "San Francisco"
/// can't return jobs in India or Massachusetts.
///
/// We try to be lenient enough that nearby cities in the same state still match
/// (Oakland, San Jose all pass for a "San Francisco, CA" search), but strict enough
/// to drop different-country and different-state listings.
/// </summary>
public static class LocationFilter
{
    /// <summary>
    /// Map of US state abbreviations to their full names. Used so "California" and
    /// "CA" are interchangeable in both user input and job-location strings.
    /// </summary>
    private static readonly Dictionary<string, string> StateAbbrevToName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AL"] = "Alabama", ["AK"] = "Alaska", ["AZ"] = "Arizona", ["AR"] = "Arkansas",
        ["CA"] = "California", ["CO"] = "Colorado", ["CT"] = "Connecticut", ["DE"] = "Delaware",
        ["FL"] = "Florida", ["GA"] = "Georgia", ["HI"] = "Hawaii", ["ID"] = "Idaho",
        ["IL"] = "Illinois", ["IN"] = "Indiana", ["IA"] = "Iowa", ["KS"] = "Kansas",
        ["KY"] = "Kentucky", ["LA"] = "Louisiana", ["ME"] = "Maine", ["MD"] = "Maryland",
        ["MA"] = "Massachusetts", ["MI"] = "Michigan", ["MN"] = "Minnesota", ["MS"] = "Mississippi",
        ["MO"] = "Missouri", ["MT"] = "Montana", ["NE"] = "Nebraska", ["NV"] = "Nevada",
        ["NH"] = "New Hampshire", ["NJ"] = "New Jersey", ["NM"] = "New Mexico", ["NY"] = "New York",
        ["NC"] = "North Carolina", ["ND"] = "North Dakota", ["OH"] = "Ohio", ["OK"] = "Oklahoma",
        ["OR"] = "Oregon", ["PA"] = "Pennsylvania", ["RI"] = "Rhode Island", ["SC"] = "South Carolina",
        ["SD"] = "South Dakota", ["TN"] = "Tennessee", ["TX"] = "Texas", ["UT"] = "Utah",
        ["VT"] = "Vermont", ["VA"] = "Virginia", ["WA"] = "Washington", ["WV"] = "West Virginia",
        ["WI"] = "Wisconsin", ["WY"] = "Wyoming", ["DC"] = "District of Columbia"
    };

    private static readonly Dictionary<string, string> StateNameToAbbrev =
        StateAbbrevToName.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Country-level / ambiguous location strings that don't actually pin a job to a
    /// specific city or state. We default-KEEP jobs with these labels rather than reject
    /// them, because the upstream API's radius parameter has already narrowed the result
    /// set — a job tagged just "USA" by the source is most likely in the user's region
    /// (the source wouldn't have surfaced it for a SF search otherwise).
    /// </summary>
    private static readonly string[] AmbiguousLocationTokens =
    {
        "usa", "united states", "u.s.", "u.s.a", "us",
        "multiple locations", "various", "anywhere", "worldwide", "global",
        "north america", "americas", "nationwide"
    };

    /// <summary>
    /// Returns true if the job's location is a plausible match for the user's location.
    ///
    /// Design note: this filter exists to block obvious cross-country leaks (a SF
    /// search shouldn't return Bangalore). It does NOT exist to do precision targeting;
    /// the upstream API's radius parameter handles that. So we default to KEEPING jobs
    /// when the location data is ambiguous (empty, "USA", "Multiple Locations", etc.) —
    /// only reject when we can actively recognize a different US state or a foreign city.
    /// </summary>
    /// <param name="userLocation">Whatever the user typed: "San Francisco, CA",
    /// "Austin", "California", "NY", etc. Empty/null means no filter (keep all).</param>
    /// <param name="radiusMiles">If 0, the user picked "Anywhere" — keep all.</param>
    public static bool Matches(JobResult job, string? userLocation, int? radiusMiles)
    {
        if (string.IsNullOrWhiteSpace(userLocation)) return true;
        if (radiusMiles == 0) return true; // user explicitly chose "Anywhere"

        // Always allow remote roles — they include the user's location by definition.
        if (job.IsRemote) return true;

        // Empty / missing location string — trust the upstream API. It wouldn't have
        // returned this job for a localized search if it were obviously elsewhere.
        if (string.IsNullOrWhiteSpace(job.Location)) return true;

        var jobLoc = job.Location;
        var jobLocLower = jobLoc.ToLowerInvariant();

        // Ambiguous strings ("USA", "Multiple Locations", etc.) — keep, same reasoning.
        if (AmbiguousLocationTokens.Any(t => jobLocLower.Equals(t) || jobLocLower.Contains($" {t}") || jobLocLower.StartsWith($"{t} ") || jobLocLower == t))
            return true;

        // 1. Direct city/keyword match. Splits on the user's commas so "San Francisco, CA"
        // can match a job whose location is just "San Francisco" or just "CA".
        foreach (var token in SplitUserLocation(userLocation))
        {
            if (TokenMatchesLocation(token, jobLoc)) return true;
        }

        // 2. State-aware match. If the user typed a state (full name or abbreviation)
        // and the job is anywhere in that state, keep it. Makes "San Jose, CA" pass for
        // a "San Francisco, CA" search.
        var userState = ExtractStateFromUserInput(userLocation);
        if (userState != null && JobIsInState(jobLoc, userState.Value))
            return true;

        // 3. If the user supplied a US state and the job's location is in a *different*
        // recognized US state, reject. If we can't determine the job's state, default
        // to KEEPING — assume it's a US job that just labeled itself loosely.
        if (userState != null)
        {
            var jobState = ExtractStateFromText(jobLoc);
            if (jobState != null && !jobState.Equals(userState.Value.Abbrev, StringComparison.OrdinalIgnoreCase))
                return false;
            // Job state unknown but user is in the US → keep.
            if (jobState == null) return true;
        }

        // No state context at all (user typed just a city) and nothing matched — reject.
        return false;
    }

    /// <summary>
    /// Pulls a US state abbreviation out of a job-location string if one is clearly
    /// present (e.g. "Austin, TX" → TX, "Boston, MA, USA" → MA). Returns null when no
    /// recognizable US state appears, which lets the caller default to KEEP rather than
    /// false-rejecting jobs with international or ambiguous labels.
    /// </summary>
    private static string? ExtractStateFromText(string text)
    {
        // Word-boundary scan for any of the 51 known abbreviations (50 + DC).
        foreach (var abbrev in StateAbbrevToName.Keys)
        {
            if (Regex.IsMatch(text, $@"\b{abbrev}\b", RegexOptions.IgnoreCase))
                return abbrev;
        }
        // Full state name fallback.
        foreach (var kvp in StateNameToAbbrev)
        {
            if (text.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }
        return null;
    }

    /// <summary>
    /// Sort key used to push exact city matches above state-only matches once the
    /// hard filter has already excluded the rest. Higher is better.
    /// </summary>
    public static int RankMatch(JobResult job, string? userLocation)
    {
        if (string.IsNullOrWhiteSpace(userLocation) || string.IsNullOrWhiteSpace(job.Location))
            return 0;

        var jobLoc = job.Location;

        // City-level match wins. Use the longest non-state token from the user's input
        // as a proxy for "city" (works for "San Francisco, CA" → "San Francisco").
        var cityGuess = SplitUserLocation(userLocation)
            .Where(t => !IsStateToken(t))
            .OrderByDescending(t => t.Length)
            .FirstOrDefault();

        if (!string.IsNullOrEmpty(cityGuess) &&
            jobLoc.Contains(cityGuess, StringComparison.OrdinalIgnoreCase))
            return 3;

        var userState = ExtractStateFromUserInput(userLocation);
        if (userState != null && JobIsInState(jobLoc, userState.Value))
            return 2;

        return job.IsRemote ? 1 : 0;
    }

    // ---------- helpers ----------

    private static IEnumerable<string> SplitUserLocation(string userLocation) =>
        userLocation
            .Split(new[] { ',', '/', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0);

    private static bool TokenMatchesLocation(string token, string jobLocation)
    {
        if (token.Length <= 2)
        {
            // Likely a state code. Use word-boundary regex so "CA" doesn't match
            // "Casablanca, Morocco" or "Canada".
            return Regex.IsMatch(jobLocation, $@"\b{Regex.Escape(token)}\b", RegexOptions.IgnoreCase);
        }
        return jobLocation.Contains(token, StringComparison.OrdinalIgnoreCase);
    }

    private static (string FullName, string Abbrev)? ExtractStateFromUserInput(string userLocation)
    {
        foreach (var token in SplitUserLocation(userLocation))
        {
            if (StateAbbrevToName.TryGetValue(token, out var fullName))
                return (fullName, token.ToUpperInvariant());
            if (StateNameToAbbrev.TryGetValue(token, out var abbrev))
                return (token, abbrev);
        }
        return null;
    }

    private static bool JobIsInState(string jobLocation, (string FullName, string Abbrev) state)
    {
        // Full-name substring match (e.g., job "San Jose, California, USA" contains "California").
        if (jobLocation.Contains(state.FullName, StringComparison.OrdinalIgnoreCase))
            return true;
        // Abbreviation match — word-boundary so "CA" doesn't slip into "Canada".
        return Regex.IsMatch(jobLocation, $@"\b{state.Abbrev}\b", RegexOptions.IgnoreCase);
    }

    private static bool IsStateToken(string token) =>
        StateAbbrevToName.ContainsKey(token) || StateNameToAbbrev.ContainsKey(token);
}
