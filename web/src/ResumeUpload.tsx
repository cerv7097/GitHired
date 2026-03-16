import { useEffect, useRef, useState } from 'react';

interface ParseResult {
  success: boolean;
  word_count: number;
  character_count: number;
  has_contact_info: boolean;
  has_sections: boolean;
  detected_sections: string[];
  file_name: string;
  file_type: string;
  ats_score: number;
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
    if (atsResult.choices && atsResult.choices[0]?.message?.content) {
      const content = JSON.parse(atsResult.choices[0].message.content);
      const fromContent = sanitize(content.overall_score ?? content.score);
      if (fromContent !== null) return fromContent;
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

export default function ResumeUpload({ userId = 'user-123', onAtsScoreUpdate }: ResumeUploadProps) {
  const [file, setFile] = useState<File | null>(null);
  const [targetRole, setTargetRole] = useState('');
  const [targetIndustry, setTargetIndustry] = useState('');
  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState<UploadResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [dragActive, setDragActive] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const parserScore = result?.parse_result?.ats_score ?? null;

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

    try {
      // Try multiple possible ports (5001 first, then fallback)
      const ports = [5001, 5298, 5000];
      let response: Response | null = null;
      let lastError: Error | null = null;

      for (const port of ports) {
        try {
          const url = `http://localhost:${port}/api/resume/upload`;
          console.log(`Attempting to upload to ${url}`);
          response = await fetch(url, {
            method: 'POST',
            body: formData,
          });
          console.log(`Connected successfully on port ${port}`);
          break;
        } catch (err) {
          console.log(`Port ${port} failed:`, err);
          lastError = err as Error;
          continue;
        }
      }

      if (!response) {
        throw new Error(
          `Cannot connect to API. Tried ports ${ports.join(', ')}. ` +
          `Make sure the backend is running with 'cd api && dotnet run'. ` +
          `Last error: ${lastError?.message || 'Unknown'}`
        );
      }

      if (!response.ok) {
        const errorData = await response.json();
        throw new Error(errorData.error || `HTTP error! status: ${response.status}`);
      }

      const data: UploadResponse = await response.json();
      setResult(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to upload resume');
    } finally {
      setLoading(false);
    }
  };

  // Keep parent dashboard in sync with whatever ATS score we parsed (including zeros)
  useEffect(() => {
    if (!onAtsScoreUpdate) return;
    if (!result) {
      onAtsScoreUpdate(null);
      return;
    }
    const derivedScore = parserScore ?? extractAtsScore(result);
    onAtsScoreUpdate(derivedScore);
  }, [result, parserScore, onAtsScoreUpdate]);

  const getScoreColor = (score: number) => {
    if (score >= 80) return '#54f1c5';
    if (score >= 60) return '#f9a826';
    return '#fb7185';
  };

  const formatJson = (payload: string) => {
    try {
      return JSON.stringify(JSON.parse(payload), null, 2);
    } catch {
      return payload;
    }
  };

  const atsScore = parserScore ?? extractAtsScore(result);

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
          accept=".pdf,.docx"
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
            <span>or click to browse (PDF or DOCX)</span>
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
                <label>Contact Info</label>
                <p>{result.parse_result.has_contact_info ? 'Detected' : 'Missing'}</p>
              </div>
              <div>
                <label>Sections Found</label>
                <p>{result.parse_result.detected_sections.length}</p>
              </div>
            </div>
            {result.parse_result.detected_sections.length > 0 && (
              <p className="section-list">
                Detected sections: {result.parse_result.detected_sections.join(', ')}
              </p>
            )}
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

          <div className="result-card analysis">
            <div className="result-title">
              <strong>AI Analysis & Recommendations</strong>
            </div>
            <div className="analysis-copy">
              {result.agent_analysis.message}
            </div>

            <details className="tool-accordion">
              <summary>
                Tool Results ({result.agent_analysis.tools_used.length})
              </summary>
              {result.agent_analysis.tools_used.map((tool, idx) => (
                <details key={idx}>
                  <summary>{tool.toolName}</summary>
                  <div className="tool-details">
                    <div>
                      <strong>Arguments</strong>
                      <pre>{formatJson(tool.arguments)}</pre>
                    </div>
                    <div>
                      <strong>Result</strong>
                      <pre>{formatJson(tool.result)}</pre>
                    </div>
                  </div>
                </details>
              ))}
            </details>
          </div>
        </div>
      )}
    </div>
  );
}
