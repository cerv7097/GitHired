import { useEffect, useRef, useState } from 'react';

interface ParseResult {
  success: boolean;
  word_count: number;
  character_count: number;
  file_name: string;
  file_type: string;
}

interface ToolExecution {
  toolName: string;
  arguments: string;
  result: string;
}

interface AgentAnalysis {
  message: string;
  tools_used: ToolExecution[];
  conversation_id: string;
}

interface UploadResponse {
  parse_result: ParseResult;
  agent_analysis: AgentAnalysis;
}

interface ResumeUploadProps {
  userId?: string;
  onAtsScoreUpdate?: (score: number | null) => void;
  onUploadStart?: () => void;
  onUploadSuccess?: () => void;
}

export function extractAtsScore(result: UploadResponse | null): number | null {
  if (!result) return null;

  const atsTool = result.agent_analysis.tools_used.find(t =>
    t.toolName === 'analyze_ats_compatibility'
  );

  if (!atsTool) return null;

  // Helper to clamp and round any numeric value we can interpret
  const sanitize = (value: unknown): number | null => {
    const numeric = Number(value);
    if (!Number.isFinite(numeric)) return null;
    return Math.min(100, Math.max(0, Math.round(numeric)));
  };

  try {
    const atsResult = JSON.parse(atsTool.result);
    if (typeof atsResult?.error === 'string' && atsResult.error.length > 0) {
      return null;
    }
    const fromRoot = sanitize(atsResult.overall_score ?? atsResult.score);
    if (fromRoot !== null) return fromRoot;
  } catch {
    // fall through to lightweight parsing below
  }

  // Try a plain number string (e.g., "0" or 0)
  const plainNumber = sanitize(atsTool.result);
  if (plainNumber !== null) return plainNumber;

  // Last resort: find first 0-100 number in the string
  const match = atsTool.result.match(/\b([0-9]{1,3})\b/);
  if (match) {
    const guessed = sanitize(match[1]);
    if (guessed !== null) return guessed;
  }

  return null;
}

function renderMarkdown(text: string) {
  const elements: React.ReactNode[] = [];
  const lines = text.split('\n');
  let i = 0;

  while (i < lines.length) {
    const line = lines[i];

    // Headings (check longest prefix first)
    if (line.startsWith('#### ')) {
      elements.push(<strong key={i} style={{ display: 'block', margin: '10px 0 2px' }}>{inlineFormat(line.slice(5))}</strong>);
      i++; continue;
    }
    if (line.startsWith('### ')) {
      elements.push(<h4 key={i} style={{ margin: '12px 0 4px' }}>{inlineFormat(line.slice(4))}</h4>);
      i++; continue;
    }
    if (line.startsWith('## ')) {
      elements.push(<h3 key={i} style={{ margin: '14px 0 4px' }}>{inlineFormat(line.slice(3))}</h3>);
      i++; continue;
    }
    if (line.startsWith('# ')) {
      elements.push(<h2 key={i} style={{ margin: '16px 0 6px' }}>{inlineFormat(line.slice(2))}</h2>);
      i++; continue;
    }

    // Collect consecutive bullet lines into a single <ul>
    if (/^(\s*[-*•]|\d+\.) /.test(line)) {
      const items: React.ReactNode[] = [];
      while (i < lines.length && /^(\s*[-*•]|\d+\.) /.test(lines[i])) {
        const itemText = lines[i].replace(/^(\s*[-*•]|\d+\.) /, '');
        items.push(<li key={i}>{inlineFormat(itemText)}</li>);
        i++;
      }
      elements.push(<ul key={`ul-${i}`} style={{ margin: '6px 0', paddingLeft: 20 }}>{items}</ul>);
      continue;
    }

    // Blank line → spacer
    if (line.trim() === '') {
      elements.push(<br key={i} />);
      i++; continue;
    }

    // Regular paragraph line
    elements.push(<p key={i} style={{ margin: '4px 0' }}>{inlineFormat(line)}</p>);
    i++;
  }

  return elements;
}

function inlineFormat(text: string): React.ReactNode {
  // Split on **bold** and render alternating plain/bold segments
  const parts = text.split(/\*\*(.+?)\*\*/g);
  return parts.map((part, idx) =>
    idx % 2 === 1 ? <strong key={idx}>{part}</strong> : part
  );
}

// localStorage key for the most recent analysis. Scoped per user so different
// accounts on the same browser don't see each other's results.
const STORED_RESULT_KEY = (userId: string) => `resumeAnalysis:${userId}`;

function loadStoredResult(userId: string): UploadResponse | null {
  if (typeof window === 'undefined') return null;
  try {
    const raw = window.localStorage.getItem(STORED_RESULT_KEY(userId));
    if (!raw) return null;
    const parsed = JSON.parse(raw) as UploadResponse;
    if (parsed && parsed.parse_result && parsed.agent_analysis) return parsed;
    return null;
  } catch {
    return null;
  }
}

export default function ResumeUpload({ userId = 'user-123', onAtsScoreUpdate, onUploadStart, onUploadSuccess }: ResumeUploadProps) {
  const [file, setFile] = useState<File | null>(null);
  const [targetRole, setTargetRole] = useState('');
  const [targetIndustry, setTargetIndustry] = useState('');
  const [loading, setLoading] = useState(false);
  // Seed from localStorage so the analysis is still visible after a tab switch,
  // page refresh, or re-login. Cleared by uploading a new resume (which replaces
  // the stored value below).
  const [result, setResult] = useState<UploadResponse | null>(() => loadStoredResult(userId));
  const [error, setError] = useState<string | null>(null);
  const [dragActive, setDragActive] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const handleDrag = (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    if (e.type === 'dragenter' || e.type === 'dragover') {
      setDragActive(true);
    } else if (e.type === 'dragleave') {
      setDragActive(false);
    }
  };

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setDragActive(false);

    if (e.dataTransfer.files && e.dataTransfer.files[0]) {
      setFile(e.dataTransfer.files[0]);
      setError(null);
    }
  };

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files[0]) {
      setFile(e.target.files[0]);
      setError(null);
    }
  };

  const handleUpload = async () => {
    if (!file) {
      setError('Please select a file');
      return;
    }

    const formData = new FormData();
    formData.append('file', file);
    formData.append('userId', userId);
    if (targetRole) formData.append('targetRole', targetRole);
    if (targetIndustry) formData.append('targetIndustry', targetIndustry);

    setLoading(true);
    setError(null);
    setResult(null);
    onUploadStart?.();

    try {
      const url = `${import.meta.env.VITE_API_BASE ?? ''}/api/resume/upload`;
      const response = await fetch(url, {
        method: 'POST',
        body: formData,
      });

      if (!response.ok) {
        const errorData = await response.json();
        throw new Error(errorData.error || `HTTP error! status: ${response.status}`);
      }

      const data: UploadResponse = await response.json();
      setResult(data);
      // Persist so the analysis survives tab navigation and re-logins.
      try {
        window.localStorage.setItem(STORED_RESULT_KEY(userId), JSON.stringify(data));
      } catch {
        // Quota exceeded or storage disabled — non-fatal, the analysis is
        // still visible in this session.
      }
      onUploadSuccess?.();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to upload resume');
    } finally {
      setLoading(false);
    }
  };

  // Keep parent dashboard in sync with whatever ATS score we parsed (including zeros)
  useEffect(() => {
    if (!onAtsScoreUpdate || !result) return;
    onAtsScoreUpdate(extractAtsScore(result));
  }, [result, onAtsScoreUpdate]);

  const getScoreColor = (score: number) => {
    if (score >= 80) return '#54f1c5';
    if (score >= 60) return '#f9a826';
    return '#fb7185';
  };

  const atsScore = extractAtsScore(result);

  return (
    <div className="resume-upload">
      <p className="section-title">Upload Center</p>
      <h4 style={{ marginTop: 0 }}>Drop your latest resume</h4>
      <p style={{ color: '#8ea5d9', marginBottom: 24 }}>
        We’ll parse the document, score it for ATS readiness, and feed the results into the coach.
      </p>

      <div
        className={`upload-dropzone ${dragActive ? 'is-active' : ''}`}
        onDragEnter={handleDrag}
        onDragLeave={handleDrag}
        onDragOver={handleDrag}
        onDrop={handleDrop}
        onClick={() => fileInputRef.current?.click()}
      >
        <input
          ref={fileInputRef}
          type="file"
          accept=".pdf,.docx,.doc,application/pdf,application/vnd.openxmlformats-officedocument.wordprocessingml.document,application/msword"
          onChange={handleFileChange}
          style={{ display: 'none' }}
        />
        <div className="dropzone-icon">📎</div>
        {file ? (
          <div className="file-meta">
            <strong>{file.name}</strong>
            <span>{(file.size / 1024).toFixed(0)} KB</span>
          </div>
        ) : (
          <div className="file-meta">
            <strong>Drag & drop your resume here</strong>
            <span>or click to browse (PDF, DOCX, or DOC)</span>
          </div>
        )}
      </div>

      <div className="upload-form-grid">
        <label className="field">
          <span>Target Role (optional)</span>
          <input
            type="text"
            value={targetRole}
            onChange={(e) => setTargetRole(e.target.value)}
            placeholder="e.g., Senior Software Engineer"
          />
        </label>
        <label className="field">
          <span>Target Industry (optional)</span>
          <input
            type="text"
            value={targetIndustry}
            onChange={(e) => setTargetIndustry(e.target.value)}
            placeholder="e.g., Technology, Finance"
          />
        </label>
      </div>

      <button
        onClick={handleUpload}
        disabled={!file || loading}
        className="primary-action"
      >
        {loading ? 'Analyzing Resume...' : 'Analyze Resume'}
      </button>

      {error && (
        <div className="alert error">
          {error}
        </div>
      )}

      {result && (
        <div className="upload-results">
          <div className="result-card success">
            <div className="result-title">
              <span className="badge">Scan results</span>
              <strong>Resume parsed successfully</strong>
            </div>
            <div className="result-grid">
              <div>
                <label>Word Count</label>
                <p>{result.parse_result.word_count}</p>
              </div>
              <div>
                <label>Characters</label>
                <p>{result.parse_result.character_count.toLocaleString()}</p>
              </div>
              <div>
                <label>File Type</label>
                <p>{result.parse_result.file_type.toUpperCase().replace('.', '')}</p>
              </div>
            </div>
          </div>

          {atsScore !== null && (
            <div className="result-card ats-score">
              <div className="score-circle" style={{ borderColor: getScoreColor(atsScore) }}>
                {atsScore}
                <span>/100</span>
              </div>
              <div className="score-details">
                <h4>ATS Compatibility Score</h4>
                <div className="score-bar">
                  <span style={{ width: `${atsScore}%`, background: getScoreColor(atsScore) }} />
                </div>
                <small>
                  {atsScore >= 80 && 'Excellent! Your resume is highly ATS-compatible.'}
                  {atsScore >= 60 && atsScore < 80 && 'Solid baseline, refine keywords for a boost.'}
                  {atsScore < 60 && 'Focus on structure and keywords to improve ATS pass rates.'}
                </small>
              </div>
            </div>
          )}

          {/* The analysis is wrapped in <details open> so users can collapse it
              when they want a cleaner dashboard but it's always available to
              re-open. State persists in localStorage (see loadStoredResult /
              the upload handler), so the section survives tab switches and
              re-logins.
              The previous "Tool Results" accordion was removed — it exposed
              internal tool names and raw arguments/results which aren't meant
              for end users to see. */}
          <details className="result-card analysis" open>
            <summary className="analysis-summary">
              <strong>AI Analysis & Recommendations</strong>
              {/* Empty — the hint label is driven entirely by CSS so it
                  swaps between "click to expand" / "click to collapse"
                  based on the parent <details>[open] state. */}
              <span className="analysis-summary-hint" />
            </summary>
            <div className="analysis-copy">
              {renderMarkdown(result.agent_analysis.message)}
            </div>
          </details>
        </div>
      )}
    </div>
  );
}
