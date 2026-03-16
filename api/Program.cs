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
b.Services.AddSingleton<AuthService>();
b.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin()));

var app = b.Build();
app.UseCors();

// Ensure auth tables exist on startup (non-fatal if DB is temporarily unavailable)
var db = app.Services.GetRequiredService<Db>();
try { await db.EnsureAuthTablesAsync(); }
catch (Exception ex) { Console.WriteLine($"[WARN] Could not ensure auth tables: {ex.Message}"); }

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

    var raw = await llm.ChatAsync(sys, $"Resume:\n{text}\nReturn only JSON.", model: "llama3-8b-instruct", maxTokens: 300);

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
        // Parse the resume file
        bufferedStream.Position = 0;
        var parseResult = await parser.ParseResumeAsync(bufferedStream, normalizedFileName);

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
        requestMessage += $"\n\nResume text:\n{resumeText}";

        // Let the agent process it (it will automatically use analyze_ats_compatibility and improve_resume tools)
        var agentResponse = await agent.ProcessMessageAsync(conversationId, userId, requestMessage);

        return Results.Ok(new
        {
            parse_result = new
            {
                success = parseResult.Success,
                word_count = parseResult.WordCount,
                character_count = parseResult.CharacterCount,
                has_contact_info = parseResult.HasContactInfo,
                has_sections = parseResult.HasSections,
                detected_sections = parseResult.DetectedSections,
                file_name = parseResult.FileName,
                file_type = parseResult.FileType,
                ats_score = parseResult.AtsScore
            },
            agent_analysis = new
            {
                message = agentResponse.Message,
                tools_used = agentResponse.ToolsUsed,
                conversation_id = agentResponse.ConversationId
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
    string? query,
    string? location,
    bool remoteOnly = false,
    string? experienceLevel = null,
    string? employmentType = null) =>
{
    var q = string.IsNullOrWhiteSpace(query) ? "software engineer" : query.Trim();
    var loc = location?.Trim().Equals("remote", StringComparison.OrdinalIgnoreCase) == true ? null : location?.Trim();

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
        var (token, user) = await auth.RegisterAsync(req.Email, req.Password, req.FirstName, req.LastName);
        return Results.Ok(new { token, user = new { user.Id, user.Email, user.FirstName, user.LastName } });
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

app.MapPost("/api/auth/login", async (
    [FromServices] AuthService auth,
    [FromBody] LoginRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest(new { error = "Email and password are required." });

    try
    {
        var (token, user) = await auth.LoginAsync(req.Email, req.Password);
        return Results.Ok(new { token, user = new { user.Id, user.Email, user.FirstName, user.LastName } });
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
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

    return Results.Ok(new { user.Id, user.Email, user.FirstName, user.LastName });
});

app.Run();

record Analyze(string userId, string resumeText);
record AgentChatRequest(string UserId, string Message, string? ConversationId);
record RegisterRequest(string Email, string Password, string FirstName, string LastName);
record LoginRequest(string Email, string Password);
