namespace CareerCoach.Agent;

/// <summary>
/// Registry for all available tools that agents can use
/// </summary>
public class ToolRegistry
{
    private readonly Dictionary<string, AgentTool> _tools = new();

    public void RegisterTool(AgentTool tool)
    {
        _tools[tool.Name] = tool;
    }

    public AgentTool? GetTool(string name)
    {
        return _tools.GetValueOrDefault(name);
    }

    public IEnumerable<AgentTool> GetAllTools()
    {
        return _tools.Values;
    }

    public List<ToolDefinition> GetToolDefinitions()
    {
        return _tools.Values.Select(tool => new ToolDefinition
        {
            Type = "function",
            Function = new FunctionDefinition
            {
                Name = tool.Name,
                Description = tool.Description,
                Parameters = tool.ParameterSchema
            }
        }).ToList();
    }
}
