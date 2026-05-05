using System.Text.RegularExpressions;

namespace CareerCoach.Services;

/// <summary>
/// Cheap, deterministic location lookup over the resume header. Used as a fallback
/// when the LLM-based profile extractor returns no location, which happens often
/// enough that we shouldn't rely on it alone for downstream job-search queries.
/// </summary>
public static class ResumeLocationExtractor
{
    private static readonly HashSet<string> UsStateCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AL","AK","AZ","AR","CA","CO","CT","DE","FL","GA",
        "HI","ID","IL","IN","IA","KS","KY","LA","ME","MD",
        "MA","MI","MN","MS","MO","MT","NE","NV","NH","NJ",
        "NM","NY","NC","ND","OH","OK","OR","PA","RI","SC",
        "SD","TN","TX","UT","VT","VA","WA","WV","WI","WY","DC"
    };

    // "Austin, TX" / "San Francisco, CA 94103" / "New York City, NY"
    private static readonly Regex CityStateRegex = new(
        @"\b([A-Z][A-Za-z\.\-']*(?:\s+[A-Z][A-Za-z\.\-']*){0,3}),\s*([A-Z]{2})(?:\s+\d{5}(?:-\d{4})?)?\b",
        RegexOptions.Compiled);

    // "Remote" / "Worldwide" / "Anywhere" — captured only when standalone
    private static readonly Regex RemoteRegex = new(
        @"^\s*(?:remote|worldwide|anywhere)(?:\s*[-–—]\s*\w+)?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Tries to find a "City, ST" location in the first <paramref name="maxLinesToScan"/> non-empty
    /// lines of the resume. Returns null if nothing convincing is found.
    /// </summary>
    public static string? ExtractFromHeader(string? resumeText, int maxLinesToScan = 30)
    {
        if (string.IsNullOrWhiteSpace(resumeText)) return null;

        var headerLines = resumeText
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Take(maxLinesToScan)
            .ToList();

        // 1. Look for "City, ST" (with optional ZIP) anchored on a state abbreviation we recognize.
        foreach (var line in headerLines)
        {
            foreach (Match m in CityStateRegex.Matches(line))
            {
                var city = m.Groups[1].Value.Trim();
                var state = m.Groups[2].Value.ToUpperInvariant();
                if (!UsStateCodes.Contains(state)) continue;
                // Skip degenerate matches where the "city" is actually a header word.
                if (city.Length < 2) continue;
                if (LooksLikeNoiseCity(city)) continue;
                return $"{city}, {state}";
            }
        }

        // 2. Look for a line that says only "Remote" / "Worldwide" / "Anywhere".
        foreach (var line in headerLines)
        {
            if (RemoteRegex.IsMatch(line)) return "Remote";
        }

        return null;
    }

    private static bool LooksLikeNoiseCity(string city)
    {
        // The regex will happily match things like "References, NA" or "Skills, IT".
        // Reject obvious section headings.
        string[] noise =
        {
            "References", "Skills", "Experience", "Education", "Summary",
            "Objective", "Profile", "Projects", "Certifications", "Awards",
            "Contact", "Email", "Phone"
        };
        return noise.Any(n => string.Equals(n, city, StringComparison.OrdinalIgnoreCase));
    }
}
