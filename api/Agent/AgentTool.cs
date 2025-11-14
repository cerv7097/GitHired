using System.Text.Json.Serialization;

namespace CareerCoach.Agent;

/// <summary>
/// Base class for all tools that the AI agent can invoke
/// </summary>
public abstract class AgentTool
{
    /// <summary>
    /// Unique identifier for this tool
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Description of what this tool does (used by LLM to decide when to call it)
    /// </summary>
    public abstract string Description { get; }

    /// <summary>
    /// JSON schema describing the parameters this tool accepts
    /// </summary>
    public abstract object ParameterSchema { get; }

    /// <summary>
    /// Execute the tool with the given parameters
    /// </summary>
    /// <param name="parameters">JSON string containing the parameters</param>
    /// <returns>Result of the tool execution</returns>
    public abstract Task<string> ExecuteAsync(string parameters);
}

/// <summary>
/// Represents a function definition for the LLM
/// </summary>
public class FunctionDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("parameters")]
    public object Parameters { get; set; } = new { };
}

/// <summary>
/// Tool definition in OpenAI function calling format
/// </summary>
public class ToolDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public FunctionDefinition Function { get; set; } = new();
}
