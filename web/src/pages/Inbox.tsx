import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { Link } from 'react-router-dom';
import { listInbox } from '../api/client';
import { connectInbox } from '../api/ws';
import { relativeTime } from '../format';

export default function Inbox() {
  const queryClient = useQueryClient();
  const { data, isLoading, error } = useQuery({
    queryKey: ['inbox'],
    queryFn: listInbox,
    refetchInterval: 30000,
  });

  useEffect(() => {
    const ws = connectInbox(() => {
      queryClient.invalidateQueries({ queryKey: ['inbox'] });
    });
    return () => ws.close();
  }, [queryClient]);

  return (
    <div className="mx-auto max-w-3xl p-6">
      <h1 className="text-2xl font-semibold mb-6">Inbox</h1>

      {isLoading && (
        <ul className="space-y-2">
          {[0, 1, 2].map((i) => (
            <li key={i} className="border rounded p-4">
              <div className="h-3 w-48 bg-gray-200 rounded animate-pulse mb-2" />
              <div className="h-2.5 w-32 bg-gray-100 rounded animate-pulse" />
            </li>
          ))}
        </ul>
      )}
      {error && (
        <div className="rounded border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
          {(error as Error).message}
        </div>
      )}
      {data && data.length === 0 && (
        <div className="rounded-lg border border-dashed bg-white p-8 text-center">
          <div className="text-3xl mb-2">📭</div>
          <h3 className="text-base font-medium mb-1">Inbox is empty</h3>
          <p className="text-sm text-gray-600">Phases assigned to you will land here.</p>
        </div>
      )}

      {/*
        TODO: filter active-vs-done + show client name. Both need the API to
        either include phase status / run inputs in the inbox payload, or
        return inbox items grouped by run. Skipped here to avoid an API
        restart during the live phase walker run.
      */}
      <ul className="space-y-2">
        {data?.map((it) => (
          <li key={it.id} className="border rounded hover:border-blue-300 hover:bg-blue-50/30">
            <Link to={`/phase/${it.phaseRunId}`} className="block p-4">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <div className="font-medium text-sm">{it.title}</div>
                  {it.subtitle && (
                    <div className="text-xs text-gray-600 mt-0.5">{it.subtitle}</div>
                  )}
                </div>
                <div
                  className="shrink-0 text-xs text-gray-500"
                  title={new Date(it.createdAt).toLocaleString()}
                >
                  {relativeTime(it.createdAt)}
                </div>
              </div>
            </Link>
          </li>
        ))}
      </ul>
    </div>
  );
}
