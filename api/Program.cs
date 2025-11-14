using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using CareerCoach.Agent;
using CareerCoach.Services;

var b = WebApplication.CreateBuilder(args);
b.Configuration.AddEnvironmentVariables();
b.Services.AddSingleton<GradientClient>();
b.Services.AddSingleton<Db>();
b.Services.AddSingleton<CareerCoachAgent>();
b.Services.AddSingleton<ResumeParser>();
b.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin()));

var app = b.Build();
app.UseCors();

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

app.MapPost("/api/mock-interview", async ([FromServices] GradientClient llm, [FromBody] Prompt req) => {
  var sys = "You are a technical interviewer. Ask one question at a time.";
  var raw = await llm.ChatAsync(sys, req.prompt);
  return Results.Text(raw, "application/json");
});

app.MapPost("/api/resume/analyze", async (
    [FromServices] GradientClient llm,
    [FromServices] Db db,
    Analyze r) =>
{
    var sys =
      "You are a resume analyzer. Return ONLY valid JSON with keys: " +
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

    var allowedExtensions = new[] { ".pdf", ".docx" };
    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
    if (!allowedExtensions.Contains(extension))
    {
        return Results.BadRequest(new { error = "Only PDF and DOCX files are supported" });
    }

    try
    {
        // Parse the resume file
        using var stream = file.OpenReadStream();
        var parseResult = await parser.ParseResumeAsync(stream, file.FileName);

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
                file_type = parseResult.FileType
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

app.Run();

record Prompt(string prompt);
record Analyze(string userId, string resumeText);
record AgentChatRequest(string UserId, string Message, string? ConversationId);
