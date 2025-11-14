import { useState } from 'react';
import { mockInterview } from './lib/api';

export default function Interview() {
  const [prompt, setPrompt] = useState('Start a mock interview for a junior .NET developer.');
  const [out, setOut] = useState<string>('');

  async function go() {
    setOut('…thinking…');
    const data = await mockInterview(prompt);
    setOut(typeof data === 'string' ? data : JSON.stringify(data, null, 2));
  }

  return (
    <div style={{ padding: 16, maxWidth: 800, margin: '0 auto' }}>
      <h1>Mock Interview</h1>
      <textarea
        value={prompt}
        onChange={e => setPrompt(e.target.value)}
        style={{ width: '100%', height: 120, marginBottom: 8 }}
      />
      <button onClick={go}>Ask</button>
      <pre style={{ whiteSpace: 'pre-wrap', marginTop: 12 }}>{out}</pre>
    </div>
  );
}