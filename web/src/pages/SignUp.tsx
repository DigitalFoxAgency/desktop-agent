import { useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { signUp } from '../api/client';

export default function SignUp() {
  const navigate = useNavigate();
  const [agencyName, setAgencyName] = useState('');
  const [agencySlug, setAgencySlug] = useState('');
  const [slugTouched, setSlugTouched] = useState(false);
  const [displayName, setDisplayName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // Auto-derive a slug from the agency name until the user edits it themselves.
  useEffect(() => {
    if (slugTouched) return;
    setAgencySlug(slugify(agencyName));
  }, [agencyName, slugTouched]);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      await signUp({ agencyName, agencySlug, displayName, email, password });
      navigate('/');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Sign-up failed.');
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
          <h1 className="text-2xl font-semibold">Create your agency</h1>
          <p className="text-sm text-gray-600 mt-1">Spin up your workspace in a minute</p>
        </div>

        <div className="rounded-lg border bg-white p-6 shadow-sm">
          <form onSubmit={onSubmit} className="space-y-4">
            <label className="block">
              <span className="text-sm font-medium">Agency name</span>
              <input
                required
                autoFocus
                value={agencyName}
                onChange={(e) => setAgencyName(e.target.value)}
                placeholder="Digital Fox Agency"
                className="mt-1 w-full rounded border px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
              />
            </label>
            <label className="block">
              <span className="text-sm font-medium">Agency slug</span>
              <input
                required
                pattern="[a-z0-9-]{2,64}"
                value={agencySlug}
                onChange={(e) => {
                  setSlugTouched(true);
                  setAgencySlug(e.target.value.toLowerCase());
                }}
                placeholder="digital-fox"
                className="mt-1 w-full rounded border px-3 py-2 text-sm font-mono focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
              />
              <span className="text-xs text-gray-500">Lowercase letters, digits, dashes. 2–64 characters.</span>
            </label>
            <label className="block">
              <span className="text-sm font-medium">Your name</span>
              <input
                required
                value={displayName}
                onChange={(e) => setDisplayName(e.target.value)}
                className="mt-1 w-full rounded border px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
              />
            </label>
            <label className="block">
              <span className="text-sm font-medium">Email</span>
              <input
                type="email"
                required
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
                minLength={10}
                autoComplete="new-password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                className="mt-1 w-full rounded border px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
              />
              <span className="text-xs text-gray-500">10+ characters, mixed case, at least one digit.</span>
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
              {submitting ? 'Creating…' : 'Create agency'}
            </button>
          </form>
        </div>

        <p className="mt-4 text-center text-sm text-gray-600">
          Already have an account?{' '}
          <Link to="/signin" className="text-blue-600 hover:underline font-medium">
            Sign in
          </Link>
        </p>
      </div>
    </div>
  );
}

function slugify(value: string): string {
  return value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 64);
}
