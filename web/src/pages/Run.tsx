import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import {
  assignWaiting,
  getRun,
  getSession,
  listTenantUsers,
  reassignPhase,
  type RunDetail,
  type TenantUser,
} from '../api/client';

export default function Run() {
  const { id } = useParams<{ id: string }>();
  const queryClient = useQueryClient();
  const session = getSession();
  const isAdmin = session?.roles.includes('admin') ?? false;

  const { data: run, isLoading, error } = useQuery({
    queryKey: ['run', id],
    queryFn: () => getRun(id!),
    enabled: !!id,
    refetchInterval: 5000,
  });

  if (isLoading) return <div className="p-6 text-sm text-gray-500">Loading…</div>;
  if (error) return <div className="p-6 text-sm text-red-600">{(error as Error).message}</div>;
  if (!run) return <div className="p-6 text-sm">Run not found.</div>;

  const waiting = run.status === 'Waiting';
  const waitingPhase = waiting ? run.phases.find((p) => p.status === 'Pending') : null;

  return (
    <div className="mx-auto max-w-3xl p-6">
      <header className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold">
            {run.moduleId} / {run.workflowId}
          </h1>
          <div className="text-sm text-gray-600">Status: {run.status}</div>
        </div>
        <Link to="/" className="text-sm underline">Back</Link>
      </header>

      {waiting && waitingPhase && (
        <WaitingBanner
          runId={run.id}
          phaseRunId={waitingPhase.id}
          phaseId={waitingPhase.phaseId}
          tenantId={session?.tenantId ?? ''}
          isAdmin={isAdmin}
          onAssigned={() => queryClient.invalidateQueries({ queryKey: ['run', id] })}
        />
      )}

      <ul className="space-y-2">
        {run.phases.sort((a, b) => a.order - b.order).map((p) => (
          <PhaseRow
            key={p.id}
            phase={p}
            runId={run.id}
            isAdmin={isAdmin}
            tenantId={session?.tenantId ?? ''}
            onChanged={() => queryClient.invalidateQueries({ queryKey: ['run', id] })}
          />
        ))}
      </ul>
    </div>
  );
}

function WaitingBanner({
  runId,
  phaseRunId,
  phaseId,
  tenantId,
  isAdmin,
  onAssigned,
}: {
  runId: string;
  phaseRunId: string;
  phaseId: string;
  tenantId: string;
  isAdmin: boolean;
  onAssigned: () => void;
}) {
  const [open, setOpen] = useState(false);
  const { data: users } = useQuery({
    queryKey: ['tenant-users', tenantId],
    queryFn: () => listTenantUsers(tenantId),
    enabled: open && !!tenantId,
  });
  const mutation = useMutation({
    mutationFn: (userId: string) => assignWaiting(runId, phaseRunId, userId),
    onSuccess: () => {
      setOpen(false);
      onAssigned();
    },
  });

  return (
    <div className="mb-4 rounded border border-amber-300 bg-amber-50 p-4">
      <div className="font-medium text-amber-900">
        Waiting for assignment: <code>{phaseId}</code>
      </div>
      <div className="text-sm text-amber-800">
        No user with the required role is available. {isAdmin ? 'Pick someone to take this phase.' : 'An admin must assign this phase.'}
      </div>
      {isAdmin && (
        <div className="mt-3">
          {!open ? (
            <button
              type="button"
              onClick={() => setOpen(true)}
              className="rounded bg-amber-600 px-3 py-1 text-sm text-white hover:bg-amber-700"
            >
              Assign user
            </button>
          ) : (
            <UserPicker users={users} onPick={(u) => mutation.mutate(u.id)} onCancel={() => setOpen(false)} />
          )}
          {mutation.error && <div className="mt-2 text-sm text-red-600">{(mutation.error as Error).message}</div>}
        </div>
      )}
    </div>
  );
}

function PhaseRow({
  phase,
  isAdmin,
  tenantId,
  onChanged,
}: {
  phase: RunDetail['phases'][number];
  runId: string;
  isAdmin: boolean;
  tenantId: string;
  onChanged: () => void;
}) {
  const [picking, setPicking] = useState(false);
  const { data: users } = useQuery({
    queryKey: ['tenant-users', tenantId],
    queryFn: () => listTenantUsers(tenantId),
    enabled: picking && !!tenantId,
  });
  const mutation = useMutation({
    mutationFn: (userId: string) => reassignPhase(phase.id, userId),
    onSuccess: () => {
      setPicking(false);
      onChanged();
    },
  });

  const canReassign = isAdmin && phase.status !== 'Completed' && phase.status !== 'Failed';

  return (
    <li className="rounded border p-4">
      <div className="flex items-center justify-between">
        <div>
          <div className="font-medium">{phase.phaseId}</div>
          <div className="text-xs text-gray-500">Status: {phase.status} · Order: {phase.order}</div>
        </div>
        <div className="flex items-center gap-2">
          <Link to={`/phase/${phase.id}`} className="text-sm underline">Open</Link>
          {canReassign && !picking && (
            <button
              type="button"
              onClick={() => setPicking(true)}
              className="rounded border px-2 py-0.5 text-xs hover:bg-gray-50"
            >
              Reassign
            </button>
          )}
        </div>
      </div>
      {picking && (
        <div className="mt-3">
          <UserPicker users={users} onPick={(u) => mutation.mutate(u.id)} onCancel={() => setPicking(false)} />
          {mutation.error && <div className="mt-2 text-sm text-red-600">{(mutation.error as Error).message}</div>}
        </div>
      )}
    </li>
  );
}

function UserPicker({
  users,
  onPick,
  onCancel,
}: {
  users: TenantUser[] | undefined;
  onPick: (user: TenantUser) => void;
  onCancel: () => void;
}) {
  const sorted = useMemo(() => (users ?? []).slice().sort((a, b) => a.email.localeCompare(b.email)), [users]);
  return (
    <div className="rounded border bg-white p-3">
      <div className="mb-2 text-sm font-medium">Pick a user</div>
      {!users && <div className="text-xs text-gray-500">Loading users…</div>}
      <ul className="space-y-1">
        {sorted.map((u) => (
          <li key={u.id} className="flex items-center justify-between text-sm">
            <span>
              {u.displayName || u.email} <span className="text-xs text-gray-500">({u.roles.join(', ') || 'no roles'})</span>
            </span>
            <button
              type="button"
              onClick={() => onPick(u)}
              className="rounded bg-gray-900 px-2 py-0.5 text-xs text-white hover:bg-gray-700"
            >
              Choose
            </button>
          </li>
        ))}
      </ul>
      <button type="button" onClick={onCancel} className="mt-3 text-xs underline">Cancel</button>
    </div>
  );
}
