import { Link, useRouteError } from 'react-router-dom';
import { useEffect, useState } from 'react';

export function NotFound() {
  return (
    <ErrorShell
      title="Page not found"
      message="The page you were looking for doesn't exist or you don't have access to it."
      status="404"
    />
  );
}

export function ServerError() {
  const error = useRouteError() as { status?: number; statusText?: string; message?: string } | undefined;
  return (
    <ErrorShell
      title="Something went wrong"
      message={error?.statusText ?? error?.message ?? 'An unexpected error occurred. Try refreshing the page.'}
      status={error?.status?.toString() ?? '500'}
    />
  );
}

export function OfflineBanner() {
  // Light-touch banner that pops up when the browser reports offline. Doesn't
  // try to reconnect WebSockets — the per-page hooks already handle that.
  const [online, setOnline] = useState<boolean>(typeof navigator === 'undefined' ? true : navigator.onLine);

  useEffect(() => {
    const onUp = () => setOnline(true);
    const onDown = () => setOnline(false);
    window.addEventListener('online', onUp);
    window.addEventListener('offline', onDown);
    return () => {
      window.removeEventListener('online', onUp);
      window.removeEventListener('offline', onDown);
    };
  }, []);

  if (online) return null;
  return (
    <div className="fixed bottom-3 left-1/2 -translate-x-1/2 z-50 bg-amber-50 text-amber-900 border border-amber-200 rounded-md px-3 py-1.5 text-xs shadow">
      You are offline. Reconnecting…
    </div>
  );
}

function ErrorShell({ title, message, status }: { title: string; message: string; status: string }) {
  return (
    <div className="min-h-screen flex items-center justify-center p-6">
      <div className="text-center max-w-md">
        <div className="text-5xl font-bold text-gray-300 mb-2">{status}</div>
        <h1 className="text-xl font-semibold mb-2">{title}</h1>
        <p className="text-sm text-gray-600 mb-6">{message}</p>
        <Link to="/" className="inline-block rounded bg-blue-600 text-white text-sm px-4 py-2 hover:bg-blue-700">
          Back to dashboard
        </Link>
      </div>
    </div>
  );
}
