# Port Configuration

## Issue: macOS Control Center uses port 5000

macOS Control Center (AirPlay Receiver) by default uses port 5000, which conflicts with our API.

## Solution: Use Port 5001

The API now runs on **port 5001** instead of port 5000.

## How to Start the API

```bash
cd /Users/lanettacervantes/career-coach/api
export GRADIENT_API_KEY="your-api-key-here"
dotnet run --urls "http://localhost:5001"
```

Or use the stored user secrets:

```bash
cd /Users/lanettacervantes/career-coach/api
GRADIENT_API_KEY=$(dotnet user-secrets list | grep GRADIENT_API_KEY | cut -d'=' -f2- | xargs) dotnet run --urls "http://localhost:5001"
```

## Frontend Configuration

- **Web App**: http://localhost:5173
- **API**: http://localhost:5001

Both `AgentChat.tsx` and `ResumeUpload.tsx` have been updated to use port 5001.

## Verify API is Running

```bash
curl http://localhost:5001/api/health
```

Should return:
```json
{
  "status": "healthy",
  "services": {
    "llm": "initialized",
    "parser": "initialized",
    "apiKeyConfigured": true
  },
  "message": "All services ready"
}
```

## Alternative: Disable AirPlay Receiver

If you want to use port 5000, you can disable AirPlay Receiver in System Settings:
1. System Settings → General → AirDrop & Handoff
2. Turn off "AirPlay Receiver"
