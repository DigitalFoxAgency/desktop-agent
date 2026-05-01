import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { listRuns, clearSession } from '../api/client';
import { useNavigate } from 'react-router-dom';

export default function Dashboard() {
  const navigate = useNavigate();
  const { data: runs, isLoading, error } = useQuery({
    queryKey: ['runs'],
    queryFn: listRuns,
  });

  function signOut() {
    clearSession();
    navigate('/signin');
  }

  return (
    <div className="mx-auto max-w-5xl p-6">
      <header className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-semibold">Agency dashboard</h1>
        <div className="flex gap-3">
          <Link to="/inbox" className="text-sm underline">Inbox</Link>
          <Link to="/catalogue" className="text-sm underline">Catalogue</Link>
          <Link to="/runs/new" className="rounded bg-blue-600 px-3 py-2 text-white text-sm">
            Start a workflow
          </Link>
          <button onClick={signOut} className="text-sm underline">Sign out</button>
        </div>
      </header>

      <h2 className="text-lg font-medium mb-2">Recent runs</h2>
      {isLoading && <p className="text-sm text-gray-500">Loading…</p>}
      {error && <p className="text-sm text-red-600">{(error as Error).message}</p>}
      {runs && runs.length === 0 && (
        <p className="text-sm text-gray-600">No runs yet. Start your first workflow.</p>
      )}
      {runs && runs.length > 0 && (
        <table className="w-full border-collapse">
          <thead>
            <tr className="text-left border-b">
              <th className="py-2">Module</th>
              <th className="py-2">Workflow</th>
              <th className="py-2">Status</th>
              <th className="py-2">Started</th>
            </tr>
          </thead>
          <tbody>
            {runs.map((r) => (
              <tr key={r.id} className="border-b">
                <td className="py-2 font-mono text-sm">{r.moduleId}</td>
                <td className="py-2">{r.workflowId}</td>
                <td className="py-2">{r.status}</td>
                <td className="py-2">{new Date(r.startedAt).toLocaleString()}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
