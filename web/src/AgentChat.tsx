import { useState } from 'react';
import React from 'react';

function renderMarkdown(text: string) {
  const elements: React.ReactNode[] = [];
  const lines = text.split('\n');
  let i = 0;

  while (i < lines.length) {
    const line = lines[i];

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

    if (line.trim() === '') {
      elements.push(<br key={i} />);
      i++; continue;
    }

    elements.push(<p key={i} style={{ margin: '4px 0' }}>{inlineFormat(line)}</p>);
    i++;
  }

  return elements;
}

function inlineFormat(text: string): React.ReactNode {
  const parts = text.split(/\*\*(.+?)\*\*/g);
  return parts.map((part, idx) =>
    idx % 2 === 1 ? <strong key={idx}>{part}</strong> : part
  );
}

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
      const response = await fetch(`${import.meta.env.VITE_API_BASE ?? ''}/api/agent/chat`, {
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
            <div className="chat-content">
              {msg.role === 'assistant' ? renderMarkdown(msg.content) : msg.content}
            </div>

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
