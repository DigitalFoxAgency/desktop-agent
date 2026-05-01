import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { listRuns, listModules, type ModuleSummary } from '../api/client';
import { clientLabel, statusBadgeClass, statusLabel, relativeTime } from '../format';

export default function Dashboard() {
  const { data: runs, isLoading, error } = useQuery({
    queryKey: ['runs'],
    queryFn: listRuns,
    refetchInterval: 5000,
  });
  const { data: modules } = useQuery({ queryKey: ['modules'], queryFn: listModules });

  return (
    <div className="mx-auto max-w-5xl p-6">
      <h1 className="text-2xl font-semibold mb-6">Dashboard</h1>
      <h2 className="text-lg font-medium mb-3">Recent runs</h2>
      {isLoading && <SkeletonRows />}
      {error && (
        <div className="rounded border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
          {(error as Error).message}
        </div>
      )}
      {runs && runs.length === 0 && (
        <div className="rounded-lg border border-dashed bg-white p-8 text-center">
          <div className="text-3xl mb-2">🚀</div>
          <h3 className="text-base font-medium mb-1">No runs yet</h3>
          <p className="text-sm text-gray-600 mb-4">Pick a workflow to start your first run.</p>
          <Link
            to="/catalogue"
            className="inline-block rounded bg-blue-600 px-4 py-2 text-white text-sm hover:bg-blue-700"
          >
            Browse the catalogue
          </Link>
        </div>
      )}
      {runs && runs.length > 0 && (
        <div className="border rounded">
          <table className="w-full text-sm">
            <thead className="bg-gray-50 text-left text-xs uppercase tracking-wide text-gray-600">
              <tr>
                <th className="py-2 px-3 font-medium">Client</th>
                <th className="py-2 px-3 font-medium">Workflow</th>
                <th className="py-2 px-3 font-medium">Status</th>
                <th className="py-2 px-3 font-medium">Started</th>
                <th className="py-2 px-3"></th>
              </tr>
            </thead>
            <tbody>
              {runs.map((r) => {
                const client = clientLabel(r.inputs);
                return (
                  <tr key={r.id} className="border-t hover:bg-gray-50">
                    <td className="py-2 px-3 font-medium">{client ?? <span className="text-gray-400">—</span>}</td>
                    <td className="py-2 px-3 text-gray-700">{lookupWorkflowName(modules, r.moduleId, r.workflowId)}</td>
                    <td className="py-2 px-3">
                      <span className={statusBadgeClass(r.status)}>{statusLabel(r.status)}</span>
                    </td>
                    <td className="py-2 px-3 text-gray-600" title={new Date(r.startedAt).toLocaleString()}>
                      {relativeTime(r.startedAt)}
                    </td>
                    <td className="py-2 px-3 text-right">
                      <Link to={`/runs/${r.id}`} className="text-sm text-blue-600 hover:underline">Open →</Link>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function SkeletonRows() {
  return (
    <div className="border rounded overflow-hidden">
      <div className="bg-gray-50 px-3 py-2">
        <div className="h-3 w-24 bg-gray-200 rounded animate-pulse" />
      </div>
      {[0, 1, 2].map((i) => (
        <div key={i} className="border-t px-3 py-3 flex items-center gap-3">
          <div className="h-3 w-32 bg-gray-200 rounded animate-pulse" />
          <div className="h-3 w-40 bg-gray-200 rounded animate-pulse" />
          <div className="ml-auto h-5 w-20 bg-gray-100 rounded animate-pulse" />
        </div>
      ))}
    </div>
  );
}

function lookupModuleName(modules: ModuleSummary[] | undefined, id: string): string {
  return modules?.find((m) => m.id === id)?.name ?? id;
}

function lookupWorkflowName(modules: ModuleSummary[] | undefined, moduleId: string, workflowId: string): string {
  const m = modules?.find((mm) => mm.id === moduleId);
  return m?.workflows.find((w) => w.id === workflowId)?.name ?? workflowId;
}
