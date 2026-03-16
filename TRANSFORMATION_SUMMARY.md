# Transformation Summary

## What Changed

The project has moved well beyond its earlier single-call LLM prototype. The current codebase is now a multi-surface career-readiness app with:

- authenticated web access
- an AI career coach agent with tool calling
- resume upload and ATS analysis
- live aggregated job search
- guided assessments
- curated learning resources

That is the meaningful transformation reflected in the current repository.

## What The Product Is Now

### Frontend

The active frontend experience is the authenticated React app in `web/src/`:

- `Login.tsx` handles sign-in and registration
- `App.tsx` renders the main dashboard, jobs, assessment, and resources views
- `ResumeUpload.tsx` handles upload and ATS sync into the dashboard
- `AgentChat.tsx` exposes the AI coach
- `Jobs.tsx` searches aggregated job listings
- `Assessment.tsx` provides track-based readiness scoring

### Backend

The API in `api/Program.cs` now exposes:

- health endpoint
- auth endpoints
- agent chat endpoints
- resume upload and legacy resume analysis endpoints
- job search endpoint

### Agent Layer

The agent in `api/Agent/` is the orchestration layer for AI-assisted workflows. It currently wires together:

- ATS analysis
- resume improvement guidance
- job search
- career path generation
- assessment generation
- user profile lookup

## Operational Reality

### Current Local Defaults

- frontend: `http://localhost:5173`
- api: `http://localhost:5001`

### Current Storage Model

- auth relies on PostgreSQL
- agent conversation history is in memory
- resume files are processed in memory during upload

### Current Caveats

- auth table setup is destructive on startup in the current `Db` implementation
- frontend API URLs are mostly hardcoded
- resume upload has fallback port logic that differs from the rest of the frontend
- some older implementation shortcuts still exist and should be cleaned up over time

## Practical Summary

The project should now be described as a career coaching and job-readiness platform rather than a simple agent demo. The implemented value is in resume analysis, guided coaching, jobs, assessments, and authenticated dashboard workflows.
