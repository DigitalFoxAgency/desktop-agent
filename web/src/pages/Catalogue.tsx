import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { listModules, type ModuleSummary, type WorkflowSummary } from '../api/client';

export default function Catalogue() {
  const { data: modules, isLoading, error } = useQuery({ queryKey: ['modules'], queryFn: listModules });

  return (
    <div className="mx-auto max-w-4xl p-6">
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-semibold">Module catalogue</h1>
        <Link to="/" className="text-sm underline">← Dashboard</Link>
      </div>

      {isLoading && <p className="text-sm text-gray-500">Loading modules…</p>}
      {error && <p className="text-sm text-red-600">Failed to load modules.</p>}
      {!isLoading && modules && modules.length === 0 && (
        <p className="text-sm text-gray-500">No modules installed.</p>
      )}

      <div className="space-y-4">
        {modules?.map((m) => <ModuleCard key={m.id} module={m} />)}
      </div>
    </div>
  );
}

function ModuleCard({ module: m }: { module: ModuleSummary }) {
  const unavailable = m.status !== 'Available';
  return (
    <div className={`border rounded-lg p-4 ${unavailable ? 'bg-gray-50 opacity-75' : 'bg-white'}`}>
      <div className="flex items-start justify-between mb-2">
        <div>
          <h2 className="text-lg font-medium">{m.name}</h2>
          <code className="text-xs text-gray-500">{m.id}</code>
        </div>
        {unavailable ? (
          <span className="inline-block text-xs uppercase tracking-wide text-red-700 bg-red-50 border border-red-200 rounded px-2 py-0.5">
            Unavailable{m.unavailableReason ? `: ${m.unavailableReason}` : ''}
          </span>
        ) : (
          <span className="inline-block text-xs uppercase tracking-wide text-green-700 bg-green-50 border border-green-200 rounded px-2 py-0.5">
            Available
          </span>
        )}
      </div>

      {m.workflows.length === 0 ? (
        <p className="text-sm text-gray-500">No workflows declared.</p>
      ) : (
        <ul className="mt-3 space-y-2">
          {m.workflows.map((w) => (
            <WorkflowRow key={w.id} moduleId={m.id} workflow={w} disabled={unavailable} />
          ))}
        </ul>
      )}
    </div>
  );
}

function WorkflowRow({ moduleId, workflow: w, disabled }: { moduleId: string; workflow: WorkflowSummary; disabled: boolean }) {
  const startHref = `/runs/new?moduleId=${encodeURIComponent(moduleId)}&workflowId=${encodeURIComponent(w.id)}`;
  return (
    <li className="border rounded p-3 flex items-start justify-between gap-4">
      <div className="min-w-0">
        <div className="flex items-center gap-2">
          <span className="font-medium text-sm">{w.name}</span>
          <code className="text-xs text-gray-500">{w.id}</code>
        </div>
        {w.description && <p className="text-xs text-gray-600 mt-1">{w.description}</p>}
        <p className="text-xs text-gray-500 mt-1">
          {w.phases.length} phase{w.phases.length === 1 ? '' : 's'}
          {' · '}roles: {Array.from(new Set(w.phases.map((p) => p.role))).join(', ')}
        </p>
      </div>
      {disabled ? (
        <span className="text-xs text-gray-400 shrink-0">Cannot start</span>
      ) : (
        <Link
          to={startHref}
          className="shrink-0 rounded bg-blue-600 text-white text-sm px-3 py-1.5 hover:bg-blue-700"
        >
          Start →
        </Link>
      )}
    </li>
  );
}
