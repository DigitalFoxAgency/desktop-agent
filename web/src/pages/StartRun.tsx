import { useEffect, useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { listModules, startRun, type WorkflowSummary } from '../api/client';

export default function StartRun() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const presetModule = params.get('moduleId');
  const presetWorkflow = params.get('workflowId');

  const { data: modules, isLoading } = useQuery({ queryKey: ['modules'], queryFn: listModules });

  const [moduleId, setModuleId] = useState(presetModule ?? '');
  const [workflowId, setWorkflowId] = useState(presetWorkflow ?? '');
  const [inputs, setInputs] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (!moduleId && modules && modules.length > 0) {
      setModuleId(modules[0].id);
    }
  }, [modules, moduleId]);

  const selectedModule = useMemo(
    () => modules?.find((m) => m.id === moduleId),
    [modules, moduleId],
  );
  const selectedWorkflow: WorkflowSummary | undefined = useMemo(
    () => selectedModule?.workflows.find((w) => w.id === workflowId),
    [selectedModule, workflowId],
  );

  useEffect(() => {
    if (selectedModule && !selectedWorkflow && selectedModule.workflows.length > 0) {
      setWorkflowId(selectedModule.workflows[0].id);
    }
  }, [selectedModule, selectedWorkflow]);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      await startRun({ moduleId, workflowId, inputs });
      navigate('/');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to start.');
    } finally {
      setSubmitting(false);
    }
  }

  if (isLoading) return <p className="p-6 text-sm text-gray-500">Loading modules…</p>;

  return (
    <div className="mx-auto max-w-2xl p-6">
      <h1 className="text-2xl font-semibold mb-6">Start a workflow</h1>
      <form onSubmit={onSubmit} className="space-y-4">
        <label className="block">
          <span className="text-sm font-medium">Module</span>
          <select
            value={moduleId}
            onChange={(e) => {
              setModuleId(e.target.value);
              setWorkflowId('');
              setInputs({});
            }}
            className="mt-1 w-full rounded border px-3 py-2"
          >
            {modules?.map((m) => (
              <option key={m.id} value={m.id}>
                {m.name}
              </option>
            ))}
          </select>
        </label>

        <label className="block">
          <span className="text-sm font-medium">Workflow</span>
          <select
            value={workflowId}
            onChange={(e) => {
              setWorkflowId(e.target.value);
              setInputs({});
            }}
            className="mt-1 w-full rounded border px-3 py-2"
          >
            {selectedModule?.workflows.map((w) => (
              <option key={w.id} value={w.id}>
                {w.name}
              </option>
            ))}
          </select>
        </label>

        {selectedWorkflow?.description && (
          <p className="text-sm text-gray-600">{selectedWorkflow.description}</p>
        )}

        {selectedWorkflow?.inputs.map((input) => (
          <label key={input.name} className="block">
            <span className="text-sm font-medium">
              {input.name}
              {input.required && <span className="text-red-600"> *</span>}
            </span>
            <input
              required={input.required}
              value={inputs[input.name] ?? ''}
              onChange={(e) => setInputs({ ...inputs, [input.name]: e.target.value })}
              className="mt-1 w-full rounded border px-3 py-2"
            />
            {input.description && (
              <span className="text-xs text-gray-500">{input.description}</span>
            )}
          </label>
        ))}

        {error && <p className="text-sm text-red-600">{error}</p>}
        <div className="flex gap-3">
          <button
            type="submit"
            disabled={submitting || !workflowId}
            className="rounded bg-blue-600 px-4 py-2 text-white disabled:opacity-50"
          >
            {submitting ? 'Starting…' : 'Start run'}
          </button>
          <button type="button" onClick={() => navigate('/')} className="text-sm underline">
            Cancel
          </button>
        </div>
      </form>
    </div>
  );
}
