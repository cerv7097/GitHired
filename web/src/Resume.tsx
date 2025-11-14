import { useState } from 'react';
import { analyzeResume } from './lib/api';

type ResumeResult = string | {
  skills?: string[];
  roles?: string[];
  summary?: string;
};

export default function Resume() {
  const [text, setText] = useState('');
  const [result, setResult] = useState<ResumeResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const userId = 'user-123';

  async function go() {
    setErr(null);
    setLoading(true);
    setResult(null);
    try {
      const data = await analyzeResume(userId, text);
      setResult(data);
    } catch (e: unknown) {
      if (e instanceof Error) {
        setErr(e.message);
      } else {
        setErr('Request failed');
      }
    } finally {
      setLoading(false);
    }
  }

  return (
    <div style={{ padding: 16, maxWidth: 900, margin: '0 auto' }}>
      <h1>Résumé Analyzer</h1>
      <textarea
        placeholder="Paste your résumé text here…"
        value={text}
        onChange={e => setText(e.target.value)}
        style={{ width: '100%', height: 200, marginBottom: 8 }}
      />
      <button onClick={go} disabled={!text.trim() || loading}>
        {loading ? 'Analyzing…' : 'Analyze'}
      </button>

      {err && <p style={{ color: 'crimson', marginTop: 12 }}>{err}</p>}

      {result && (
        <div style={{ marginTop: 12 }}>
          <h3>Raw</h3>
          <pre style={{ whiteSpace: 'pre-wrap' }}>
            {typeof result === 'string' ? result : JSON.stringify(result, null, 2)}
          </pre>

          {typeof result !== 'string' && result.skills && (
            <p><b>Skills:</b> {result.skills.join(', ')}</p>
          )}
          {typeof result !== 'string' && result.roles && (
            <p><b>Roles:</b> {result.roles.join(', ')}</p>
          )}
          {typeof result !== 'string' && result.summary && (
            <p><b>Summary:</b> {result.summary}</p>
          )}
        </div>
      )}
    </div>
  );
}