namespace CareerCoach.Services;

/// <summary>
/// Decides whether a job listing is plausibly a tech / software role.
///
/// The job-board APIs we aggregate (JSearch, Adzuna, The Muse, Remotive) sometimes
/// return non-technical roles for tech-flavored queries — cashier, driver, nurse, etc.
/// We apply two filters:
///   1. An inclusive list of tech-title keywords. The role must contain one.
///   2. A hard blocklist of clearly non-tech roles. The role must not contain any.
///
/// Both checks look at the title. The blocklist also looks at the snippet, since some
/// retail postings hide "Cashier / Sales Associate" wording in the body and put a vague
/// title up top.
/// </summary>
public static class TechJobFilter
{
    private static readonly string[] TechTitleKeywords =
    {
        "engineer", "developer", "software", "data", "analyst", "architect", "devops", "sre",
        "cloud", "backend", "frontend", "fullstack", "full stack", "full-stack", "mobile",
        "machine learning", "ai ", " ai", "ml ", " ml", "security", "cyber", "network",
        "infrastructure", "platform", "systems", "product manager", "technical", "tech",
        "qa ", " qa", "quality assurance", "test", "database", "dba", "it ", " it ",
        "scrum", "agile", "program manager", "project manager", "cto", "vp engineering",
        "information technology", "computer", "application", "web ", "api ", "saas",
        "blockchain", "embedded", "firmware", "ux ", " ux", "ui ", " ui", "design"
    };

    /// <summary>
    /// Roles that look obviously non-technical. Matched against the title (and snippet
    /// for a few retail-flavored words). Anything matching these is dropped even if it
    /// happens to contain a tech-sounding word like "systems" or "support".
    ///
    /// Note on the "Technician" trap: many non-software roles end in "Technician" and
    /// match our inclusive "tech" keyword (Pharmacy Technician, Vet Tech, Surgical Tech,
    /// Auto Tech, etc.). Each of those professions needs its own entry below so the
    /// blocklist beats the inclusive match.
    /// </summary>
    private static readonly string[] NonTechTitleBlocklist =
    {
        "cashier", "barista", "server", "waitress", "waiter", "bartender", "host ", "hostess",
        "sales associate", "sales representative", "retail associate", "store associate",
        "customer service representative", "csr ",
        "driver", "delivery", "courier", "warehouse", "stocker", "picker", "packer",
        "cleaner", "janitor", "housekeeping", "groundskeeper", "landscaper",
        // Healthcare — both clinical roles and the "Technician" variants that the
        // inclusive "tech" keyword would otherwise let through.
        "nurse", "rn ", "lpn ", "cna ", "medical assistant", "phlebotomist", "caregiver",
        "pharmacy", "pharmacist", "pharmacy technician", "pharmacy tech",
        "veterinary", "veterinarian", "vet tech", "vet technician",
        "dental", "dentist", "dental hygienist", "dental assistant",
        "surgical tech", "surgical technologist", "scrub tech",
        "patient care technician", "patient care tech",
        "ophthalmic", "optician", "optical tech",
        "radiology tech", "radiologic technologist", "ultrasound tech", "sonographer",
        "lab technician", "laboratory technician", "medical lab", "histology",
        "respiratory therapist", "physical therapist", "occupational therapist",
        "ekg tech", "ecg tech", "cardiac tech",
        "emt", "paramedic",
        // Trades — automotive and HVAC technicians also collide with "tech".
        "automotive", "auto tech", "auto technician", "diesel", "mechanic",
        "hvac", "tire tech", "lube tech",
        // Other non-tech "Technician" patterns that match our inclusive "tech" keyword.
        // Deliberately conservative: avoid words like "field technician" or "quality
        // technician" that could legitimately refer to IT field service / software QA.
        "service technician", "maintenance technician", "manufacturing technician",
        "production technician", "appliance technician", "plumbing technician",
        "elevator technician", "roofing technician",
        "teacher", "tutor", "professor", "preschool", "daycare",
        "cook", "chef", "dishwasher", "kitchen",
        "security guard", "police officer", "firefighter",
        "construction", "plumber", "electrician",
        "real estate agent", "loan officer", "insurance agent", "underwriter",
        "receptionist", "front desk", "concierge",
        "social worker", "counselor", "therapist",
        // Office / clerical — sometimes contain "data" (Data Entry Clerk) or "systems"
        // (Systems Office Clerk) which would otherwise pass the inclusive list.
        "data entry", "office clerk", "office assistant",
        "executive assistant", "administrative assistant", "admin assistant"
    };

    /// <summary>
    /// True if the job's title contains a tech keyword and does not match the non-tech blocklist.
    /// </summary>
    public static bool IsTechRole(JobResult job)
    {
        return HasTechTitleKeyword(job.Title) && !LooksNonTech(job);
    }

    /// <summary>
    /// True if the value contains any of the tech-title keywords. Exposed so the
    /// recommendations flow can also validate generated search queries.
    /// </summary>
    public static bool HasTechTitleKeyword(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return TechTitleKeywords.Any(kw => value.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksNonTech(JobResult job)
    {
        var title = job.Title ?? "";
        if (NonTechTitleBlocklist.Any(kw => title.Contains(kw, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Also peek at the snippet so we catch postings whose title is generic but whose
        // description makes the non-tech nature obvious. Keep this list narrow so we don't
        // over-filter legitimate tech roles that happen to mention these words in passing.
        var snippet = job.DescriptionSnippet ?? "";
        string[] snippetBlocklist = { "cashier", "sales associate", "barista", "waitress", "waiter" };
        return snippetBlocklist.Any(kw => snippet.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }
}
