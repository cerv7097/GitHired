# 🚀 AI Career Coach - Intelligent Job & Career Readiness Platform

An AI-powered career development platform featuring a **true AI agent** that autonomously helps users with job searching, resume optimization, career planning, and skills assessment.

## ✨ Key Features

### 🤖 AI Agent Capabilities
- **Autonomous Tool Selection** - Agent decides which tools to use based on user needs
- **Multi-turn Conversations** - Maintains context across conversation history
- **Complex Workflow Execution** - Chains multiple tools to accomplish sophisticated tasks
- **Personalized Recommendations** - Tailors advice based on user profile and goals

### 🎯 Core Functions
1. **Resume Analysis & ATS Checking**
   - Parse and analyze resumes
   - Check ATS (Applicant Tracking System) compatibility
   - Provide specific improvement recommendations
   - Score resume quality (0-100)

2. **Job Recommendations**
   - Search jobs based on skills, experience, location
   - Match user profile to opportunities
   - Show match scores and relevance
   - Filter by industry and role

3. **Career Path Planning**
   - Generate personalized career roadmaps
   - Identify skill gaps
   - Recommend courses and certifications
   - Create timeline with milestones
   - Suggest portfolio projects

4. **Skills Assessment**
   - Generate custom assessments for any role/industry
   - Multiple question types (multiple choice, short answer, coding)
   - Adjustable difficulty levels
   - Automated scoring (planned)

5. **Mock Interview Practice**
   - AI-powered interview coaching
   - Industry-specific questions
   - Real-time feedback (planned)
   - Multi-turn conversation practice

## 🏗️ Architecture

```
career-coach/
├── api/                          # C# ASP.NET Core Backend
│   ├── Agent/                    # AI Agent Framework
│   │   ├── AgentTool.cs          # Base class for tools
│   │   ├── ToolRegistry.cs       # Tool management
│   │   ├── ConversationMessage.cs # Conversation state
│   │   ├── CareerCoachAgent.cs   # Main agent orchestrator
│   │   └── Tools/                # Individual tools
│   │       ├── AnalyzeATSTool.cs
│   │       ├── SearchJobsTool.cs
│   │       ├── GetCareerPathTool.cs
│   │       ├── GenerateAssessmentTool.cs
│   │       └── GetUserProfileTool.cs
│   ├── GradientClient.cs         # LLM client with function calling
│   ├── Db.cs                     # Database layer
│   └── Program.cs                # API endpoints
│
├── web/                          # React + TypeScript Frontend
│   └── src/
│       ├── AgentChat.tsx         # AI Agent chat interface
│       ├── Resume.tsx            # Resume analyzer
│       └── Interview.tsx         # Mock interview
│
└── AI_AGENT_GUIDE.md            # Complete agent documentation
```

## 🚀 Quick Start

### Prerequisites
- .NET 9.0 SDK
- Node.js 18+
- PostgreSQL (for production)
- DigitalOcean Gradient API key

### Backend Setup

1. **Configure environment variables:**
```bash
cd api
# Create .env or use user secrets
dotnet user-secrets set "GRADIENT_API_KEY" "your-api-key"
dotnet user-secrets set "PGHOST" "your-db-host"
dotnet user-secrets set "PGPORT" "5432"
dotnet user-secrets set "PGDATABASE" "career_coach"
dotnet user-secrets set "PGUSER" "your-user"
dotnet user-secrets set "PGPASSWORD" "your-password"
```

2. **Run the API:**
```bash
dotnet run
# API will be available at http://localhost:5000
```

### Frontend Setup

1. **Install dependencies:**
```bash
cd web
npm install
```

2. **Run the development server:**
```bash
npm run dev
# Frontend will be available at http://localhost:5173
```

## 📡 API Endpoints

### Agent Endpoints

#### POST `/api/agent/chat`
Main endpoint for conversing with the AI agent.

**Request:**
```json
{
  "userId": "user-123",
  "message": "Find me senior developer jobs in fintech",
  "conversationId": "optional-conv-id"
}
```

**Response:**
```json
{
  "message": "I found 3 senior developer positions in fintech...",
  "toolsUsed": [
    {
      "toolName": "get_user_profile",
      "arguments": "...",
      "result": "..."
    },
    {
      "toolName": "search_jobs",
      "arguments": "...",
      "result": "..."
    }
  ],
  "conversationId": "conv-abc-123"
}
```

#### GET `/api/agent/conversation/{conversationId}`
Retrieve conversation history.

#### DELETE `/api/agent/conversation/{conversationId}`
Clear conversation history.

### Legacy Endpoints

#### POST `/api/resume/analyze`
Direct resume analysis (legacy, prefer using agent).

#### POST `/api/mock-interview`
Direct interview question generation (legacy, prefer using agent).

## 🛠️ Available AI Tools

The agent can autonomously use these tools:

| Tool | Purpose | Parameters |
|------|---------|------------|
| `analyze_ats_compatibility` | Analyze resume ATS compatibility | `resume_text` |
| `search_jobs` | Find matching job opportunities | `skills`, `experience_level`, `industry`, `location` |
| `get_career_path` | Generate career development plan | `current_role`, `target_role`, `current_skills`, `industry` |
| `generate_assessment` | Create skills assessment | `industry`, `role`, `difficulty`, `num_questions` |
| `get_user_profile` | Retrieve user information | `user_id` |

See [AI_AGENT_GUIDE.md](./AI_AGENT_GUIDE.md) for detailed documentation.

## 💬 Example Interactions

### Resume Analysis
```
User: "Can you check if my resume is ATS-friendly?"
Agent: [Calls analyze_ats_compatibility]
       "Your resume scores 82/100. Here are the issues I found..."
```

### Job Search
```
User: "I want to find remote senior developer jobs"
Agent: [Calls get_user_profile → search_jobs]
       "Based on your C#, React, and SQL skills, I found 5 positions..."
```

### Career Planning
```
User: "How do I become a tech lead?"
Agent: [Calls get_user_profile → get_career_path]
       "Here's your 18-month roadmap to tech lead..."
```

## 🔮 Planned Features

### Phase 1 (Current)
- ✅ AI Agent Framework
- ✅ Core tools (ATS, job search, career path, assessments)
- ✅ Conversation memory
- ✅ React chat interface

### Phase 2 (Next)
- 🔲 User authentication (JWT)
- 🔲 Database schema for users, resumes, assessments
- 🔲 File upload for resumes (PDF/DOCX)
- 🔲 Industry selection system
- 🔲 Persistent conversation storage

### Phase 3 (Future)
- 🔲 Real job API integration (LinkedIn, Indeed)
- 🔲 Assessment auto-grading
- 🔲 Multi-agent specialization
- 🔲 RAG for industry knowledge
- 🔲 Voice-enabled mock interviews
- 🔲 AI resume builder
- 🔲 Email notifications for job matches

## 🧪 Testing

### Test the Agent

**Using curl:**
```bash
curl -X POST http://localhost:5000/api/agent/chat \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "test-user",
    "message": "What jobs would you recommend for someone with React and Node.js skills?"
  }'
```

**Using the web interface:**
1. Navigate to http://localhost:5173
2. Try example prompts like:
   - "Find me jobs matching my skills"
   - "Check my resume for ATS compatibility"
   - "Create a career path to senior engineer"

### Build & Test
```bash
# Backend
cd api
dotnet build
dotnet test  # (when tests are added)

# Frontend
cd web
npm run build
npm run lint
```

## 🤝 Contributing

This is a project to transform a simple LLM app into a full AI agent platform. Key areas for contribution:

1. **Additional Tools** - Add more agent capabilities
2. **Database Integration** - Connect real user profiles and data
3. **Job API Integration** - Connect to real job boards
4. **Frontend Enhancement** - Improve UI/UX
5. **Testing** - Add comprehensive tests

## 📝 License

MIT License - feel free to use this as a foundation for your own AI agent projects!

## 🙏 Acknowledgments

- Built with [DigitalOcean Gradient](https://www.digitalocean.com/products/ai-ml) (Llama 3)
- Frontend: React + TypeScript + Vite
- Backend: ASP.NET Core 9.0 + C#
- AI Agent inspired by OpenAI function calling patterns

---

**What makes this an AI Agent?** Unlike simple chatbots, this system:
- ✅ Uses tools/functions autonomously
- ✅ Maintains conversation context
- ✅ Breaks down complex tasks
- ✅ Chains multiple operations
- ✅ Makes decisions based on goals

See [AI_AGENT_GUIDE.md](./AI_AGENT_GUIDE.md) for the complete technical breakdown!
