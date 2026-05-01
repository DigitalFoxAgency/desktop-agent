import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { signIn } from '../api/client';

export default function SignIn() {
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      await signIn(email, password);
      navigate('/');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Sign-in failed.');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-50 via-blue-50 to-gray-50 flex items-center justify-center p-6">
      <div className="w-full max-w-md">
        <div className="text-center mb-8">
          <div className="inline-flex items-center justify-center w-12 h-12 rounded-xl bg-blue-600 text-white text-xl font-semibold mb-3">
            AP
          </div>
          <h1 className="text-2xl font-semibold">Welcome back</h1>
          <p className="text-sm text-gray-600 mt-1">Sign in to your agency workspace</p>
        </div>

        <div className="rounded-lg border bg-white p-6 shadow-sm">
          <form onSubmit={onSubmit} className="space-y-4">
            <label className="block">
              <span className="text-sm font-medium">Email</span>
              <input
                type="email"
                required
                autoFocus
                autoComplete="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                className="mt-1 w-full rounded border px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
              />
            </label>
            <label className="block">
              <span className="text-sm font-medium">Password</span>
              <input
                type="password"
                required
                autoComplete="current-password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                className="mt-1 w-full rounded border px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
              />
            </label>
            {error && (
              <div className="rounded border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
                {error}
              </div>
            )}
            <button
              type="submit"
              disabled={submitting}
              className="w-full rounded bg-blue-600 px-3 py-2 text-white text-sm font-medium hover:bg-blue-700 disabled:opacity-50 transition-colors"
            >
              {submitting ? 'Signing in…' : 'Sign in'}
            </button>
          </form>
        </div>

        <p className="mt-4 text-center text-sm text-gray-600">
          New here?{' '}
          <Link to="/signup" className="text-blue-600 hover:underline font-medium">
            Create an agency
          </Link>
        </p>
      </div>
    </div>
  );
}
