export async function analyzeResume(userId: string, resumeText: string) {
  const res = await fetch(`${import.meta.env.VITE_API_BASE}/api/resume/analyze`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ userId, resumeText })
  });

  // surface non-2xx as errors so the UI can show them
  const text = await res.text();
  if (!res.ok) {
    throw new Error(`HTTP ${res.status}: ${text.slice(0, 200)}`);
  }
  try { return JSON.parse(text); } catch { return text; }
}
