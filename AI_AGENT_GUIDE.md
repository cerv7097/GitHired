# Career Coach AI Agent Framework

## Overview

Your Career Coach application has been successfully transformed into a **true AI agent** with autonomous tool-calling capabilities. The agent can now:

✅ Maintain multi-turn conversations with context
✅ Autonomously decide when to use tools/functions
✅ Execute complex multi-step workflows
✅ Provide personalized career guidance

## What Makes This an AI Agent?

Unlike the previous implementation which only made single LLM calls, this system exhibits true agent behavior:

1. **Autonomy**: The agent decides which tools to use based on the user's request
2. **Tool Use**: Can invoke multiple functions (search jobs, analyze resumes, etc.)
3. **Memory**: Maintains conversation history across multiple interactions
4. **Goal-Oriented**: Breaks down complex requests into steps using available tools
5. **Iterative Problem-Solving**: Can call multiple tools in sequence to accomplish tasks

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                   CareerCoachAgent                      │
│  (Orchestrates conversations and tool execution)        │
└──────────────┬──────────────────────────┬───────────────┘
               │                          │
        ┌──────▼──────┐          ┌────────▼─────────┐
        │   LLM       │          │  ToolRegistry    │
        │ (Gradient)  │          │ (Available Tools) │
        └─────────────┘          └──────────────────┘
                                          │
                    ┌─────────────────────┼────────────────────┐
                    │                     │                    │
            ┌───────▼────────┐   ┌────────▼──────┐   ┌───────▼────────┐
            │ AnalyzeATSTool │   │ SearchJobsTool│   │GetCareerPathTool│
            └────────────────┘   └───────────────┘   └────────────────┘
                    │                     │                    │
            ┌───────▼────────┐   ┌────────▼──────────┐
            │GenerateAssessment│ │GetUserProfileTool │
            └─────────────────┘  └───────────────────┘
```

## Available Tools

### 1. **analyze_ats_compatibility**
Analyzes resumes for ATS (Applicant Tracking System) compatibility.

**Parameters:**
- `resume_text` (string): Full resume text

**Returns:**
- ATS score (0-100)
- List of issues
- Recommendations
- Missing elements

**Example:**
```json
{
  "score": 85,
  "issues": ["No clear section headers"],
  "recommendations": ["Add EXPERIENCE and EDUCATION sections"],
  "missing_elements": ["LinkedIn URL"]
}
```

### 2. **search_jobs**
Searches for job recommendations based on user profile.

**Parameters:**
- `skills` (array): List of skills
- `experience_level` (string): "entry", "mid", "senior", or "lead"
- `industry` (string): Target industry
- `location` (string): Preferred location or "remote"

**Returns:**
- List of matching jobs with match scores

### 3. **get_career_path**
Generates personalized career development roadmap.

**Parameters:**
- `current_role` (string): Current job title
- `target_role` (string): Desired role
- `current_skills` (array): List of current skills
- `industry` (string): Target industry

**Returns:**
- Skill gaps
- Recommended courses
- Timeline with milestones
- Project ideas
- Networking strategies

### 4. **generate_assessment**
Creates customized skills assessments.

**Parameters:**
- `industry` (string): Industry to assess for
- `role` (string): Specific role
- `difficulty` (string): "entry", "mid", or "senior"
- `num_questions` (integer): Number of questions

**Returns:**
- Assessment with multiple question types

### 5. **get_user_profile**
Retrieves user profile information.

**Parameters:**
- `user_id` (string): User identifier

**Returns:**
- Complete user profile with skills, education, preferences

## API Endpoints

### POST `/api/agent/chat`
Main endpoint for interacting with the AI agent.

**Request:**
```json
{
  "userId": "user-123",
  "message": "I want to transition from junior to senior developer",
  "conversationId": "conv-abc-123" // optional, auto-generated if omitted
}
```

**Response:**
```json
{
  "message": "Based on your profile, here's a career path...",
  "toolsUsed": [
    {
      "toolName": "get_user_profile",
      "arguments": "{\"user_id\":\"user-123\"}",
      "result": "{...}"
    },
    {
      "toolName": "get_career_path",
      "arguments": "{\"current_role\":\"Junior Developer\",...}",
      "result": "{...}"
    }
  ],
  "conversationId": "conv-abc-123"
}
```

### GET `/api/agent/conversation/{conversationId}`
Retrieve conversation history.

### DELETE `/api/agent/conversation/{conversationId}`
Clear conversation history.

## How the Agent Works

### Agent Loop

1. **User sends message** → Agent receives message in context of conversation
2. **Agent analyzes** → LLM determines if tools are needed
3. **Tool execution** → Agent executes requested tools
4. **Tool results** → Results added to conversation context
5. **Agent synthesizes** → LLM processes tool results and generates response
6. **Response returned** → User receives final answer

### Example Flow

**User:** "Find me jobs that match my skills"

```
1. Agent receives message
2. Agent calls: get_user_profile(user-123)
   → Returns: { skills: ["C#", "React", "SQL"], experience_level: "mid", ... }
3. Agent calls: search_jobs(skills=["C#","React","SQL"], experience_level="mid")
   → Returns: { jobs: [{ title: "Mid-Level Developer", ... }] }
4. Agent synthesizes response:
   "Based on your profile, I found 3 jobs that match your skills in C#, React, and SQL..."
```

## Example Interactions

### 1. Resume Analysis
```
User: "Can you check if my resume is ATS-friendly?"
[Agent may first ask for resume, or user can paste it]

Agent uses: analyze_ats_compatibility
Response: "Your resume scores 85/100 for ATS compatibility. Here are improvements..."
```

### 2. Job Search
```
User: "I'm looking for senior developer jobs in fintech"

Agent uses:
  1. get_user_profile (to get skills)
  2. search_jobs (with skills, senior level, fintech industry)
Response: "I found 3 positions that match your profile..."
```

### 3. Career Planning
```
User: "How can I become a lead engineer in 2 years?"

Agent uses:
  1. get_user_profile
  2. get_career_path (current → target role)
Response: "Here's your personalized roadmap: In 3 months..."
```

### 4. Skills Assessment
```
User: "I want to test my skills for a data analyst role"

Agent uses: generate_assessment(role="Data Analyst")
Response: "I've created a 10-question assessment covering SQL, Python, statistics..."
```

## Adding New Tools

To add a new tool:

1. **Create tool class** in `api/Agent/Tools/`
```csharp
public class MyNewTool : AgentTool
{
    public override string Name => "my_tool_name";
    public override string Description => "What this tool does";
    public override object ParameterSchema => new { /* JSON schema */ };
    public override Task<string> ExecuteAsync(string parameters) { /* implementation */ }
}
```

2. **Register in CareerCoachAgent.cs**
```csharp
_toolRegistry.RegisterTool(new MyNewTool());
```

3. **Agent automatically uses it** based on the description!

## Next Steps

### Immediate Enhancements:
1. ✅ AI Agent Framework - **COMPLETE**
2. 🔲 Add user authentication (JWT)
3. 🔲 Implement full database schema for user profiles
4. 🔲 Integrate real job search API (LinkedIn, Indeed)
5. 🔲 Add resume file upload (PDF/DOCX parsing)
6. 🔲 Create frontend UI for agent chat
7. 🔲 Add persistent conversation storage in database
8. 🔲 Implement assessment scoring system

### Advanced Features:
- Multi-agent collaboration (specialized agents for different domains)
- RAG (Retrieval Augmented Generation) for industry-specific knowledge
- Integration with external APIs (LinkedIn, GitHub, etc.)
- Webhook notifications for new job matches
- AI-powered interview preparation with voice
- Resume builder with AI suggestions

## Testing the Agent

You can test the agent by:

1. **Start the API:**
```bash
cd api
dotnet run
```

2. **Send a test request:**
```bash
curl -X POST http://localhost:5000/api/agent/chat \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "user-123",
    "message": "What jobs match my skills?"
  }'
```

3. **The agent will:**
   - Automatically call `get_user_profile`
   - Then call `search_jobs`
   - Synthesize a personalized response

## Key Differences from Before

| Before | After (AI Agent) |
|--------|------------------|
| Single LLM call | Multi-step reasoning with tools |
| No state between requests | Conversation memory |
| Manual tool selection | Autonomous tool selection |
| Simple prompt → response | Complex workflows |
| No function calling | Full function calling support |
| Stateless | Stateful with context |

## Congratulations!

You now have a **fully functional AI agent** that can autonomously help users with their career development. The agent intelligently uses tools, maintains context, and provides personalized guidance—making this a true AI-powered career coaching platform!
