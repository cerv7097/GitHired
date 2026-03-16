# AI Agent Guide

## Overview

The API includes a tool-calling career coach agent implemented in [`api/Agent/CareerCoachAgent.cs`](api/Agent/CareerCoachAgent.cs). The agent maintains in-memory conversation state, decides when to call tools, executes those tools, and returns a final synthesized response.

This guide reflects the current implementation.

## Agent Responsibilities

The current agent is designed to:

- analyze resume text for ATS compatibility
- suggest resume improvements
- search for jobs through the job aggregation layer
- generate career path guidance
- generate assessments
- read a stored user profile from the database

## Architecture

```text
User message
  -> CareerCoachAgent
  -> GradientClient.ChatWithToolsAsync(...)
  -> tool selection from ToolRegistry
  -> tool execution
  -> final assistant response
```

Key files:

- `api/Agent/CareerCoachAgent.cs`
- `api/Agent/AgentTool.cs`
- `api/Agent/ToolRegistry.cs`
- `api/Agent/ConversationMessage.cs`
- `api/GradientClient.cs`

## Registered Tools

### `analyze_ats_compatibility`

- Purpose: Evaluate ATS readiness from resume text
- Backing file: `api/Agent/Tools/AnalyzeATSTool.cs`
- Typical use: pasted resume text or parsed upload content

### `improve_resume`

- Purpose: Generate concrete resume improvement suggestions
- Backing file: `api/Agent/Tools/ImproveResumeTool.cs`
- Typical use: chained after ATS analysis

### `search_jobs`

- Purpose: Search live job listings through the aggregator
- Backing file: `api/Agent/Tools/SearchJobsTool.cs`
- Typical use: role, skill, or preference-driven job search

### `get_career_path`

- Purpose: Build a roadmap from current role to target role
- Backing file: `api/Agent/Tools/GetCareerPathTool.cs`

### `generate_assessment`

- Purpose: Generate assessment content for a role or industry
- Backing file: `api/Agent/Tools/GenerateAssessmentTool.cs`

### `get_user_profile`

- Purpose: Retrieve a user profile from the database
- Backing file: `api/Agent/Tools/GetUserProfileTool.cs`

## Request Flow

1. A user sends a message to `POST /api/agent/chat`.
2. The API creates or resumes an in-memory conversation.
3. The agent sends the conversation plus tool schemas to the model.
4. If the model requests tools, the agent executes them and appends tool outputs to history.
5. The loop continues until the assistant returns a normal message or the iteration cap is hit.

The current loop limit is `5`.

## Conversation Storage

- Conversation history is stored in memory only.
- `GET /api/agent/conversation/{conversationId}` returns the in-memory message list.
- `DELETE /api/agent/conversation/{conversationId}` clears that in-memory history.
- A process restart clears conversation history.

## Error Handling

Current behavior includes:

- graceful fallback when the LLM call fails
- tool execution capture in the response payload
- user-facing message when the model cannot be reached

The agent currently advises checking `GRADIENT_API_KEY` and internet connectivity when model access fails.

## API Contract

### `POST /api/agent/chat`

Request:

```json
{
  "userId": "user-123",
  "message": "Find remote .NET jobs",
  "conversationId": "optional-existing-id"
}
```

Response shape:

```json
{
  "message": "Here are several roles that match...",
  "toolsUsed": [
    {
      "toolName": "search_jobs",
      "arguments": "{...}",
      "result": "{...}"
    }
  ],
  "conversationId": "generated-or-existing-id"
}
```

## Resume Upload Integration

`POST /api/resume/upload` uses the agent as part of its workflow:

1. parse the uploaded PDF or DOCX
2. create an agent conversation
3. ask the agent to analyze ATS compatibility and provide improvement guidance
4. return parser metadata plus agent output

This is the most complete example of multi-tool orchestration in the current product.

## Limitations

- Conversation state is not persisted.
- Tool results depend on third-party services and configured API keys.
- The agent prompt still contains some broad guidance inherited from earlier iterations.
## Adding a New Tool

1. Create a new tool class under `api/Agent/Tools/`.
2. Inherit from `AgentTool`.
3. Define `Name`, `Description`, `ParameterSchema`, and `ExecuteAsync`.
4. Register the tool in `RegisterTools()` inside `CareerCoachAgent`.

Minimal shape:

```csharp
public class MyTool : AgentTool
{
    public override string Name => "my_tool";
    public override string Description => "Describe the job clearly.";
    public override object ParameterSchema => new { };
    public override Task<string> ExecuteAsync(string parameters) => Task.FromResult("{}");
}
```

## Recommended Next Cleanup

- move hardcoded frontend API URLs to a shared config
- persist conversation history if long-lived chat matters
- align the system prompt with the actual supported product scope
