import { useState } from 'react';

interface ParseResult {
  success: boolean;
  word_count: number;
  character_count: number;
  has_contact_info: boolean;
  has_sections: boolean;
  detected_sections: string[];
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

export default function ResumeUpload() {
  const [file, setFile] = useState<File | null>(null);
  const [targetRole, setTargetRole] = useState('');
  const [targetIndustry, setTargetIndustry] = useState('');
  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState<UploadResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [dragActive, setDragActive] = useState(false);

  const userId = 'user-123'; // In production, get from auth

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

  const getATSScore = (): number | null => {
    if (!result) return null;

    const atsTool = result.agent_analysis.tools_used.find(t =>
      t.toolName === 'analyze_ats_compatibility'
    );

    if (atsTool) {
      try {
        const atsResult = JSON.parse(atsTool.result);
        // Try to extract content from OpenAI response format
        if (atsResult.choices && atsResult.choices[0]?.message?.content) {
          const content = JSON.parse(atsResult.choices[0].message.content);
          return content.overall_score || content.score || null;
        }
        return atsResult.overall_score || atsResult.score || null;
      } catch {
        return null;
      }
    }
    return null;
  };

  const getScoreColor = (score: number) => {
    if (score >= 80) return '#4caf50';
    if (score >= 60) return '#ff9800';
    return '#f44336';
  };

  const atsScore = getATSScore();

  return (
    <div style={{ maxWidth: 1000, margin: '0 auto', padding: 20 }}>
      <h1>📄 Resume Analyzer</h1>
      <p style={{ color: '#666', marginBottom: 30 }}>
        Upload your resume (PDF or DOCX) for comprehensive ATS analysis and improvement suggestions
      </p>

      {/* File Upload Area */}
      <div
        onDragEnter={handleDrag}
        onDragLeave={handleDrag}
        onDragOver={handleDrag}
        onDrop={handleDrop}
        style={{
          border: `2px dashed ${dragActive ? '#1976d2' : '#ccc'}`,
          borderRadius: 8,
          padding: 40,
          textAlign: 'center',
          background: dragActive ? '#e3f2fd' : '#fafafa',
          marginBottom: 20,
          cursor: 'pointer'
        }}
        onClick={() => document.getElementById('fileInput')?.click()}
      >
        <input
          id="fileInput"
          type="file"
          accept=".pdf,.docx"
          onChange={handleFileChange}
          style={{ display: 'none' }}
        />

        <div style={{ fontSize: 48, marginBottom: 10 }}>📎</div>

        {file ? (
          <div>
            <div style={{ fontWeight: 'bold', color: '#1976d2', marginBottom: 5 }}>
              {file.name}
            </div>
            <div style={{ fontSize: '0.9em', color: '#666' }}>
              {(file.size / 1024).toFixed(0)} KB
            </div>
          </div>
        ) : (
          <div>
            <div style={{ fontWeight: 'bold', marginBottom: 5 }}>
              Drag & drop your resume here
            </div>
            <div style={{ fontSize: '0.9em', color: '#666' }}>
              or click to browse (PDF or DOCX, max 5MB)
            </div>
          </div>
        )}
      </div>

      {/* Optional Target Fields */}
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 15, marginBottom: 20 }}>
        <div>
          <label style={{ display: 'block', marginBottom: 5, fontWeight: 'bold' }}>
            Target Role (Optional)
          </label>
          <input
            type="text"
            value={targetRole}
            onChange={(e) => setTargetRole(e.target.value)}
            placeholder="e.g., Senior Software Engineer"
            style={{
              width: '100%',
              padding: 10,
              borderRadius: 4,
              border: '1px solid #ccc',
              fontSize: 14
            }}
          />
        </div>
        <div>
          <label style={{ display: 'block', marginBottom: 5, fontWeight: 'bold' }}>
            Target Industry (Optional)
          </label>
          <input
            type="text"
            value={targetIndustry}
            onChange={(e) => setTargetIndustry(e.target.value)}
            placeholder="e.g., Technology, Finance"
            style={{
              width: '100%',
              padding: 10,
              borderRadius: 4,
              border: '1px solid #ccc',
              fontSize: 14
            }}
          />
        </div>
      </div>

      {/* Upload Button */}
      <button
        onClick={handleUpload}
        disabled={!file || loading}
        style={{
          width: '100%',
          padding: 15,
          fontSize: 16,
          fontWeight: 'bold',
          borderRadius: 8,
          border: 'none',
          background: !file || loading ? '#ccc' : '#1976d2',
          color: '#fff',
          cursor: !file || loading ? 'not-allowed' : 'pointer',
          marginBottom: 20
        }}
      >
        {loading ? '🔄 Analyzing Resume...' : '🚀 Analyze Resume'}
      </button>

      {/* Error Display */}
      {error && (
        <div style={{
          padding: 15,
          background: '#ffebee',
          border: '1px solid #f44336',
          borderRadius: 8,
          color: '#c62828',
          marginBottom: 20
        }}>
          ❌ {error}
        </div>
      )}

      {/* Results */}
      {result && (
        <div>
          {/* Parse Results */}
          <div style={{
            background: '#e8f5e9',
            border: '1px solid #4caf50',
            borderRadius: 8,
            padding: 20,
            marginBottom: 20
          }}>
            <h3 style={{ marginTop: 0 }}>✅ Resume Parsed Successfully</h3>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 15 }}>
              <div>
                <div style={{ fontSize: '0.85em', color: '#666' }}>Word Count</div>
                <div style={{ fontSize: '1.5em', fontWeight: 'bold' }}>
                  {result.parse_result.word_count}
                </div>
              </div>
              <div>
                <div style={{ fontSize: '0.85em', color: '#666' }}>Contact Info</div>
                <div style={{ fontSize: '1.5em', fontWeight: 'bold' }}>
                  {result.parse_result.has_contact_info ? '✓' : '✗'}
                </div>
              </div>
              <div>
                <div style={{ fontSize: '0.85em', color: '#666' }}>Sections Found</div>
                <div style={{ fontSize: '1.5em', fontWeight: 'bold' }}>
                  {result.parse_result.detected_sections.length}
                </div>
              </div>
            </div>
            {result.parse_result.detected_sections.length > 0 && (
              <div style={{ marginTop: 15 }}>
                <strong>Detected Sections:</strong>{' '}
                {result.parse_result.detected_sections.join(', ')}
              </div>
            )}
          </div>

          {/* ATS Score */}
          {atsScore !== null && (
            <div style={{
              background: '#fff',
              border: `3px solid ${getScoreColor(atsScore)}`,
              borderRadius: 8,
              padding: 20,
              marginBottom: 20,
              textAlign: 'center'
            }}>
              <h3 style={{ marginTop: 0 }}>ATS Compatibility Score</h3>
              <div style={{
                fontSize: '4em',
                fontWeight: 'bold',
                color: getScoreColor(atsScore)
              }}>
                {atsScore}/100
              </div>
              <div style={{ fontSize: '0.9em', color: '#666' }}>
                {atsScore >= 80 && '🎉 Excellent! Your resume is highly ATS-compatible'}
                {atsScore >= 60 && atsScore < 80 && '👍 Good, but there\'s room for improvement'}
                {atsScore < 60 && '⚠️ Needs significant improvements to pass ATS systems'}
              </div>
            </div>
          )}

          {/* Agent Analysis */}
          <div style={{
            background: '#fff',
            border: '1px solid #ddd',
            borderRadius: 8,
            padding: 20
          }}>
            <h3 style={{ marginTop: 0 }}>🤖 AI Analysis & Recommendations</h3>
            <div style={{ whiteSpace: 'pre-wrap', lineHeight: 1.6 }}>
              {result.agent_analysis.message}
            </div>

            {/* Tools Used */}
            <details style={{ marginTop: 20 }}>
              <summary style={{ cursor: 'pointer', fontWeight: 'bold', color: '#1976d2' }}>
                🔧 View Detailed Tool Results ({result.agent_analysis.tools_used.length} tools used)
              </summary>
              {result.agent_analysis.tools_used.map((tool, idx) => (
                <details key={idx} style={{ marginLeft: 20, marginTop: 10 }}>
                  <summary style={{ cursor: 'pointer', color: '#f57c00' }}>
                    {tool.toolName}
                  </summary>
                  <pre style={{
                    background: '#f5f5f5',
                    padding: 15,
                    borderRadius: 4,
                    overflow: 'auto',
                    fontSize: '0.85em',
                    maxHeight: 400
                  }}>
                    {JSON.stringify(JSON.parse(tool.result), null, 2)}
                  </pre>
                </details>
              ))}
            </details>
          </div>
        </div>
      )}
    </div>
  );
}
