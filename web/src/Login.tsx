import { useState } from 'react';

const API_BASE = import.meta.env.VITE_API_BASE ?? '';

export interface User {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
}

interface Props {
  onLogin: (token: string, user: User) => void;
}

type Mode = 'login' | 'register';
type Step = 'credentials' | 'verify' | 'forgot' | 'reset';

export default function Login({ onLogin }: Props) {
  const [mode, setMode] = useState<Mode>('login');
  const [step, setStep] = useState<Step>('credentials');
  const [firstName, setFirstName] = useState('');
  const [lastName, setLastName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [verificationCode, setVerificationCode] = useState('');
  const [resetCode, setResetCode] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setMessage(null);
    setLoading(true);

    try {
      const endpoint = mode === 'login' ? '/api/auth/login' : '/api/auth/register';
      const body = mode === 'login'
        ? { email, password }
        : { email, password, firstName, lastName };

      const res = await fetch(`${API_BASE}${endpoint}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });

      if (res.status === 409) {
        const data = await res.json().catch(() => ({}));
        if (data.requiresEmailVerification) {
          setStep('verify');
          setMessage(data.error ?? 'Enter the code sent to your email.');
        } else {
          setError(data.error ?? 'An account with that email already exists.');
        }
        return;
      }
      if (res.status === 401 || res.status === 400) {
        const data = await res.json().catch(() => ({}));
        setError(data.error ?? 'Invalid email or password.');
        return;
      }
      if (!res.ok) {
        setError('Something went wrong. Please try again.');
        return;
      }

      const data = await res.json();
      if (mode === 'register' && data.requiresEmailVerification) {
        setStep('verify');
        setVerificationCode('');
        setMessage(data.message ?? 'Verification code sent.');
        return;
      }

      onLogin(data.token, {
        id: data.user.id,
        email: data.user.email,
        firstName: data.user.firstName,
        lastName: data.user.lastName,
      });
    } catch {
      setError('Could not reach the server. Make sure the API is running.');
    } finally {
      setLoading(false);
    }
  }

  function switchMode(next: Mode) {
    setMode(next);
    setStep('credentials');
    setError(null);
    setMessage(null);
    setVerificationCode('');
  }

  async function handleVerify(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setMessage(null);
    setLoading(true);

    try {
      const res = await fetch(`${API_BASE}/api/auth/verify-email`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, code: verificationCode }),
      });

      const data = await res.json().catch(() => ({}));
      if (!res.ok) {
        setError(data.error ?? 'Verification failed.');
        return;
      }

      onLogin(data.token, {
        id: data.user.id,
        email: data.user.email,
        firstName: data.user.firstName,
        lastName: data.user.lastName,
      });
    } catch {
      setError('Could not reach the server. Make sure the API is running.');
    } finally {
      setLoading(false);
    }
  }

  async function handleResendCode() {
    setError(null);
    setMessage(null);
    setLoading(true);

    try {
      const res = await fetch(`${API_BASE}/api/auth/resend-verification`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email }),
      });

      const data = await res.json().catch(() => ({}));
      if (!res.ok) {
        setError(data.error ?? 'Could not resend verification code.');
        return;
      }

      setMessage(data.message ?? 'Verification code sent.');
    } catch {
      setError('Could not reach the server. Make sure the API is running.');
    } finally {
      setLoading(false);
    }
  }

  async function sendResetCode() {
    setError(null);
    setMessage(null);
    setLoading(true);

    try {
      const res = await fetch(`${API_BASE}/api/auth/forgot-password`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email }),
      });

      const data = await res.json().catch(() => ({}));
      if (!res.ok) {
        setError(data.error ?? 'Something went wrong.');
        return;
      }

      setMessage(data.message ?? 'If that email is registered, a reset code has been sent.');
      setStep('reset');
      setResetCode('');
      setNewPassword('');
    } catch {
      setError('Could not reach the server. Make sure the API is running.');
    } finally {
      setLoading(false);
    }
  }

  async function handleForgotPassword(e: React.FormEvent) {
    e.preventDefault();
    await sendResetCode();
  }

  async function handleResetPassword(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setMessage(null);
    setLoading(true);

    try {
      const res = await fetch(`${API_BASE}/api/auth/reset-password`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, code: resetCode, newPassword }),
      });

      const data = await res.json().catch(() => ({}));
      if (!res.ok) {
        setError(data.error ?? 'Password reset failed.');
        return;
      }

      setMessage(data.message ?? 'Password reset successfully.');
      setStep('credentials');
      setPassword('');
      setResetCode('');
      setNewPassword('');
    } catch {
      setError('Could not reach the server. Make sure the API is running.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="auth-shell">
      <div className="auth-ambient auth-ambient-left" aria-hidden="true">
        <span>Build stronger resumes</span>
        <span>with intelligent guidance</span>
      </div>

      <div className="auth-ambient auth-ambient-right" aria-hidden="true">
        <span>Discover better opportunities</span>
        <span>tailored to your future</span>
      </div>

      <div className="auth-card card">
        <div style={{ textAlign: 'center', marginBottom: 28 }}>
          <img src="/Nextwavelogo.png" alt="NextWave Insights" style={{ width: 160, height: 160, margin: '0 auto 16px', display: 'block', objectFit: 'contain', filter: 'brightness(0) invert(1)' }} />
          <h2 style={{ margin: 0, fontSize: '1.8rem' }}>GitHired</h2>
          <p style={{ color: '#7c91c1', marginTop: 4, fontSize: '0.82rem', letterSpacing: '0.05em' }}>by NextWave Insights</p>
          <p style={{ color: '#8ea5d9', marginTop: 6, fontSize: '0.9rem' }}>Your AI-powered career launch platform</p>
        </div>

        {step === 'credentials' || step === 'verify' ? (
          <div className="auth-toggle">
            <button
              type="button"
              className={mode === 'login' ? 'active' : ''}
              onClick={() => switchMode('login')}
              disabled={step === 'verify'}
            >
              Sign In
            </button>
            <button
              type="button"
              className={mode === 'register' ? 'active' : ''}
              onClick={() => switchMode('register')}
              disabled={step === 'verify'}
            >
              Create Account
            </button>
          </div>
        ) : (
          <div style={{ marginBottom: 24 }}>
            <button
              type="button"
              className="ghost-button"
              onClick={() => { setStep('credentials'); setError(null); setMessage(null); }}
              style={{ fontSize: '0.85rem', padding: '6px 0', color: '#8ea5d9' }}
            >
              ← Back to Sign In
            </button>
          </div>
        )}

        {error && <div className="alert error">{error}</div>}
        {message && <div className="alert success">{message}</div>}

        {step === 'credentials' && (
          <form onSubmit={handleSubmit}>
            {mode === 'register' && (
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, marginBottom: 12 }}>
                <div className="field">
                  <label>First Name</label>
                  <input
                    type="text"
                    placeholder="Jane"
                    value={firstName}
                    onChange={e => setFirstName(e.target.value)}
                    required
                    autoComplete="given-name"
                  />
                </div>
                <div className="field">
                  <label>Last Name</label>
                  <input
                    type="text"
                    placeholder="Doe"
                    value={lastName}
                    onChange={e => setLastName(e.target.value)}
                    required
                    autoComplete="family-name"
                  />
                </div>
              </div>
            )}

            <div className="field" style={{ marginBottom: 12 }}>
              <label>Email</label>
              <input
                type="email"
                placeholder="you@example.com"
                value={email}
                onChange={e => setEmail(e.target.value)}
                required
                autoComplete="email"
              />
            </div>

            <div className="field" style={{ marginBottom: mode === 'login' ? 8 : 20 }}>
              <label>Password</label>
              <input
                type="password"
                placeholder={mode === 'register' ? 'Choose a password' : 'Your password'}
                value={password}
                onChange={e => setPassword(e.target.value)}
                required
                autoComplete={mode === 'register' ? 'new-password' : 'current-password'}
              />
            </div>

            {mode === 'login' && (
              <div style={{ textAlign: 'right', marginBottom: 20 }}>
                <button
                  type="button"
                  className="ghost-button"
                  onClick={() => { setStep('forgot'); setError(null); setMessage(null); }}
                  style={{ fontSize: '0.82rem', color: '#8ea5d9', padding: '2px 0' }}
                >
                  Forgot password?
                </button>
              </div>
            )}

            <button type="submit" className="primary-action" disabled={loading}>
              {loading ? 'Please wait…' : mode === 'login' ? 'Sign In' : 'Create Account'}
            </button>
          </form>
        )}

        {step === 'verify' && (
          <form onSubmit={handleVerify}>
            <div className="field" style={{ marginBottom: 12 }}>
              <label>Email</label>
              <input type="email" value={email} disabled />
            </div>

            <div className="field" style={{ marginBottom: 20 }}>
              <label>Verification Code</label>
              <input
                type="text"
                placeholder="123456"
                value={verificationCode}
                onChange={e => setVerificationCode(e.target.value)}
                required
                inputMode="numeric"
                autoComplete="one-time-code"
              />
            </div>

            <button type="submit" className="primary-action" disabled={loading}>
              {loading ? 'Please wait…' : 'Verify Email'}
            </button>

            <button
              type="button"
              className="secondary-action"
              disabled={loading}
              onClick={handleResendCode}
              style={{ marginTop: 12, width: '100%' }}
            >
              Resend Code
            </button>
          </form>
        )}

        {step === 'forgot' && (
          <form onSubmit={handleForgotPassword}>
            <p style={{ color: '#8ea5d9', fontSize: '0.9rem', marginBottom: 20 }}>
              Enter your email and we'll send you a code to reset your password.
            </p>
            <div className="field" style={{ marginBottom: 20 }}>
              <label>Email</label>
              <input
                type="email"
                placeholder="you@example.com"
                value={email}
                onChange={e => setEmail(e.target.value)}
                required
                autoComplete="email"
              />
            </div>

            <button type="submit" className="primary-action" disabled={loading}>
              {loading ? 'Sending…' : 'Send Reset Code'}
            </button>
          </form>
        )}

        {step === 'reset' && (
          <form onSubmit={handleResetPassword}>
            <p style={{ color: '#8ea5d9', fontSize: '0.9rem', marginBottom: 20 }}>
              Enter the code sent to <strong style={{ color: '#f4fbff' }}>{email}</strong> and choose a new password.
            </p>

            <div className="field" style={{ marginBottom: 12 }}>
              <label>Reset Code</label>
              <input
                type="text"
                placeholder="123456"
                value={resetCode}
                onChange={e => setResetCode(e.target.value)}
                required
                inputMode="numeric"
                autoComplete="one-time-code"
              />
            </div>

            <div className="field" style={{ marginBottom: 20 }}>
              <label>New Password</label>
              <input
                type="password"
                placeholder="Choose a new password"
                value={newPassword}
                onChange={e => setNewPassword(e.target.value)}
                required
                autoComplete="new-password"
              />
            </div>

            <button type="submit" className="primary-action" disabled={loading}>
              {loading ? 'Resetting…' : 'Reset Password'}
            </button>

            <button
              type="button"
              className="secondary-action"
              disabled={loading}
              onClick={sendResetCode}
              style={{ marginTop: 12, width: '100%' }}
            >
              Resend Code
            </button>
          </form>
        )}
      </div>
    </div>
  );
}
