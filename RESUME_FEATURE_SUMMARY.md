# Resume Feature Summary

## Scope

The resume feature is one of the main implemented workflows in the project. It lets a signed-in user upload a PDF or DOCX resume, parses the file server-side, calculates a parser-derived ATS score, and then runs agent-based analysis for ATS compatibility and improvement suggestions.

## Current User Flow

1. User uploads a `.pdf` or `.docx` file from the dashboard.
2. The API validates file presence, type, and the 5 MB size limit.
3. `ResumeParser` extracts text and basic structure signals.
4. The API creates a new agent conversation.
5. The agent analyzes the resume and returns guidance.
6. The frontend displays parse metadata, ATS score, and tool outputs.

## Implemented Pieces

### Backend

- `api/Services/ResumeParser.cs`
- `api/Agent/Tools/AnalyzeATSTool.cs`
- `api/Agent/Tools/ImproveResumeTool.cs`
- `api/Program.cs` upload endpoint

### Frontend

- `web/src/ResumeUpload.tsx`
- dashboard ATS score sync in `web/src/App.tsx`

## What The Feature Returns

The upload endpoint returns two sections:

- `parse_result`
- `agent_analysis`

`parse_result` currently includes:

- success flag
- word count
- character count
- contact info detection
- section detection
- detected section names
- file name and file type
- parser ATS score

`agent_analysis` currently includes:

- synthesized response message
- tool execution records
- conversation id

## Supported Formats

- PDF
- DOCX

Not supported:

- DOC
- TXT
- RTF
- image-only resumes without extractable text

## Current Constraints

- max file size: 5 MB
- upload processing is synchronous
- text passed to the agent is truncated after 8000 characters
- files are processed in memory

## Product Positioning

This feature is about resume readiness and improvement guidance.

## Known Implementation Notes

- `ResumeUpload.tsx` tries `5001`, then `5298`, then `5000` for local upload requests.
- The rest of the frontend is primarily pinned to `http://localhost:5001`.
- Parser ATS score and tool-derived ATS score may differ because they come from different stages of analysis.
