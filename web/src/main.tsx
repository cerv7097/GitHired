import React, { useEffect, useState } from 'react'
import ReactDOM from 'react-dom/client'
import App from './App'
import Login, { type User } from './Login'
import './index.css'
import './App.css'

const API_BASE = 'http://localhost:5001'
const TOKEN_KEY = 'cc_token'

interface AuthState {
  token: string
  user: User
}

function Root() {
  const [auth, setAuth] = useState<AuthState | null>(null)
  const [checking, setChecking] = useState(true)

  useEffect(() => {
    const token = localStorage.getItem(TOKEN_KEY)
    if (!token) {
      setChecking(false)
      return
    }
    fetch(`${API_BASE}/api/auth/me`, {
      headers: { Authorization: `Bearer ${token}` },
    })
      .then(res => {
        if (!res.ok) throw new Error('invalid')
        return res.json()
      })
      .then(data => {
        setAuth({
          token,
          user: {
            id: data.id,
            email: data.email,
            firstName: data.firstName,
            lastName: data.lastName,
          },
        })
      })
      .catch(() => {
        localStorage.removeItem(TOKEN_KEY)
      })
      .finally(() => setChecking(false))
  }, [])

  function handleLogin(token: string, user: User) {
    localStorage.setItem(TOKEN_KEY, token)
    setAuth({ token, user })
  }

  function handleLogout() {
    localStorage.removeItem(TOKEN_KEY)
    setAuth(null)
  }

  if (checking) {
    return (
      <div className="auth-shell">
        <div className="chat-loading">
          <div className="spinner" />
        </div>
      </div>
    )
  }

  if (!auth) {
    return <Login onLogin={handleLogin} />
  }

  return <App user={auth.user} onLogout={handleLogout} />
}

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <Root />
  </React.StrictMode>,
)
