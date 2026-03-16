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

export default function AgentChat({ userId }: { userId: string }) {
  const [messages, setMessages] = useState<Message[]>([]);
  const [input, setInput] = useState('');
  const [loading, setLoading] = useState(false);
  const [conversationId, setConversationId] = useState<string | null>(null);
  const [showTools, setShowTools] = useState(false);

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

  const formatJson = (payload: string) => {
    try {
      return JSON.stringify(JSON.parse(payload), null, 2);
    } catch {
      return payload;
    }
  };

  return (
    <div className="chat-panel">
      <div className="chat-toolbar">
        <div>
          <p className="section-title">Live Guidance</p>
          <h4 style={{ margin: 0 }}>AI Career Coach</h4>
        </div>
        <div className="chat-actions">
          <label className="toggle">
            <input
              type="checkbox"
              checked={showTools}
              onChange={(e) => setShowTools(e.target.checked)}
            />
            <span>Show tool usage</span>
          </label>
          <button type="button" className="ghost-button" onClick={clearConversation}>
            Clear Chat
          </button>
        </div>
      </div>

      {messages.length === 0 && (
        <div className="chat-empty">
          <h5>Try asking:</h5>
          <ul>
            <li>"What jobs match my skills?"</li>
            <li>"Analyze my resume for ATS compatibility"</li>
            <li>"How can I become a senior developer?"</li>
            <li>"Create an assessment for a data analyst role"</li>
            <li>"What’s my path from junior to lead engineer?"</li>
          </ul>
        </div>
      )}

      <div className="chat-window">
        {messages.map((msg, idx) => (
          <div key={idx} className={`chat-message ${msg.role}`}>
            <div className="chat-author">
              {msg.role === 'user' ? '👤 You' : '🤖 Career Coach'}
            </div>
            <div className="chat-content">{msg.content}</div>

            {showTools && msg.toolsUsed && msg.toolsUsed.length > 0 && (
              <div className="chat-tools">
                <strong>Tools used</strong>
                {msg.toolsUsed.map((tool, tidx) => (
                  <details key={tidx}>
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
              </div>
            )}
          </div>
        ))}

        {loading && (
          <div className="chat-loading">
            <div className="spinner" />
            <p>Thinking through the best response...</p>
          </div>
        )}
      </div>

      <div className="chat-input">
        <textarea
          value={input}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
              e.preventDefault();
              sendMessage();
            }
          }}
          placeholder="Ask about jobs, resume advice, assessments, or skill plans..."
        />
        <button
          type="button"
          className="primary-action"
          onClick={sendMessage}
          disabled={loading || !input.trim()}
        >
          Send
        </button>
      </div>

      {conversationId && (
        <p className="chat-meta">Conversation ID: {conversationId}</p>
      )}
    </div>
  );
}
