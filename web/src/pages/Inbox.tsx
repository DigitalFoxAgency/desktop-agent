import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { listInbox } from '../api/client';
import { connectInbox } from '../api/ws';
import { clientLabel, isActiveStatus, relativeTime, statusBadgeClass, statusLabel } from '../format';

type Filter = 'active' | 'done' | 'all';

export default function Inbox() {
  const queryClient = useQueryClient();
  const { data, isLoading, error } = useQuery({
    queryKey: ['inbox'],
    queryFn: listInbox,
    refetchInterval: 30000,
  });

  const [filter, setFilter] = useState<Filter>('active');

  useEffect(() => {
    const ws = connectInbox(() => {
      queryClient.invalidateQueries({ queryKey: ['inbox'] });
    });
    return () => ws.close();
  }, [queryClient]);

  const counts = useMemo(() => {
    const c = { active: 0, done: 0, all: 0 };
    for (const it of data ?? []) {
      c.all++;
      if (isActiveStatus(it.phaseStatus)) c.active++;
      else c.done++;
    }
    return c;
  }, [data]);

  const filtered = useMemo(() => {
    if (!data) return [];
    return data.filter((it) => {
      if (filter === 'active') return isActiveStatus(it.phaseStatus);
      if (filter === 'done') return !isActiveStatus(it.phaseStatus);
      return true;
    });
  }, [data, filter]);

  return (
    <div className="mx-auto max-w-3xl p-6">
      <h1 className="text-2xl font-semibold mb-4">Inbox</h1>

      <div className="mb-4 flex items-center gap-1 border-b">
        <Tab label="Active" count={counts.active} active={filter === 'active'} onClick={() => setFilter('active')} />
        <Tab label="Done" count={counts.done} active={filter === 'done'} onClick={() => setFilter('done')} />
        <Tab label="All" count={counts.all} active={filter === 'all'} onClick={() => setFilter('all')} />
      </div>

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
      {data && filtered.length === 0 && !isLoading && (
        <div className="rounded-lg border border-dashed bg-white p-8 text-center">
          <div className="text-3xl mb-2">{filter === 'active' ? '✨' : '📭'}</div>
          <h3 className="text-base font-medium mb-1">
            {filter === 'active' ? 'All caught up' : filter === 'done' ? 'Nothing finished yet' : 'Inbox is empty'}
          </h3>
          <p className="text-sm text-gray-600">
            {filter === 'active'
              ? 'No active phases waiting on you right now.'
              : filter === 'done'
                ? 'Completed phases will land here.'
                : 'Phases assigned to you will land here.'}
          </p>
        </div>
      )}

      <ul className="space-y-2">
        {filtered.map((it) => {
          const client = clientLabel(it.inputs);
          return (
            <li key={it.id} className="border rounded hover:border-blue-300 hover:bg-blue-50/30">
              <Link to={`/phase/${it.phaseRunId}`} className="block p-4">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0 flex-1">
                    <div className="font-medium text-sm">
                      {client ? (
                        <>
                          {client}
                          <span className="text-gray-400"> · </span>
                          <span className="text-gray-700">{it.title.replace(/^.*?—\s*/, '')}</span>
                        </>
                      ) : (
                        it.title
                      )}
                    </div>
                    <div className="mt-1 flex items-center gap-2 text-xs text-gray-600">
                      <span className={statusBadgeClass(it.phaseStatus)}>{statusLabel(it.phaseStatus)}</span>
                      {it.subtitle && <span className="text-gray-500">{it.subtitle}</span>}
                    </div>
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
          );
        })}
      </ul>
    </div>
  );
}

function Tab({ label, count, active, onClick }: { label: string; count: number; active: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`px-3 py-2 -mb-px border-b-2 text-sm transition-colors ${
        active
          ? 'border-blue-600 text-gray-900 font-medium'
          : 'border-transparent text-gray-600 hover:text-gray-900'
      }`}
    >
      {label}
      <span className={`ml-1.5 text-xs ${active ? 'text-gray-500' : 'text-gray-400'}`}>{count}</span>
    </button>
  );
}
