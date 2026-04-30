using System.Text.RegularExpressions;

namespace CareerCoach.Services;

public enum SeniorityLevel
{
    Entry = 0,
    Mid = 1,
    Senior = 2
}

public record SeniorityAssessment(
    SeniorityLevel Level,
    bool IsUncertain,
    string Reason);

public record JobSeniorityAssessment(
    SeniorityLevel? Level,
    int CompatibilityScore,
    bool IsCompatible,
    string Reason);

/// <summary>
/// Keeps experience-level matching deterministic and conservative. LLM-extracted
/// profile levels are useful signals, but recommendations should not trust them
/// blindly because a single over-classified profile can send entry-level users to
/// senior roles.
/// </summary>
public static class SeniorityMatcher
{
    private static readonly Regex YearsRegex = new(
        @"(?<years>\d{1,2})\+?\s*(?:years|yrs|year)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] EntryKeywords =
    {
        "intern", "internship", "junior", "jr", "associate", "new grad", "new graduate",
        "graduate", "early career", "entry level", "entry-level", "apprentice", "trainee",
        "student", "campus"
    };

    private static readonly string[] MidKeywords =
    {
        "engineer", "developer", "analyst", "specialist", "consultant", "programmer",
        "software engineer ii", "software engineer iii", "developer ii", "developer iii",
        "level ii", "level iii", "mid level", "mid-level"
    };

    private static readonly string[] SeniorKeywords =
    {
        "senior", "sr", "lead", "principal", "staff", "manager", "director", "head of",
        "architect", "vp", "chief", "management", "supervisor", "mentor", "mentored",
        "led team", "team lead"
    };

    private static readonly string[] StudentSignals =
    {
        "student", "candidate", "expected graduation", "graduated", "recent graduate",
        "bachelor", "b.s.", "bs ", "master", "m.s.", "ms ", "university", "college"
    };

    public static SeniorityAssessment InferUserLevel(global::UserProfileRecord profile)
    {
        var text = JoinProfileText(profile);
        var normalizedProfileLevel = Normalize(profile.ExperienceLevel);
        var maxYears = ExtractMaxYears(text);

        var entryScore = 0;
        var midScore = 0;
        var seniorScore = 0;
        var reasons = new List<string>();

        if (normalizedProfileLevel is not null)
        {
            // Treat the stored profile level as one signal, not the verdict.
            AddSignal(normalizedProfileLevel.Value, 2);
            reasons.Add($"stored profile level={ToLabel(normalizedProfileLevel.Value)}");
        }

        if (maxYears.HasValue)
        {
            if (maxYears.Value >= 7)
            {
                seniorScore += 4;
                reasons.Add($"{maxYears.Value}+ years suggests senior");
            }
            else if (maxYears.Value >= 3)
            {
                midScore += 4;
                reasons.Add($"{maxYears.Value} years suggests mid");
            }
            else
            {
                entryScore += 3;
                reasons.Add($"{maxYears.Value} years suggests entry");
            }
        }

        if (ContainsAny(text, EntryKeywords))
        {
            entryScore += 3;
            reasons.Add("entry/student/internship signal");
        }

        if (ContainsAny(text, StudentSignals) && !HasStrongFullTimeSignal(profile.Roles))
        {
            entryScore += 2;
            reasons.Add("student or recent graduate signal");
        }

        var rolesText = string.Join(" ", profile.Roles);
        if (ContainsAny(rolesText, SeniorKeywords))
        {
            seniorScore += 3;
            reasons.Add("senior or leadership title history");
        }
        else if (ContainsAny(rolesText, MidKeywords))
        {
            midScore += 2;
            reasons.Add("full-time practitioner title history");
        }

        if (ContainsAny(text, SeniorKeywords))
        {
            seniorScore += 2;
            reasons.Add("leadership/management resume language");
        }

        var best = new[]
        {
            (Level: SeniorityLevel.Entry, Score: entryScore),
            (Level: SeniorityLevel.Mid, Score: midScore),
            (Level: SeniorityLevel.Senior, Score: seniorScore)
        }.OrderByDescending(x => x.Score).ThenBy(x => x.Level).First();

        var secondScore = new[] { entryScore, midScore, seniorScore }.OrderDescending().Skip(1).First();
        var uncertain = best.Score == 0 || best.Score - secondScore <= 1;

        // When signals are sparse or conflicted, bias downward. This is the safe
        // fallback that prevents ambiguous entry-level profiles from receiving
        // senior/leadership recommendations by default.
        var level = uncertain && best.Level > SeniorityLevel.Entry
            ? (best.Level == SeniorityLevel.Senior ? SeniorityLevel.Mid : SeniorityLevel.Entry)
            : best.Level;

        return new SeniorityAssessment(
            level,
            uncertain,
            reasons.Count == 0 ? "no reliable seniority signals; defaulted conservatively" : string.Join("; ", reasons));

        void AddSignal(SeniorityLevel level, int weight)
        {
            if (level == SeniorityLevel.Entry) entryScore += weight;
            else if (level == SeniorityLevel.Mid) midScore += weight;
            else seniorScore += weight;
        }
    }

    public static JobSeniorityAssessment AssessJob(JobResult job, SeniorityLevel userLevel)
    {
        var jobLevel = ClassifyJob(job.Title, job.DescriptionSnippet);

        // Hard-block only when the job is more than one level above the user (e.g. Senior for an Entry user).
        // One level up (e.g. Mid for an Entry user) is common in real job searching and should be allowed.
        if (jobLevel is not null && (int)jobLevel.Value - (int)userLevel > 1)
        {
            return new JobSeniorityAssessment(
                jobLevel,
                -100,
                false,
                $"job level {ToLabel(jobLevel.Value)} exceeds user level {ToLabel(userLevel)}");
        }

        var score = jobLevel is null
            ? 8
            : 35 - (Math.Abs((int)userLevel - (int)jobLevel.Value) * 10);

        return new JobSeniorityAssessment(
            jobLevel,
            score,
            true,
            jobLevel is null ? "job seniority unclear; allowed with lower confidence" : "seniority compatible");
    }

    public static SeniorityLevel? ClassifyJob(string title, string? description)
    {
        var titleText = title ?? "";
        var combined = $"{titleText} {description ?? ""}";
        var years = ExtractMaxYears(combined);

        if (ContainsAny(titleText, SeniorKeywords) || years >= 7)
            return SeniorityLevel.Senior;

        if (ContainsAny(titleText, EntryKeywords) || ContainsAny(combined, EntryKeywords) || years <= 2)
            return SeniorityLevel.Entry;

        if (ContainsAny(titleText, MidKeywords) || ContainsAny(combined, MidKeywords) || years is >= 3 and <= 6)
            return SeniorityLevel.Mid;

        return null;
    }

    public static string ToLabel(SeniorityLevel level) => level switch
    {
        SeniorityLevel.Entry => "entry",
        SeniorityLevel.Mid => "mid",
        SeniorityLevel.Senior => "senior",
        _ => "entry"
    };

    public static SeniorityLevel? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim().ToLowerInvariant();
        if (v is "entry" or "junior" or "jr" or "intern" or "student" or "new grad") return SeniorityLevel.Entry;
        if (v is "mid" or "intermediate" or "mid-level" or "mid level") return SeniorityLevel.Mid;
        if (v is "senior" or "sr" or "lead" or "principal" or "staff" or "manager" or "director") return SeniorityLevel.Senior;
        return null;
    }

    private static string JoinProfileText(global::UserProfileRecord profile) =>
        string.Join(" ", new[]
        {
            string.Join(" ", profile.Roles),
            string.Join(" ", profile.Skills),
            string.Join(" ", profile.Tools),
            profile.ExperienceLevel ?? "",
            profile.Summary ?? "",
            profile.Education,
            profile.ResumeText ?? ""
        });

    private static int? ExtractMaxYears(string text)
    {
        var years = YearsRegex.Matches(text ?? "")
            .Select(m => int.TryParse(m.Groups["years"].Value, out var y) ? y : 0)
            .Where(y => y > 0 && y < 50)
            .DefaultIfEmpty(0)
            .Max();

        return years == 0 ? null : years;
    }

    private static bool HasStrongFullTimeSignal(IEnumerable<string> roles) =>
        roles.Any(r => ContainsAny(r, MidKeywords) || ContainsAny(r, SeniorKeywords)) &&
        !roles.All(r => ContainsAny(r, EntryKeywords));

    private static bool ContainsAny(string value, IEnumerable<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return keywords.Any(keyword => Regex.IsMatch(
            value,
            $@"(^|[^a-z0-9]){Regex.Escape(keyword)}([^a-z0-9]|$)",
            RegexOptions.IgnoreCase));
    }
}
