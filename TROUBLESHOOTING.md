# 🔧 Troubleshooting Guide

## "Failed to Fetch" Error When Uploading Resume

### Problem
When trying to upload a resume, you get an error: "Failed to fetch" or "Cannot connect to API"

### Root Cause
The frontend was configured to connect to `http://localhost:5000`, but:
1. **macOS uses port 5000 for AirPlay/AirTunes by default**
2. Your API is actually running on port **5298** (as configured in `launchSettings.json`)

### Solution ✅ (Already Fixed)

The code has been updated to:
1. **Frontend automatically tries multiple ports** (5298, 5000, 5001)
2. **AgentChat uses correct port** (5298)
3. **Better error messages** showing which ports were tried

### Verify It's Working

**1. Check API is running:**
```bash
curl http://localhost:5298/
# Should return: API up
```

**2. Check upload endpoint:**
```bash
curl http://localhost:5298/api/resume/upload
# Should return 405 Method Not Allowed (since it expects POST)
```

**3. Test the frontend:**
- Open browser to http://localhost:5173
- Open browser console (F12)
- Try uploading a resume
- You should see: "Attempting to upload to http://localhost:5298..."
- Then: "Connected successfully on port 5298"

### Common Issues & Fixes

#### Issue 1: API Not Running
**Symptom:** "Cannot connect to API" after trying all ports

**Fix:**
```bash
cd api
dotnet run
# Look for: "Now listening on: http://localhost:5298"
```

#### Issue 2: Port 5000 Conflict (macOS)
**Symptom:** Port 5000 returns 403 Forbidden or connects to wrong service

**Why:** macOS Monterey+ uses port 5000 for AirPlay Receiver

**Fix:** Use port 5298 instead (already configured)

**Alternative - Disable AirPlay on port 5000:**
```
System Settings → General → AirDrop & Handoff →
Uncheck "AirPlay Receiver"
```

#### Issue 3: CORS Error
**Symptom:** Browser console shows CORS policy error

**Fix:** CORS is already configured in `Program.cs`:
```csharp
b.Services.AddCors(o => o.AddDefaultPolicy(p =>
  p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin()));
```

If still seeing CORS errors, verify API is running and restart it.

#### Issue 4: File Format Error
**Symptom:** "Only PDF and DOCX files are supported"

**Fix:** Make sure you're uploading `.pdf` or `.docx` files, not:
- `.doc` (old Word format - not supported)
- `.txt`, `.rtf`, or other text formats

#### Issue 5: File Too Large
**Symptom:** "File size exceeds 5MB limit"

**Fix:**
- Compress your PDF
- Remove images from resume
- Or increase limit in `Program.cs` (line 187):
```csharp
const long maxFileSize = 10 * 1024 * 1024; // Increase to 10MB
```

#### Issue 6: Frontend Not Running
**Symptom:** Can't access http://localhost:5173

**Fix:**
```bash
cd web
npm install  # If first time
npm run dev
# Should show: Local: http://localhost:5173/
```

### Debugging Tips

**1. Open Browser DevTools (F12)**
- Console tab shows fetch errors
- Network tab shows actual requests
- Look for the request to `/api/resume/upload`
- Check status code and response

**2. Check API Logs**
The terminal running `dotnet run` shows:
- Incoming requests
- Errors/exceptions
- CORS preflight requests

**3. Test with curl**
```bash
# Create a test PDF (macOS)
echo "Test Resume" | textutil -stdin -format txt -convert pdf -output /tmp/test.pdf

# Upload it
curl -X POST http://localhost:5298/api/resume/upload \
  -F "file=@/tmp/test.pdf" \
  -F "userId=test-user" \
  -F "targetRole=Developer"
```

### Port Configuration

**API Ports (configured in `api/Properties/launchSettings.json`):**
- HTTP: `5298`
- HTTPS: `7114` (if using https profile)

**Frontend Port:**
- Vite dev server: `5173`

**To change API port:**
1. Edit `api/Properties/launchSettings.json`
2. Change `applicationUrl` values
3. Update frontend URLs if needed
4. Restart API

### Still Having Issues?

**Check this checklist:**
- [ ] API is running (`dotnet run` in api folder)
- [ ] Frontend is running (`npm run dev` in web folder)
- [ ] Browser is pointing to http://localhost:5173
- [ ] No errors in API terminal
- [ ] No errors in browser console (F12)
- [ ] Using correct file format (PDF or DOCX)
- [ ] File is under 5MB
- [ ] Port 5298 is not blocked by firewall

**Get more info:**
```bash
# Check if API port is listening
netstat -an | grep 5298

# Check if anything is blocking the port
lsof -i :5298

# See full API output
cd api
dotnet run --verbosity detailed
```

### Quick Reference - Working Configuration

**API** (`api/Program.cs`):
```csharp
// Line 12: CORS enabled for all origins
b.Services.AddCors(o => o.AddDefaultPolicy(p =>
  p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin()));

// Line 173: Upload endpoint
app.MapPost("/api/resume/upload", async (...) => { ... })
  .DisableAntiforgery();
```

**Frontend** (`web/src/ResumeUpload.tsx`):
```typescript
// Lines 88-107: Auto-detects port
const ports = [5298, 5000, 5001];
// Tries each port until one works
```

**Ports:**
- API: http://localhost:5298
- Frontend: http://localhost:5173

---

**The resume upload feature should now work perfectly!** 🎉

If you're still experiencing issues after trying these steps, please check:
1. API terminal for error messages
2. Browser console for detailed error info
3. Network tab in DevTools for actual request/response
