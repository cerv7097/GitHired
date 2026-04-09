using System.Text.Json;
using CareerCoach.Agent.Tools;
using CareerCoach.Services;

namespace CareerCoach.Agent;

/// <summary>
/// Main AI agent that coordinates tools and manages conversations for career coaching
/// </summary>
public class CareerCoachAgent
{
    private readonly GradientClient _llm;
    private readonly ToolRegistry _toolRegistry;
    private readonly Dictionary<string, List<ConversationMessage>> _conversations = new();
    private const int MaxIterations = 5; // Prevent infinite loops

    public CareerCoachAgent(GradientClient llm, Db db, JobAggregatorService aggregator)
    {
        _llm = llm;
        _toolRegistry = new ToolRegistry();

        // Register all available tools
        RegisterTools(db, aggregator);
    }

    private void RegisterTools(Db db, JobAggregatorService aggregator)
    {
        _toolRegistry.RegisterTool(new AnalyzeATSTool(_llm));
        _toolRegistry.RegisterTool(new ImproveResumeTool(_llm));
        _toolRegistry.RegisterTool(new SearchJobsTool(aggregator));
        _toolRegistry.RegisterTool(new GetCareerPathTool(_llm));
        _toolRegistry.RegisterTool(new GenerateAssessmentTool(_llm));
        _toolRegistry.RegisterTool(new GetUserProfileTool(db));
        _toolRegistry.RegisterTool(new RecommendJobsTool(db, aggregator));
    }

    /// <summary>
    /// Process a user message and return the agent's response
    /// </summary>
    public async Task<AgentResponse> ProcessMessageAsync(string conversationId, string userId, string userMessage)
    {
        try
        {
            return await ProcessMessageInternalAsync(conversationId, userId, userMessage);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Agent processing failed: {ex.Message}");
            return new AgentResponse
            {
                Message = $"I hit an internal error while processing your request. Please try again in a moment. Details: {ex.Message}",
                ToolsUsed = new List<ToolExecution>(),
                ConversationId = conversationId
            };
        }
    }

    private async Task<AgentResponse> ProcessMessageInternalAsync(string conversationId, string userId, string userMessage)
    {
        // Initialize or retrieve conversation history
        if (!_conversations.ContainsKey(conversationId))
        {
            _conversations[conversationId] = new List<ConversationMessage>
            {
                new ConversationMessage
                {
                    Role = "system",
                    Content = GetSystemPrompt(userId)
                }
            };
        }

        var messages = _conversations[conversationId];

        // Add user message
        messages.Add(new ConversationMessage
        {
            Role = "user",
            Content = userMessage
        });

        // Agent loop: continue until we get a final answer (no tool calls)
        var iterations = 0;
        var toolExecutions = new List<ToolExecution>();

        while (iterations < MaxIterations)
        {
            iterations++;

            ChatCompletionResponse response;
            try
            {
                // Call LLM with available tools
                response = await _llm.ChatWithToolsAsync(
                    messages,
                    _toolRegistry.GetToolDefinitions(),
                    temperature: 0.7
                );
            }
            catch (HttpRequestException ex)
            {
                // Network/auth failures from the LLM should not crash the API
                Console.WriteLine($"[ERROR] LLM request failed: {ex.Message}");
                return BuildLLMErrorResponse(conversationId, toolExecutions, ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Unexpected agent failure: {ex.Message}");
                return BuildLLMErrorResponse(conversationId, toolExecutions, ex.Message);
            }

            var assistantMessage = response.Choices.FirstOrDefault()?.Message;
            if (assistantMessage == null)
            {
                break;
            }

            // Add assistant message to history
            messages.Add(assistantMessage);

            // Check if assistant wants to call tools
            if (assistantMessage.ToolCalls != null && assistantMessage.ToolCalls.Any())
            {
                var toolResults = await Task.WhenAll(
                    assistantMessage.ToolCalls.Select(async toolCall =>
                    {
                        var tool = _toolRegistry.GetTool(toolCall.Function.Name);
                        if (tool == null)
                        {
                            var missingToolResult = JsonSerializer.Serialize(new { error = "Tool not found" });
                            return new
                            {
                                Execution = (ToolExecution?)null,
                                Message = new ConversationMessage
                                {
                                    Role = "tool",
                                    ToolCallId = toolCall.Id,
                                    Name = toolCall.Function.Name,
                                    Content = missingToolResult
                                }
                            };
                        }

                        var result = await tool.ExecuteAsync(toolCall.Function.Arguments);
                        return new
                        {
                            Execution = (ToolExecution?)new ToolExecution
                            {
                                ToolName = toolCall.Function.Name,
                                Arguments = toolCall.Function.Arguments,
                                Result = result
                            },
                            Message = new ConversationMessage
                            {
                                Role = "tool",
                                ToolCallId = toolCall.Id,
                                Name = toolCall.Function.Name,
                                Content = result
                            }
                        };
                    })
                );

                toolExecutions.AddRange(toolResults
                    .Where(r => r.Execution != null)
                    .Select(r => r.Execution!));
                messages.AddRange(toolResults.Select(r => r.Message));

                // Continue loop to let assistant process tool results
                continue;
            }

            // No tool calls, we have a final response
            return new AgentResponse
            {
                Message = assistantMessage.Content ?? "I apologize, I couldn't generate a response.",
                ToolsUsed = toolExecutions,
                ConversationId = conversationId
            };
        }

        // Max iterations reached
        return new AgentResponse
        {
            Message = "I've processed your request but need to pause here. How else can I help you?",
            ToolsUsed = toolExecutions,
            ConversationId = conversationId
        };
    }

    private AgentResponse BuildLLMErrorResponse(string conversationId, List<ToolExecution> toolExecutions, string errorMessage)
    {
        var hint = "Please verify GRADIENT_API_KEY is set on the API server and that it can reach the internet.";
        return new AgentResponse
        {
            Message = $"I ran into a problem reaching the AI model. {hint} Details: {errorMessage}",
            ToolsUsed = toolExecutions,
            ConversationId = conversationId
        };
    }

    /// <summary>
    /// Get the system prompt that defines the agent's behavior
    /// </summary>
    private string GetSystemPrompt(string userId)
    {
        return $@"You are an AI Career Coach helping professionals advance their careers. You have access to the user's profile in the database.

IMPORTANT BEHAVIOR RULES:
- Whenever the user asks about jobs, career paths, role alignment, skill gaps, or any personalized career question, ALWAYS call get_user_profile first to retrieve their stored resume data, skills, roles, education, and experience.
- If the user says anything like ""based on my experience"", ""based on my skills"", ""what roles fit me"", ""what jobs match my profile"", or similar — call get_user_profile immediately before responding.
- After get_user_profile, use the returned skills, roles, education, and resume_text to give a fully personalized response. Never give generic advice when you have their actual profile data.
- When recommending jobs, use the recommend_jobs tool which automatically uses their stored profile.
- When asked to analyze a resume that is provided in the message, use analyze_ats_compatibility and improve_resume tools.

Your capabilities:
- Analyze resumes for ATS compatibility and improvement (use tools: analyze_ats_compatibility, improve_resume)
- Recommend jobs personalized to the user's profile (use tool: recommend_jobs)
- Answer career questions using the user's actual experience (use tool: get_user_profile first)
- Create personalized career development paths (use tool: get_career_path)
- Generate skills assessments tailored to the user (use tool: generate_assessment)
- Search for specific jobs (use tool: search_jobs)

Guidelines:
1. Always be encouraging and specific — generic advice is unhelpful
2. Reference the user's actual skills, roles, and experience in your responses
3. When the user's profile has a resume_text, use it to give grounded, specific advice
4. Tailor all advice to the user's experience level and background
5. Pass parser_context to resume analysis tools when it is provided in the message
6. When parser diagnostics indicate extraction issues, separate those from genuine resume quality issues

Current user ID: {userId}";
    }

    /// <summary>
    /// Clear conversation history for a specific conversation
    /// </summary>
    public void ClearConversation(string conversationId)
    {
        _conversations.Remove(conversationId);
    }

    /// <summary>
    /// Get conversation history
    /// </summary>
    public List<ConversationMessage>? GetConversationHistory(string conversationId)
    {
        return _conversations.GetValueOrDefault(conversationId);
    }
}

/// <summary>
/// Response from the agent
/// </summary>
public class AgentResponse
{
    public string Message { get; set; } = "";
    public List<ToolExecution> ToolsUsed { get; set; } = new();
    public string ConversationId { get; set; } = "";
}

/// <summary>
/// Record of a tool execution
/// </summary>
public class ToolExecution
{
    public string ToolName { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string Result { get; set; } = "";
}
