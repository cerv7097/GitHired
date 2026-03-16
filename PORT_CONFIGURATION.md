# Port Configuration

## Current Local Defaults

- Frontend: `http://localhost:5173`
- API HTTP: `http://localhost:5001`
- API HTTPS: `https://localhost:7114`

These values come from the current project configuration, especially `api/Properties/launchSettings.json`.

## Why Port 5001 Is Used

The API is configured for `5001` to avoid the common macOS conflict on `5000` with AirPlay Receiver and related services.

## Backend Startup

```bash
cd /Users/lanettacervantes/career-coach/api
dotnet run
```

The default development profile should bind to `http://localhost:5001`.

## Frontend Expectations

Most frontend API calls are currently hardcoded to `http://localhost:5001`, including:

- auth
- agent chat
- jobs

Resume upload has its own fallback logic and tries these ports in order:

1. `5001`
2. `5298`
3. `5000`

That fallback exists in `web/src/ResumeUpload.tsx`.

## Health Check

```bash
curl http://localhost:5001/api/health
```

Expected shape:

```json
{
  "status": "healthy",
  "services": {
    "llm": "initialized",
    "parser": "initialized",
    "apiKeyConfigured": true
  }
}
```

## If You Need A Different Port

1. Update `api/Properties/launchSettings.json`.
2. Update hardcoded frontend URLs in `web/src/`.
3. Keep `ResumeUpload.tsx` fallback logic aligned with the rest of the app.

## Recommendation

If more port changes are expected, move the frontend to a single shared API base configuration instead of keeping separate hardcoded values in individual components.
