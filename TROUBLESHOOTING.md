# Troubleshooting

## API Does Not Start

Check:

- `.NET 9` is installed
- required environment variables are set
- PostgreSQL is reachable if you are using auth flows

Run:

```bash
cd api
dotnet run
```

Expected local API URL:

`http://localhost:5001`

## Frontend Cannot Reach The API

Most frontend components expect the API on `http://localhost:5001`.

Check:

- the API is actually running on `5001`
- another process is not occupying that port
- the frontend was started with `npm run dev`

Useful commands:

```bash
lsof -i :5001
curl http://localhost:5001/api/health
```

## Resume Upload Fails With "Cannot connect to API"

`ResumeUpload.tsx` tries these ports:

- `5001`
- `5298`
- `5000`

If upload fails:

1. confirm the API is running
2. confirm one of those ports is listening
3. prefer aligning the backend to `5001`, since that is the main app default

## Resume Upload Rejects The File

Expected causes:

- wrong file type
- file larger than 5 MB
- invalid or unreadable PDF/DOCX

Supported formats:

- `.pdf`
- `.docx`

Not supported:

- `.doc`
- `.txt`
- `.rtf`

## Agent Chat Returns A Model Error

The agent depends on the model client being able to reach the external AI service.

Check:

- `GRADIENT_API_KEY` is set for the API process
- the machine has network access
- the API logs show whether the failure is auth-related or network-related

## Login Or Registration Fails

Check:

- PostgreSQL connection settings are valid
- the database is reachable
- the app can create and read auth records

Relevant variables:

- `PGHOST`
- `PGPORT`
- `PGDATABASE`
- `PGUSER`
- `PGPASSWORD`

## Important Development Caveat

`Db.EnsureAuthTablesAsync()` currently drops and recreates auth tables on startup. If accounts appear to disappear after restarting the API, that is the reason.

This behavior should be treated as a development-only shortcut, not production behavior.

## Job Search Returns No Results

Check:

- `JSEARCH_API_KEY` is set
- `ADZUNA_APP_ID` and `ADZUNA_APP_KEY` are set
- external job providers are reachable

The aggregator tolerates single-source failures, so partial results are possible even when one provider is down.

## Port 5000 Conflicts On macOS

If you see unexpected behavior on `5000`, prefer `5001`.

Alternative:

1. Open System Settings
2. Go to `General`
3. Open `AirDrop & Handoff`
4. Disable `AirPlay Receiver`
