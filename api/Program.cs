using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using CareerCoach.Agent;
using CareerCoach.Services;

var b = WebApplication.CreateBuilder(args);
b.Configuration.AddEnvironmentVariables();
b.Services.AddHttpClient();
b.Services.AddSingleton<GradientClient>();
b.Services.AddSingleton<Db>();
b.Services.AddSingleton<JSearchClient>();
b.Services.AddSingleton<AdzunaClient>();
b.Services.AddSingleton<TheMuseClient>();
b.Services.AddSingleton<RemotiveClient>();
b.Services.AddSingleton<JobAggregatorService>();
b.Services.AddSingleton<CareerCoachAgent>();
b.Services.AddSingleton<ResumeParser>();
b.Services.AddSingleton<EmailSender>();
b.Services.AddSingleton<AuthService>();
b.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin()));

var app = b.Build();
app.UseCors();

// Ensure tables exist on startup (non-fatal if DB is temporarily unavailable)
var db = app.Services.GetRequiredService<Db>();
try { await db.EnsureAuthTablesAsync(); }
catch (Exception ex) { Console.WriteLine($"[WARN] Could not ensure auth tables: {ex.Message}"); }
try { await db.EnsureProfileTablesAsync(); }
catch (Exception ex) { Console.WriteLine($"[WARN] Could not ensure profile tables: {ex.Message}"); }

app.MapGet("/", () => "API up");

// Health check endpoint
app.MapGet("/api/health", ([FromServices] GradientClient llm, [FromServices] ResumeParser parser) =>
{
    try
    {
        var hasApiKey = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GRADIENT_API_KEY"));
        return Results.Ok(new
        {
            status = "healthy",
            services = new
            {
                llm = "initialized",
                parser = "initialized",
                apiKeyConfigured = hasApiKey
            },
            message = hasApiKey ? "All services ready" : "WARNING: GRADIENT_API_KEY not set"
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { status = "degraded", error = ex.Message });
    }
});

app.MapPost("/api/resume/analyze", async (
    [FromServices] GradientClient llm,
    [FromServices] Db db,
    Analyze r) =>
{
    var sys =
      "You are a resume analyzer. Analyze the resume for ATS compatibility and return an ATS score on a scale of 1-100, with 100 being the highest compatibility. Return ONLY valid JSON with keys: " +
      "skills (string[]), tools (string[]), roles (string[]), summary (string). " +
      "No markdown fences, no commentary.";

    var text = r.resumeText.Length > 8000 ? r.resumeText[..8000] : r.resumeText;

    var raw = await llm.ChatAsync(sys, $"Resume:\n{text}\nReturn only JSON.", maxTokens: 500);

    // 1) Extract assistant content from OpenAI-style response
    static string ExtractContent(string openaiJson)
    {
        using var doc = JsonDocument.Parse(openaiJson);
        return doc.RootElement.GetProperty("choices")[0]
                  .GetProperty("message")
                  .GetProperty("content")
                  .GetString() ?? "";
    }

    // 2) Strip ```json ... ``` fences if present
    static string StripFences(string s)
    {
        s = s.Trim();
        if (s.StartsWith("```"))
        {
            var firstNewline = s.IndexOf('\n');
            var lastFence = s.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
                s = s.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
        }
        return s;
    }

    // 3) Normalize (summary can come back as array or string)
    static object Normalize(string json)
    {
        using var doc = JsonDocument.Parse(json);
        static string[] Arr(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out var el)) return Array.Empty<string>();
            if (el.ValueKind == JsonValueKind.Array)
                return el.EnumerateArray()
                         .Where(x => x.ValueKind == JsonValueKind.String)
                         .Select(x => x.GetString()!.Trim())
                         .Where(x => x.Length > 0)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .ToArray();
            if (el.ValueKind == JsonValueKind.String)
                return new[] { el.GetString()!.Trim() };
            return Array.Empty<string>();
        }

        var root = doc.RootElement;

        var skills = Arr(root, "skills");
        var tools  = Arr(root, "tools");
        var roles  = Arr(root, "roles");

        // summary may be string or array; coerce to single string
        string summary = "";
        if (root.TryGetProperty("summary", out var sEl))
        {
            if (sEl.ValueKind == JsonValueKind.String) summary = sEl.GetString()!.Trim();
            else if (sEl.ValueKind == JsonValueKind.Array)
                summary = string.Join(", ",
                    sEl.EnumerateArray()
                       .Where(x => x.ValueKind == JsonValueKind.String)
                       .Select(x => x.GetString()!.Trim())
                       .Where(x => x.Length > 0));
        }

        return new { skills, tools, roles, summary };
    }

    var content = ExtractContent(raw);
    var stripped = StripFences(content);

    object result;
    try
    {
        result = Normalize(stripped);
    }
    catch
    {
        // fallback: return raw content so you can see what happened
        result = new { error = "parse_failed", rawContent = content };
    }

    // save session but never break the response if DB hiccups
    try { await db.SaveSessionAsync(r.userId, "resumeAnalysis", raw); }
    catch (Exception ex) { Console.WriteLine($"[WARN] SaveSession failed: {ex.Message}"); }

    // upsert user profile so recommendations and GetUserProfileTool have real data
    try
    {
        using var pdoc = JsonDocument.Parse(stripped);
        var pr = pdoc.RootElement;

        static string[] ExtractArr(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out var el)) return Array.Empty<string>();
            if (el.ValueKind == JsonValueKind.Array)
                return el.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()!.Trim())
                    .Where(x => x.Length > 0)
                    .ToArray();
            if (el.ValueKind == JsonValueKind.String)
                return new[] { el.GetString()!.Trim() };
            return Array.Empty<string>();
        }

        var pSkills = ExtractArr(pr, "skills");
        var pTools  = ExtractArr(pr, "tools");
        var pRoles  = ExtractArr(pr, "roles");
        var pSummary = pr.TryGetProperty("summary", out var se) && se.ValueKind == JsonValueKind.String
            ? se.GetString() : null;

        if (pSkills.Length > 0 || pRoles.Length > 0)
            await db.UpsertUserProfileAsync(r.userId, pSkills, pTools, pRoles, null, pSummary, null);
    }
    catch (Exception ex) { Console.WriteLine($"[WARN] Profile upsert failed: {ex.Message}"); }

    return Results.Json(result);
});

// New AI Agent endpoint - this is where the magic happens!
app.MapPost("/api/agent/chat", async (
    [FromServices] CareerCoachAgent agent,
    [FromBody] AgentChatRequest req) =>
{
    try
    {
        var response = await agent.ProcessMessageAsync(
            req.ConversationId ?? Guid.NewGuid().ToString(),
            req.UserId,
            req.Message
        );

        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Agent Error"
        );
    }
});

// Endpoint to clear conversation history
app.MapDelete("/api/agent/conversation/{conversationId}", (
    [FromServices] CareerCoachAgent agent,
    string conversationId) =>
{
    agent.ClearConversation(conversationId);
    return Results.Ok(new { message = "Conversation cleared" });
});

// Endpoint to get conversation history
app.MapGet("/api/agent/conversation/{conversationId}", (
    [FromServices] CareerCoachAgent agent,
    string conversationId) =>
{
    var history = agent.GetConversationHistory(conversationId);
    if (history == null)
    {
        return Results.NotFound(new { message = "Conversation not found" });
    }
    return Results.Ok(new { messages = history });
});

// Resume file upload endpoint - accepts PDF or DOCX
app.MapPost("/api/resume/upload", async (
    [FromServices] ResumeParser parser,
    [FromServices] CareerCoachAgent agent,
    [FromServices] GradientClient parser_llm,
    [FromServices] Db profile_db,
    IFormFile file,
    [FromForm] string userId,
    [FromForm] string? targetRole,
    [FromForm] string? targetIndustry) =>
{
    // Validation
    if (file == null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "No file uploaded" });
    }

    const long maxFileSize = 5 * 1024 * 1024; // 5MB
    if (file.Length > maxFileSize)
    {
        return Results.BadRequest(new { error = "File size exceeds 5MB limit" });
    }

    using var bufferedStream = new MemoryStream();
    await file.CopyToAsync(bufferedStream);
    bufferedStream.Position = 0;

    if (!ResumeFileValidator.TryNormalizeFileMetadata(file, bufferedStream, out var normalizedFileName, out var validationError))
    {
        return Results.BadRequest(new { error = validationError ?? "Only PDF and DOCX files are supported" });
    }

    try
    {
        var uploadSw = System.Diagnostics.Stopwatch.StartNew();

        // Parse the resume file
        var parseSw = System.Diagnostics.Stopwatch.StartNew();
        bufferedStream.Position = 0;
        var parseResult = await parser.ParseResumeAsync(bufferedStream, normalizedFileName);
        parseSw.Stop();
        Console.WriteLine($"[TIMING] Resume parsing completed in {parseSw.ElapsedMilliseconds}ms");

        if (!parseResult.Success)
        {
            return Results.BadRequest(new { error = parseResult.ErrorMessage });
        }

        // Start a conversation with the agent to analyze the resume
        var conversationId = Guid.NewGuid().ToString();

        // Limit resume text to avoid timeout (keep first 8000 characters, roughly 1200 words)
        var resumeText = parseResult.Text.Length > 8000
            ? parseResult.Text.Substring(0, 8000) + "\n[Resume truncated for processing]"
            : parseResult.Text;

        var parserContext = JsonSerializer.Serialize(new
        {
            word_count = parseResult.WordCount,
            character_count = parseResult.CharacterCount,
            file_type = parseResult.FileType,
            extraction_warning = parseResult.WordCount < 50
                ? "Very few words were extracted. The file may be image-based or heavily formatted — treat content as potentially incomplete."
                : null
        });

        // Build the request message
        var requestMessage = $"I've uploaded my resume. Please analyze it for ATS compatibility and provide improvement suggestions.";
        if (!string.IsNullOrEmpty(targetRole))
        {
            requestMessage += $" I'm targeting a {targetRole} position.";
        }
        if (!string.IsNullOrEmpty(targetIndustry))
        {
            requestMessage += $" in the {targetIndustry} industry.";
        }
        requestMessage +=
            $"\n\nParser diagnostics (pass this into tool calls as parser_context when analyzing the resume):\n{parserContext}" +
            $"\n\nResume text:\n{resumeText}";

        var profileSaveTask = ExtractAndSaveProfileAsync();
        var agentSw = System.Diagnostics.Stopwatch.StartNew();
        var agentTask = agent.ProcessMessageAsync(conversationId, userId, requestMessage);

        await Task.WhenAll(profileSaveTask, agentTask);
        agentSw.Stop();
        Console.WriteLine($"[TIMING] Agent analysis completed in {agentSw.ElapsedMilliseconds}ms");

        var agentResponse = await agentTask;
        uploadSw.Stop();
        Console.WriteLine($"[TIMING] Total /api/resume/upload completed in {uploadSw.ElapsedMilliseconds}ms");

        async Task ExtractAndSaveProfileAsync()
        {
            var profileSw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var extractSys =
                    "You are a resume data extractor. Extract structured data from the resume text. " +
                    "Return ONLY valid JSON, no markdown fences, no commentary. " +
                    "Only extract information directly supported by the resume text. Do not infer unrelated career paths. " +
                    "JSON keys: skills (string[]), tools (string[]), roles (string[] of job titles explicitly present in the resume or the closest direct tech equivalents), " +
                    "education (array of {degree, institution, year}), " +
                    "experience_level (one of: entry, mid, senior, lead), summary (string).";

                var extractRaw = await parser_llm.ChatAsync(extractSys,
                    $"Resume:\n{resumeText[..Math.Min(6000, resumeText.Length)]}\nReturn only JSON.",
                    maxTokens: 800);

            // parse the LLM response
            using var extractDoc = JsonDocument.Parse(extractRaw);
            var extractContent = extractDoc.RootElement
                .GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";

            var jsonStart = extractContent.IndexOf('{');
            var jsonEnd = extractContent.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
                extractContent = extractContent[jsonStart..(jsonEnd + 1)];

            using var profileDoc = JsonDocument.Parse(extractContent);
            var pr = profileDoc.RootElement;

            static string[] ExtractArr(JsonElement root, string key)
            {
                if (!root.TryGetProperty(key, out var el)) return Array.Empty<string>();
                if (el.ValueKind == JsonValueKind.Array)
                    return el.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString()!.Trim())
                        .Where(x => x.Length > 0)
                        .ToArray();
                if (el.ValueKind == JsonValueKind.String)
                    return new[] { el.GetString()!.Trim() };
                return Array.Empty<string>();
            }

            var pSkills = ExtractArr(pr, "skills");
            var pTools  = ExtractArr(pr, "tools");
            var pRoles  = ExtractArr(pr, "roles");
            var pExpLevel = pr.TryGetProperty("experience_level", out var expEl) && expEl.ValueKind == JsonValueKind.String
                ? expEl.GetString() : null;
            var pSummary = pr.TryGetProperty("summary", out var sumEl) && sumEl.ValueKind == JsonValueKind.String
                ? sumEl.GetString() : null;
            var pEducation = pr.TryGetProperty("education", out var eduEl)
                ? JsonSerializer.Serialize(eduEl) : "[]";

            Console.WriteLine($"[INFO] Profile extraction: skills={pSkills.Length}, roles={pRoles.Length}, tools={pTools.Length}, expLevel={pExpLevel}");

            // Always save — even empty arrays anchor the profile row so recommendations can work
                await profile_db.UpsertUserProfileAsync(
                    userId, pSkills, pTools, pRoles,
                    pExpLevel, pSummary, null,
                    resumeText: parseResult.Text,
                    education: pEducation,
                    replaceResumeData: true);

                profileSw.Stop();
                Console.WriteLine($"[TIMING] Profile extraction/save completed in {profileSw.ElapsedMilliseconds}ms");
                Console.WriteLine($"[INFO] Profile saved for user {userId}");
            }
            catch (Exception ex)
            {
                profileSw.Stop();
                Console.WriteLine($"[TIMING] Profile extraction/save failed after {profileSw.ElapsedMilliseconds}ms");
                Console.WriteLine($"[WARN] Profile extraction from resume failed: {ex.Message}");
                Console.WriteLine($"[WARN] Stack: {ex.StackTrace}");
                // Still save resume text even if extraction failed, so the profile row exists
                try
                {
                    await profile_db.UpsertUserProfileAsync(
                        userId, [], [], [],
                        null, null, null,
                        resumeText: parseResult.Text,
                        education: "[]",
                        replaceResumeData: true);
                    Console.WriteLine($"[INFO] Saved bare profile (resume text only) for user {userId}");
                }
                catch (Exception dbEx)
                {
                    Console.WriteLine($"[WARN] Even bare profile save failed: {dbEx.Message}");
                }
            }
        }

        static int? ExtractAtsScore(IEnumerable<ToolExecution> toolsUsed)
        {
            var atsTool = toolsUsed.FirstOrDefault(t => t.ToolName == "analyze_ats_compatibility");
            if (atsTool == null || string.IsNullOrWhiteSpace(atsTool.Result)) return null;

            try
            {
                using var scoreDoc = JsonDocument.Parse(atsTool.Result);
                var root = scoreDoc.RootElement;
                if (root.TryGetProperty("overall_score", out var overallScore) && overallScore.TryGetInt32(out var parsedOverall))
                    return Math.Clamp(parsedOverall, 0, 100);
                if (root.TryGetProperty("score", out var score) && score.TryGetInt32(out var parsedScore))
                    return Math.Clamp(parsedScore, 0, 100);
            }
            catch
            {
                if (int.TryParse(atsTool.Result.Trim(), out var parsedRaw))
                    return Math.Clamp(parsedRaw, 0, 100);
            }

            return null;
        }

        var latestAtsScore = ExtractAtsScore(agentResponse.ToolsUsed);
        if (latestAtsScore != null)
        {
            try { await profile_db.UpdateAtsScoreAsync(userId, latestAtsScore.Value); }
            catch (Exception ex) { Console.WriteLine($"[WARN] ATS score update failed: {ex.Message}"); }
        }

        return Results.Ok(new
        {
            parse_result = new
            {
                success        = parseResult.Success,
                word_count     = parseResult.WordCount,
                character_count = parseResult.CharacterCount,
                file_name      = parseResult.FileName,
                file_type      = parseResult.FileType
            },
            agent_analysis = new
            {
                message          = agentResponse.Message,
                tools_used       = agentResponse.ToolsUsed,
                conversation_id  = agentResponse.ConversationId
            }
        });
    }
    catch (Exception ex)
    {
        // Log the full exception for debugging
        Console.WriteLine($"[ERROR] Resume upload failed: {ex.Message}");
        Console.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
        if (ex.InnerException != null)
        {
            Console.WriteLine($"[ERROR] Inner exception: {ex.InnerException.Message}");
        }

        return Results.Problem(
            detail: $"{ex.Message}\n\nInner: {ex.InnerException?.Message ?? "None"}\n\nType: {ex.GetType().Name}",
            statusCode: 500,
            title: "Resume processing error"
        );
    }
}).DisableAntiforgery(); // Disable antiforgery for file upload

app.MapGet("/api/jobs/search", async (
    [FromServices] JobAggregatorService aggregator,
    [FromServices] Db db,
    string? query,
    string? location,
    string? userId,
    bool remoteOnly = false,
    string? experienceLevel = null,
    string? employmentType = null) =>
{
    var q = string.IsNullOrWhiteSpace(query) ? "software engineer" : query.Trim();
    var loc = location?.Trim().Equals("remote", StringComparison.OrdinalIgnoreCase) == true ? null : location?.Trim();

    // Log search query to user profile if user is identified
    if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(query))
    {
        try { await db.LogSearchQueryAsync(userId, q); }
        catch (Exception ex) { Console.WriteLine($"[WARN] LogSearchQuery failed: {ex.Message}"); }
    }

    var result = await aggregator.SearchAllAsync(q, loc, remoteOnly, experienceLevel, employmentType);
    return Results.Ok(new
    {
        totalResults = result.TotalResults,
        jobs = result.Jobs.Select(j => new
        {
            title = j.Title,
            company = j.Company,
            logoUrl = j.LogoUrl,
            location = j.Location,
            isRemote = j.IsRemote,
            employmentType = j.EmploymentType,
            minSalary = j.MinSalary,
            maxSalary = j.MaxSalary,
            salaryCurrency = j.SalaryCurrency,
            salaryPeriod = j.SalaryPeriod,
            descriptionSnippet = j.DescriptionSnippet,
            applyLink = j.ApplyLink,
            postedAt = j.PostedAt
        })
    });
});

app.MapGet("/api/jobs/recommended", async (
    [FromServices] Db db,
    [FromServices] JobAggregatorService aggregator,
    [FromServices] GradientClient llm,
    string userId,
    string? location) =>
{
    var profile = await db.GetUserProfileAsync(userId);
    if (profile == null)
        return Results.NotFound(new { error = "No profile found. Please upload your resume first." });

    var isRemote = location?.Equals("remote", StringComparison.OrdinalIgnoreCase) == true;
    var searchLocation = isRemote ? null : location?.Trim();

    // Use LLM to generate 2 targeted, diverse search queries from the user's profile
    var queries = new List<string>();
    try
    {
        string profileSummary;
        if (profile.Roles.Length > 0 || profile.Skills.Length > 0)
        {
            profileSummary =
                $"Roles: {string.Join(", ", profile.Roles.Take(3))}\n" +
                $"Skills: {string.Join(", ", profile.Skills.Take(8))}\n" +
                $"Tools: {string.Join(", ", profile.Tools.Take(5))}\n" +
                $"Experience level: {profile.ExperienceLevel ?? "not specified"}";
        }
        else if (!string.IsNullOrWhiteSpace(profile.ResumeText))
        {
            // Profile was saved but extraction produced no structured data — use raw resume text
            profileSummary = $"Resume text (first 2000 chars):\n{profile.ResumeText[..Math.Min(2000, profile.ResumeText.Length)]}";
        }
        else
        {
            // No profile data at all — skip LLM, go straight to fallback
            throw new InvalidOperationException("Profile has no structured data or resume text.");
        }

        var querySys =
            "You are a tech job search expert. Given a candidate's tech/software profile, generate exactly 2 distinct " +
            "job search query strings suitable for a job board (like Indeed or LinkedIn). " +
            "Each query must be a specific tech or software job title — examples: \"Senior React Developer\", " +
            "\"Data Engineer Python AWS\", \"DevOps Engineer Kubernetes\". " +
            "Never generate queries for non-tech roles (e.g. sales, marketing, HR, retail, nursing). " +
            "If the profile is unclear, default to software engineering roles. " +
            "Return ONLY a JSON array of exactly 2 strings. No commentary, no markdown fences.";

        var queryRaw = await llm.ChatAsync(querySys, profileSummary, maxTokens: 120);
        using var queryDoc = JsonDocument.Parse(queryRaw);
        var queryContent = queryDoc.RootElement
            .GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";

        var arrStart = queryContent.IndexOf('[');
        var arrEnd = queryContent.LastIndexOf(']');
        if (arrStart >= 0 && arrEnd > arrStart)
        {
            using var arrDoc = JsonDocument.Parse(queryContent[arrStart..(arrEnd + 1)]);
            queries = arrDoc.RootElement.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!.Trim())
                .Where(q => q.Length > 0)
                .Take(2)
                .ToList();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[WARN] LLM query generation failed: {ex.Message}");
    }

    // Fall back to simple query if LLM failed
    if (queries.Count == 0)
    {
        var topRole = profile.Roles.FirstOrDefault() ?? "software engineer";
        var topSkills = profile.Skills.Take(2).ToArray();
        queries.Add(string.Join(" ", new[] { topRole }.Concat(topSkills)));
    }

    Console.WriteLine($"[INFO] Recommendation search queries: {string.Join(" | ", queries)}");

    // Tech keywords used to validate generated queries and post-filter irrelevant job results.
    var techTitleKeywords = new[]
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

    bool HasTechTitleKeyword(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        techTitleKeywords.Any(kw => value.Contains(kw, StringComparison.OrdinalIgnoreCase));

    var profileTerms = profile.Roles
        .Concat(profile.Skills)
        .Concat(profile.Tools)
        .SelectMany(v => v.Split(new[] { ' ', '/', '-', ',', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
        .Select(v => v.Trim())
        .Where(v => v.Length >= 3)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    bool MatchesProfile(string title, string snippet) =>
        profileTerms.Length == 0 ||
        profileTerms.Any(term =>
            title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains(term, StringComparison.OrdinalIgnoreCase));

    bool IsRelevantTechJob(JobResult job) =>
        HasTechTitleKeyword(job.Title) && MatchesProfile(job.Title, job.DescriptionSnippet ?? "");

    queries = queries
        .Where(q => HasTechTitleKeyword(q) && MatchesProfile(q, ""))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(2)
        .ToList();

    // Run all queries and aggregate results
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var allJobs = new List<object>();

    foreach (var q in queries)
    {
        try
        {
            var result = await aggregator.SearchAllAsync(q, searchLocation, isRemote, profile.ExperienceLevel, museCategory: "Technology");
            foreach (var j in result.Jobs)
            {
                var key = $"{j.Company}|{j.Title}";
                if (IsRelevantTechJob(j) && seen.Add(key))
                {
                    allJobs.Add(new
                    {
                        title = j.Title,
                        company = j.Company,
                        logoUrl = j.LogoUrl,
                        location = j.Location,
                        isRemote = j.IsRemote,
                        employmentType = j.EmploymentType,
                        minSalary = j.MinSalary,
                        maxSalary = j.MaxSalary,
                        salaryCurrency = j.SalaryCurrency,
                        salaryPeriod = j.SalaryPeriod,
                        descriptionSnippet = j.DescriptionSnippet,
                        applyLink = j.ApplyLink,
                        postedAt = j.PostedAt
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Job search for query '{q}' failed: {ex.Message}");
        }
    }

    // If fewer than 3 results, run a broader fallback search
    if (allJobs.Count < 3)
    {
        try
        {
            var fallbackQuery = profile.Roles.FirstOrDefault() ?? "software engineer";
            if (!HasTechTitleKeyword(fallbackQuery) || !MatchesProfile(fallbackQuery, ""))
                fallbackQuery = profile.Skills.FirstOrDefault(s => HasTechTitleKeyword(s)) ?? "software engineer";

            var fallback = await aggregator.SearchAllAsync(fallbackQuery, searchLocation, isRemote, profile.ExperienceLevel, museCategory: "Technology");
            foreach (var j in fallback.Jobs)
            {
                var key = $"{j.Company}|{j.Title}";
                if (IsRelevantTechJob(j) && seen.Add(key))
                {
                    allJobs.Add(new
                    {
                        title = j.Title,
                        company = j.Company,
                        logoUrl = j.LogoUrl,
                        location = j.Location,
                        isRemote = j.IsRemote,
                        employmentType = j.EmploymentType,
                        minSalary = j.MinSalary,
                        maxSalary = j.MaxSalary,
                        salaryCurrency = j.SalaryCurrency,
                        salaryPeriod = j.SalaryPeriod,
                        descriptionSnippet = j.DescriptionSnippet,
                        applyLink = j.ApplyLink,
                        postedAt = j.PostedAt
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Fallback job search failed: {ex.Message}");
        }
    }

    return Results.Ok(new
    {
        matchedOn = new
        {
            roles = profile.Roles.Take(3),
            skills = profile.Skills.Take(5),
            experienceLevel = profile.ExperienceLevel ?? "not specified",
            atsScore = profile.AtsScore,
            searchQueries = queries
        },
        totalResults = allJobs.Count,
        jobs = allJobs
    });
});

// ── Auth endpoints ──────────────────────────────────────────────────────────

app.MapPost("/api/auth/register", async (
    [FromServices] AuthService auth,
    [FromBody] RegisterRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password)
        || string.IsNullOrWhiteSpace(req.FirstName) || string.IsNullOrWhiteSpace(req.LastName))
        return Results.BadRequest(new { error = "All fields are required." });

    try
    {
        var user = await auth.RegisterAsync(req.Email, req.Password, req.FirstName, req.LastName);
        return Results.Ok(new
        {
            requiresEmailVerification = true,
            message = "Verification code sent.",
            user = new { user.Id, user.Email, user.FirstName, user.LastName, user.EmailVerified }
        });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] Register failed: {ex.Message}");
        return Results.Problem(detail: ex.Message, statusCode: 500, title: "Registration error");
    }
});

app.MapPost("/api/auth/verify-email", async (
    [FromServices] AuthService auth,
    [FromBody] VerifyEmailRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Code))
        return Results.BadRequest(new { error = "Email and verification code are required." });

    try
    {
        var (token, user) = await auth.VerifyEmailAsync(req.Email, req.Code.Trim());
        return Results.Ok(new
        {
            token,
            user = new { user.Id, user.Email, user.FirstName, user.LastName, user.EmailVerified }
        });
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] Email verification failed: {ex.Message}");
        return Results.Problem(detail: ex.Message, statusCode: 500, title: "Email verification error");
    }
});

app.MapPost("/api/auth/resend-verification", async (
    [FromServices] AuthService auth,
    [FromBody] ResendVerificationRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Email))
        return Results.BadRequest(new { error = "Email is required." });

    try
    {
        await auth.ResendVerificationCodeAsync(req.Email);
        return Results.Ok(new { message = "Verification code sent." });
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] Resend verification failed: {ex.Message}");
        return Results.Problem(detail: ex.Message, statusCode: 500, title: "Resend verification error");
    }
});

app.MapPost("/api/auth/login", async (
    [FromServices] AuthService auth,
    [FromBody] LoginRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest(new { error = "Email and password are required." });

    try
    {
        var (token, user) = await auth.LoginAsync(req.Email, req.Password);
        return Results.Ok(new
        {
            token,
            user = new { user.Id, user.Email, user.FirstName, user.LastName, user.EmailVerified }
        });
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message, requiresEmailVerification = true });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] Login failed: {ex.Message}");
        return Results.Problem(detail: ex.Message, statusCode: 500, title: "Login error");
    }
});

app.MapGet("/api/auth/me", async (
    [FromServices] AuthService auth,
    HttpContext ctx) =>
{
    var header = ctx.Request.Headers.Authorization.FirstOrDefault();
    if (header == null || !header.StartsWith("Bearer "))
        return Results.Unauthorized();

    var token = header["Bearer ".Length..];
    var user = await auth.ValidateTokenAsync(token);
    if (user == null) return Results.Unauthorized();

    return Results.Ok(new { user.Id, user.Email, user.FirstName, user.LastName, user.EmailVerified });
});

app.Run();

record Analyze(string userId, string resumeText);
record AgentChatRequest(string UserId, string Message, string? ConversationId);
record RegisterRequest(string Email, string Password, string FirstName, string LastName);
record VerifyEmailRequest(string Email, string Code);
record ResendVerificationRequest(string Email);
record LoginRequest(string Email, string Password);
