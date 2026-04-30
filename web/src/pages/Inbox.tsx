import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { Link } from 'react-router-dom';
import { listInbox } from '../api/client';
import { connectInbox } from '../api/ws';

export default function Inbox() {
  const queryClient = useQueryClient();
  const { data, isLoading, error } = useQuery({ queryKey: ['inbox'], queryFn: listInbox, refetchInterval: 30000 });

  useEffect(() => {
    const ws = connectInbox(() => {
      queryClient.invalidateQueries({ queryKey: ['inbox'] });
    });
    return () => ws.close();
  }, [queryClient]);

  return (
    <div className="mx-auto max-w-3xl p-6">
      <header className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-semibold">Inbox</h1>
        <Link to="/" className="text-sm underline">Back to dashboard</Link>
      </header>
      {isLoading && <p className="text-sm text-gray-500">Loading…</p>}
      {error && <p className="text-sm text-red-600">{(error as Error).message}</p>}
      {data && data.length === 0 && (
        <p className="text-sm text-gray-600">Nothing assigned to you yet.</p>
      )}
      <ul className="space-y-2">
        {data?.map((it) => (
          <li key={it.id} className="border rounded p-4 hover:bg-gray-50">
            <Link to={`/phase/${it.phaseRunId}`} className="block">
              <div className="font-medium">{it.title}</div>
              {it.subtitle && <div className="text-sm text-gray-600">{it.subtitle}</div>}
              <div className="text-xs text-gray-400 mt-1">{new Date(it.createdAt).toLocaleString()}</div>
            </Link>
          </li>
        ))}
      </ul>
    </div>
  );
}
