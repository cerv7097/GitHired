# Web App

This directory contains the Vite + React frontend for GitHired.

## Current App Sections

- authentication
- dashboard
- resume upload
- AI coach chat
- jobs
- assessment
- resources

The active app entry point is `src/main.tsx`, which gates the main application behind login.

## Run Locally

```bash
npm install
npm run dev
```

Default dev URL:

`http://localhost:5173`

## API Assumptions

The frontend currently expects the backend at `http://localhost:5001` for most requests.

One exception is `src/ResumeUpload.tsx`, which falls back to `5298` and `5000` if `5001` is unavailable.

## Notes

- The frontend is centered on authentication, coaching, resume analysis, jobs, assessments, and resources.
