# GitHired by NextWave Insights

GitHired is a career-readiness platform with a React frontend and an ASP.NET Core API. The current product centers on authenticated access, AI-guided career coaching, resume upload and ATS analysis, live job search aggregation, self-assessments, and curated learning resources.

## Current Features

- Account registration, login, and token-based session validation
- AI career coach chat backed by a tool-calling agent
- Resume upload for PDF and DOCX files
- Resume parsing, ATS scoring, and AI-generated improvement guidance
- Live job aggregation across multiple providers
- Guided skills assessments across several career tracks
- Dashboard and resource library in the authenticated web app

## Tech Stack

- Backend: ASP.NET Core 9, C#
- Frontend: React, TypeScript, Vite
- Database: PostgreSQL via `Npgsql`
- AI: Gradient-backed chat and tool calling

## Project Structure

```text
career-coach/
├── api/
│   ├── Agent/
│   │   ├── CareerCoachAgent.cs
│   │   ├── ToolRegistry.cs
│   │   └── Tools/
│   ├── Services/
│   ├── Program.cs
│   └── Properties/launchSettings.json
├── web/
│   ├── src/
│   │   ├── App.tsx
│   │   ├── AgentChat.tsx
│   │   ├── ResumeUpload.tsx
│   │   ├── Jobs.tsx
│   │   ├── Assessment.tsx
│   │   └── Login.tsx
│   └── README.md
├── AI_AGENT_GUIDE.md
├── PORT_CONFIGURATION.md
├── RESUME_FEATURE_SUMMARY.md
├── RESUME_UPLOAD_GUIDE.md
├── TRANSFORMATION_SUMMARY.md
└── TROUBLESHOOTING.md
```

## Prerequisites

- .NET 9 SDK
- Node.js 18+
- PostgreSQL
- `GRADIENT_API_KEY`

For job search, configure the provider keys used by the API. The repository currently expects:

- `JSEARCH_API_KEY`
- `ADZUNA_APP_ID`
- `ADZUNA_APP_KEY`
- `THE_MUSE_API_KEY` optional

For database-backed auth:

- `PGHOST`
- `PGPORT`
- `PGDATABASE`
- `PGUSER`
- `PGPASSWORD`
- `PGSSLmode` optional, defaults to `require`

## Local Development

### Backend

```bash
cd api
dotnet user-secrets set "GRADIENT_API_KEY" "your-key"
dotnet user-secrets set "PGHOST" "your-host"
dotnet user-secrets set "PGPORT" "5432"
dotnet user-secrets set "PGDATABASE" "career_coach"
dotnet user-secrets set "PGUSER" "your-user"
dotnet user-secrets set "PGPASSWORD" "your-password"
dotnet run
```

Default local API URL:

- `http://localhost:5001`

### Frontend

```bash
cd web
npm install
npm run dev
```

Default local frontend URL:

- `http://localhost:5173`

## API Endpoints

### Core

- `GET /` health text response
- `GET /api/health` service readiness summary

### Auth

- `POST /api/auth/register`
- `POST /api/auth/login`
- `GET /api/auth/me`

### Agent

- `POST /api/agent/chat`
- `GET /api/agent/conversation/{conversationId}`
- `DELETE /api/agent/conversation/{conversationId}`

### Resume

- `POST /api/resume/upload`
- `POST /api/resume/analyze`

### Jobs

- `GET /api/jobs/search`

## AI Agent Tools

The agent currently registers these tools:

- `analyze_ats_compatibility`
- `improve_resume`
- `search_jobs`
- `get_career_path`
- `generate_assessment`
- `get_user_profile`

## Current Product Notes

- The authenticated app is the main user flow.
- Resume upload creates an agent conversation and combines parser output with tool-driven analysis.
- Job search uses a fan-out aggregator and deduplicates results across providers.
- Assessments are frontend-guided experiences with local scoring and next-step recommendations.
## Important Implementation Notes

- `api/Properties/launchSettings.json` is configured for port `5001`.
- The frontend currently hardcodes `http://localhost:5001` for most API calls.
- `ResumeUpload.tsx` also falls back to `5298` and `5000` if `5001` is unavailable.
- `Db.EnsureAuthTablesAsync()` currently recreates auth tables on startup. That behavior is suitable for local development only.

## Docs in This Repo

- [AI_AGENT_GUIDE.md](./AI_AGENT_GUIDE.md): agent architecture and tool flow
- [RESUME_UPLOAD_GUIDE.md](./RESUME_UPLOAD_GUIDE.md): upload and parsing behavior
- [RESUME_FEATURE_SUMMARY.md](./RESUME_FEATURE_SUMMARY.md): resume feature scope
- [PORT_CONFIGURATION.md](./PORT_CONFIGURATION.md): local ports and frontend/backend assumptions
- [TRANSFORMATION_SUMMARY.md](./TRANSFORMATION_SUMMARY.md): what changed from the earlier app shape
- [TROUBLESHOOTING.md](./TROUBLESHOOTING.md): local setup and common failures

## Verification

```bash
cd api
dotnet build

cd ../web
npm run build
```
