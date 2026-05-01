import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import {
  assignWaiting,
  getRun,
  getSession,
  listModules,
  listTenantUsers,
  reassignPhase,
  type ModuleSummary,
  type PhaseSummary,
  type RunDetail,
  type TenantUser,
} from '../api/client';
import {
  isActiveStatus,
  isTerminalStatus,
  relativeTime,
  statusBadgeClass,
  statusKind,
  statusLabel,
} from '../format';

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
  const { data: modules } = useQuery({ queryKey: ['modules'], queryFn: listModules });

  if (isLoading) return <div className="p-6 text-sm text-gray-500">Loading…</div>;
  if (error) return <div className="p-6 text-sm text-red-600">{(error as Error).message}</div>;
  if (!run) return <div className="p-6 text-sm">Run not found.</div>;

  const moduleName = modules?.find((m) => m.id === run.moduleId)?.name ?? run.moduleId;
  const workflow = modules?.find((m) => m.id === run.moduleId)?.workflows.find((w) => w.id === run.workflowId);
  const workflowName = workflow?.name ?? run.workflowId;

  // Merge run phases (live status) with the workflow definition (full ordered phase
  // list with display names + role). The API only sends phases that have actually
  // been queued; we want to show the whole pipeline including not-yet-queued ones.
  const pipeline = buildPipeline(run, workflow);

  const waiting = statusKind(run.status) === 'waiting';
  const waitingPhase = waiting ? run.phases.find((p) => statusKind(p.status) === 'pending') : null;

  return (
    <div className="mx-auto max-w-3xl p-6">
      <header className="mb-6">
        <div className="text-sm text-gray-500">{moduleName}</div>
        <h1 className="text-2xl font-semibold leading-tight">{workflowName}</h1>
        <div className="mt-1 flex items-center gap-2 text-sm text-gray-600">
          <span className={statusBadgeClass(run.status)}>{statusLabel(run.status)}</span>
          <span>·</span>
          <span title={new Date(run.startedAt).toLocaleString()}>Started {relativeTime(run.startedAt)}</span>
          {run.completedAt && (
            <>
              <span>·</span>
              <span title={new Date(run.completedAt).toLocaleString()}>Completed {relativeTime(run.completedAt)}</span>
            </>
          )}
        </div>
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

      <ol className="space-y-1">
        {pipeline.map((step, i) => (
          <PipelineRow
            key={step.key}
            step={step}
            index={i}
            isLast={i === pipeline.length - 1}
            runId={run.id}
            isAdmin={isAdmin}
            tenantId={session?.tenantId ?? ''}
            onChanged={() => queryClient.invalidateQueries({ queryKey: ['run', id] })}
          />
        ))}
      </ol>
    </div>
  );
}

interface PipelineStep {
  key: string;
  phaseRunId: string | null; // null when not yet queued
  phaseId: string;
  displayName: string;
  role: string;
  order: number;
  status: number | null;
}

function buildPipeline(run: RunDetail, workflow: ModuleSummary['workflows'][number] | undefined): PipelineStep[] {
  const liveByPhaseId = new Map(run.phases.map((p) => [p.phaseId, p]));
  const defs = workflow?.phases ?? [];
  if (defs.length === 0) {
    // Fall back to whatever the API gave us.
    return run.phases
      .slice()
      .sort((a, b) => a.order - b.order)
      .map((p) => ({
        key: p.id,
        phaseRunId: p.id,
        phaseId: p.phaseId,
        displayName: p.phaseId,
        role: '',
        order: p.order,
        status: p.status,
      }));
  }
  return defs
    .slice()
    .sort((a, b) => orderOf(defs, a) - orderOf(defs, b))
    .map((d, idx) => {
      const live = liveByPhaseId.get(d.id);
      return {
        key: live?.id ?? `def-${d.id}`,
        phaseRunId: live?.id ?? null,
        phaseId: d.id,
        displayName: d.name,
        role: d.role,
        order: idx,
        status: live?.status ?? null,
      };
    });
}

function orderOf(defs: PhaseSummary[], d: PhaseSummary): number {
  // Workflows declare their phases in order — preserve declaration order.
  return defs.indexOf(d);
}

function PipelineRow({
  step,
  index,
  isLast,
  isAdmin,
  tenantId,
  onChanged,
}: {
  step: PipelineStep;
  index: number;
  isLast: boolean;
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
    mutationFn: (userId: string) => {
      if (!step.phaseRunId) {
        throw new Error('Phase has not been queued yet — cannot reassign.');
      }
      return reassignPhase(step.phaseRunId, userId);
    },
    onSuccess: () => {
      setPicking(false);
      onChanged();
    },
  });

  const k = step.status === null ? 'queued' : statusKind(step.status);
  const canOpen = step.phaseRunId !== null && k !== 'queued';
  const canReassign = isAdmin && step.phaseRunId !== null && !isTerminalStatus(step.status);
  const isActive = isActiveStatus(step.status);

  return (
    <li className="relative flex gap-3">
      {/* Stepper rail */}
      <div className="flex flex-col items-center pt-1.5">
        <StepDot status={step.status} />
        {!isLast && <div className={`flex-1 w-px ${isActive ? 'bg-blue-300' : 'bg-gray-200'}`} />}
      </div>

      <div className="flex-1 pb-4 min-w-0">
        <div className={`rounded border p-3 ${isActive ? 'border-blue-300 bg-blue-50/30' : 'border-gray-200'}`}>
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0">
              <div className="flex items-center gap-2">
                <span className="text-xs text-gray-500 tabular-nums">{(index + 1).toString().padStart(2, '0')}</span>
                <span className="font-medium">{step.displayName}</span>
              </div>
              <div className="mt-0.5 flex items-center gap-2 text-xs text-gray-600">
                <span>{step.role}</span>
                {step.status !== null ? (
                  <>
                    <span>·</span>
                    <span className={statusBadgeClass(step.status)}>{statusLabel(step.status)}</span>
                  </>
                ) : (
                  <>
                    <span>·</span>
                    <span className="inline-block text-xs uppercase tracking-wide rounded px-2 py-0.5 border bg-gray-50 text-gray-500 border-gray-200">
                      Not started
                    </span>
                  </>
                )}
              </div>
            </div>

            <div className="flex items-center gap-1.5 shrink-0">
              {canOpen && (
                <Link
                  to={`/phase/${step.phaseRunId}`}
                  className="rounded bg-blue-600 px-2.5 py-1 text-xs text-white hover:bg-blue-700"
                >
                  Open
                </Link>
              )}
              {canReassign && !picking && (
                <button
                  type="button"
                  onClick={() => setPicking(true)}
                  className="rounded border px-2.5 py-1 text-xs hover:bg-gray-50"
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
        </div>
      </div>
    </li>
  );
}

function StepDot({ status }: { status: number | null }) {
  const k = status === null ? 'queued' : statusKind(status);
  const base = 'w-3 h-3 rounded-full border-2';
  if (k === 'completed') return <div className={`${base} bg-green-500 border-green-500`} />;
  if (k === 'failed') return <div className={`${base} bg-red-500 border-red-500`} />;
  if (k === 'running') return <div className={`${base} bg-blue-500 border-blue-500 animate-pulse`} />;
  if (k === 'paused' || k === 'waiting') return <div className={`${base} bg-amber-400 border-amber-400`} />;
  if (k === 'pending') return <div className={`${base} bg-white border-blue-400`} />;
  return <div className={`${base} bg-white border-gray-300`} />;
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
      <div className="font-medium text-amber-900">Waiting for assignment: {phaseId}</div>
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
              {u.displayName || u.email}{' '}
              <span className="text-xs text-gray-500">({u.roles.join(', ') || 'no roles'})</span>
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
