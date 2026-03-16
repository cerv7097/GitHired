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
                // Execute each tool call
                foreach (var toolCall in assistantMessage.ToolCalls)
                {
                    var tool = _toolRegistry.GetTool(toolCall.Function.Name);
                    if (tool == null)
                    {
                        // Tool not found, add error message
                        messages.Add(new ConversationMessage
                        {
                            Role = "tool",
                            ToolCallId = toolCall.Id,
                            Name = toolCall.Function.Name,
                            Content = JsonSerializer.Serialize(new { error = "Tool not found" })
                        });
                        continue;
                    }

                    // Execute the tool
                    var result = await tool.ExecuteAsync(toolCall.Function.Arguments);

                    // Record tool execution
                    toolExecutions.Add(new ToolExecution
                    {
                        ToolName = toolCall.Function.Name,
                        Arguments = toolCall.Function.Arguments,
                        Result = result
                    });

                    // Add tool result to conversation
                    messages.Add(new ConversationMessage
                    {
                        Role = "tool",
                        ToolCallId = toolCall.Id,
                        Name = toolCall.Function.Name,
                        Content = result
                    });
                }

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
        return $@"You are an AI Career Coach specialized in helping professionals advance their careers.

Your capabilities:
- Analyze resumes for ATS compatibility and provide improvement suggestions
- At the top of the response, provide a score of ATS compatibility from 0 to 100
- Search and recommend jobs based on user skills, experience, and preferences
- Create personalized career development paths
- Generate customized skills assessments
- Offer career advice and industry insights that would strenghten the user's resume

Guidelines:
1. Always be encouraging and supportive
2. Provide specific, actionable advice
3. Use tools to gather data before making recommendations
4. When analyzing resumes or creating career paths, focus on technical skills and competencies
5. Use industry-standard terminology
6. Ask clarifying questions if you need more information
7. Tailor all advice to the user's specific situation

Current user ID: {userId}

When appropriate, use the get_user_profile tool to retrieve the user's information to personalize your responses.";
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
