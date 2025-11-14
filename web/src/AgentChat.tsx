import { useState } from 'react';

interface Message {
  role: 'user' | 'assistant';
  content: string;
  toolsUsed?: ToolExecution[];
}

interface ToolExecution {
  toolName: string;
  arguments: string;
  result: string;
}

interface AgentResponse {
  message: string;
  toolsUsed: ToolExecution[];
  conversationId: string;
}

export default function AgentChat() {
  const [messages, setMessages] = useState<Message[]>([]);
  const [input, setInput] = useState('');
  const [loading, setLoading] = useState(false);
  const [conversationId, setConversationId] = useState<string | null>(null);
  const [showTools, setShowTools] = useState(false);
  const userId = 'user-123'; // In production, get from auth

  async function sendMessage() {
    if (!input.trim()) return;

    const userMessage: Message = { role: 'user', content: input };
    setMessages(prev => [...prev, userMessage]);
    setInput('');
    setLoading(true);

    try {
      const response = await fetch('http://localhost:5001/api/agent/chat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          userId,
          message: input,
          conversationId
        })
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      const data: AgentResponse = await response.json();

      // Update conversation ID
      if (!conversationId) {
        setConversationId(data.conversationId);
      }

      const assistantMessage: Message = {
        role: 'assistant',
        content: data.message,
        toolsUsed: data.toolsUsed
      };

      setMessages(prev => [...prev, assistantMessage]);
    } catch (error) {
      console.error('Error:', error);
      setMessages(prev => [
        ...prev,
        {
          role: 'assistant',
          content: `Error: ${error instanceof Error ? error.message : 'Failed to get response'}`
        }
      ]);
    } finally {
      setLoading(false);
    }
  }

  function clearConversation() {
    setMessages([]);
    setConversationId(null);
  }

  return (
    <div style={{ maxWidth: 900, margin: '0 auto', padding: 20 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 20 }}>
        <h1>🤖 AI Career Coach</h1>
        <div>
          <label style={{ marginRight: 10 }}>
            <input
              type="checkbox"
              checked={showTools}
              onChange={(e) => setShowTools(e.target.checked)}
            />
            Show tool usage
          </label>
          <button onClick={clearConversation} style={{ marginLeft: 10 }}>
            Clear Chat
          </button>
        </div>
      </div>

      {messages.length === 0 && (
        <div style={{
          background: '#f5f5f5',
          padding: 20,
          borderRadius: 8,
          marginBottom: 20,
          border: '1px solid #ddd'
        }}>
          <h3>Try asking:</h3>
          <ul>
            <li>"What jobs match my skills?"</li>
            <li>"Analyze my resume for ATS compatibility" (then paste resume)</li>
            <li>"How can I become a senior developer?"</li>
            <li>"Create an assessment for a data analyst role"</li>
            <li>"What's my career path from junior to lead engineer?"</li>
          </ul>
        </div>
      )}

      <div style={{
        height: 500,
        overflowY: 'auto',
        border: '1px solid #ccc',
        borderRadius: 8,
        padding: 15,
        marginBottom: 15,
        background: '#fafafa'
      }}>
        {messages.map((msg, idx) => (
          <div
            key={idx}
            style={{
              marginBottom: 15,
              padding: 12,
              borderRadius: 8,
              background: msg.role === 'user' ? '#e3f2fd' : '#fff',
              border: `1px solid ${msg.role === 'user' ? '#90caf9' : '#e0e0e0'}`
            }}
          >
            <div style={{ fontWeight: 'bold', marginBottom: 5, color: msg.role === 'user' ? '#1976d2' : '#388e3c' }}>
              {msg.role === 'user' ? '👤 You' : '🤖 AI Career Coach'}
            </div>
            <div style={{ whiteSpace: 'pre-wrap' }}>{msg.content}</div>

            {showTools && msg.toolsUsed && msg.toolsUsed.length > 0 && (
              <div style={{
                marginTop: 10,
                padding: 10,
                background: '#fff3e0',
                borderRadius: 4,
                fontSize: '0.85em',
                border: '1px solid #ffb74d'
              }}>
                <div style={{ fontWeight: 'bold', marginBottom: 5 }}>🔧 Tools Used:</div>
                {msg.toolsUsed.map((tool, tidx) => (
                  <details key={tidx} style={{ marginBottom: 5 }}>
                    <summary style={{ cursor: 'pointer', color: '#f57c00' }}>
                      {tool.toolName}
                    </summary>
                    <div style={{ marginLeft: 15, marginTop: 5 }}>
                      <div><strong>Arguments:</strong></div>
                      <pre style={{ fontSize: '0.9em', overflow: 'auto' }}>
                        {JSON.stringify(JSON.parse(tool.arguments), null, 2)}
                      </pre>
                      <div><strong>Result:</strong></div>
                      <pre style={{ fontSize: '0.9em', overflow: 'auto', maxHeight: 200 }}>
                        {JSON.stringify(JSON.parse(tool.result), null, 2)}
                      </pre>
                    </div>
                  </details>
                ))}
              </div>
            )}
          </div>
        ))}

        {loading && (
          <div style={{ textAlign: 'center', color: '#666' }}>
            <div>🤔 Thinking...</div>
            <div style={{ fontSize: '0.85em', marginTop: 5 }}>
              (The agent is analyzing your request and deciding which tools to use)
            </div>
          </div>
        )}
      </div>

      <div style={{ display: 'flex', gap: 10 }}>
        <textarea
          value={input}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
              e.preventDefault();
              sendMessage();
            }
          }}
          placeholder="Ask me about your career, job search, resume, or skills assessment..."
          style={{
            flex: 1,
            padding: 12,
            fontSize: 14,
            borderRadius: 8,
            border: '1px solid #ccc',
            resize: 'vertical',
            minHeight: 80,
            fontFamily: 'inherit'
          }}
        />
        <button
          onClick={sendMessage}
          disabled={loading || !input.trim()}
          style={{
            padding: '12px 24px',
            fontSize: 16,
            borderRadius: 8,
            border: 'none',
            background: loading || !input.trim() ? '#ccc' : '#1976d2',
            color: '#fff',
            cursor: loading || !input.trim() ? 'not-allowed' : 'pointer',
            fontWeight: 'bold',
            alignSelf: 'flex-end'
          }}
        >
          Send
        </button>
      </div>

      {conversationId && (
        <div style={{ marginTop: 10, fontSize: '0.8em', color: '#666' }}>
          Conversation ID: {conversationId}
        </div>
      )}
    </div>
  );
}
