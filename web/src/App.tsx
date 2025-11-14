import { useState } from 'react'
import AgentChat from './AgentChat'
import ResumeUpload from './ResumeUpload'

export default function App() {
  const [view, setView] = useState<'chat' | 'resume'>('resume')

  return (
    <div>
      <nav style={{
        background: '#1976d2',
        padding: '15px 20px',
        color: '#fff',
        display: 'flex',
        gap: 20,
        boxShadow: '0 2px 4px rgba(0,0,0,0.1)'
      }}>
        <button
          onClick={() => setView('resume')}
          style={{
            padding: '10px 20px',
            background: view === 'resume' ? '#fff' : 'transparent',
            color: view === 'resume' ? '#1976d2' : '#fff',
            border: '2px solid #fff',
            borderRadius: 4,
            cursor: 'pointer',
            fontWeight: 'bold'
          }}
        >
          📄 Resume Upload
        </button>
        <button
          onClick={() => setView('chat')}
          style={{
            padding: '10px 20px',
            background: view === 'chat' ? '#fff' : 'transparent',
            color: view === 'chat' ? '#1976d2' : '#fff',
            border: '2px solid #fff',
            borderRadius: 4,
            cursor: 'pointer',
            fontWeight: 'bold'
          }}
        >
          💬 AI Career Coach
        </button>
      </nav>

      {view === 'resume' ? <ResumeUpload /> : <AgentChat />}
    </div>
  )
}
