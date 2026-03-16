# Resume Upload Guide

## Overview

The resume upload flow is implemented and active. It accepts PDF and DOCX resumes, extracts text server-side, analyzes structure, and sends the content into the AI agent for ATS and improvement guidance.

## Endpoint

`POST /api/resume/upload`

Content type:

`multipart/form-data`

Fields:

- `file` required
- `userId` required
- `targetRole` optional
- `targetIndustry` optional

## Validation Rules

- file must be present
- file must be PDF or DOCX after normalization
- file size must be 5 MB or smaller

## Server Flow

1. The API buffers the upload into memory.
2. File metadata is normalized and validated.
3. `ResumeParser.ParseResumeAsync()` extracts text.
4. The parsed text is truncated to 8000 characters if needed.
5. The API creates an agent message asking for ATS analysis and improvement suggestions.
6. The response combines parser metadata with agent output.

## Response Shape

Example:

```json
{
  "parse_result": {
    "success": true,
    "word_count": 450,
    "character_count": 3200,
    "has_contact_info": true,
    "has_sections": true,
    "detected_sections": ["Experience", "Education", "Skills"],
    "file_name": "resume.pdf",
    "file_type": ".pdf",
    "ats_score": 78
  },
  "agent_analysis": {
    "message": "Your resume is readable and generally well structured, but here are the main areas to improve...",
    "tools_used": [],
    "conversation_id": "..."
  }
}
```

## Frontend Behavior

The upload UI lives in `web/src/ResumeUpload.tsx`.

Current behavior:

- drag and drop or file picker
- optional role and industry targeting
- upload loading state
- parse result display
- ATS score display
- expandable tool output display
- ATS score sync back into the dashboard

## Local Development

### Start the API

```bash
cd api
dotnet run
```

### Start the frontend

```bash
cd web
npm install
npm run dev
```

### Local URLs

- frontend: `http://localhost:5173`
- primary api: `http://localhost:5001`

## Common Failure Modes

### Unsupported file type

Expected message:

`Only PDF and DOCX files are supported`

### File too large

Expected message:

`File size exceeds 5MB limit`

### API not reachable

The upload component attempts:

- `http://localhost:5001`
- `http://localhost:5298`
- `http://localhost:5000`

If all fail, the frontend reports a connection error.

### Parser failure

If extraction fails, the API returns a resume parsing error with details from the parser layer.

## Implementation Notes

- PDF parsing uses `iTextSharp.LGPLv2.Core`
- DOCX parsing uses `DocumentFormat.OpenXml`
- files are not persisted by this endpoint
- conversation history produced by the agent is in memory

## Current Limitations

- complex PDF layouts may still parse imperfectly
- image-heavy resumes are not ideal
- there is no resume version history
- there is no direct export or rewrite workflow
- the feature depends on the AI model being reachable for full analysis
