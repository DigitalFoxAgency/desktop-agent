import { useEffect, useMemo, useRef, useState } from 'react';
import { useParams, Link, useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import ChatPane, { type ChatMessage } from '../components/ChatPane';
import FileTree from '../components/FileTree';
import ConfirmationDialog, { type PendingConfirmation } from '../components/ConfirmationDialog';
import {
  closePhase,
  getFile,
  getPhaseDiagnostics,
  getRun,
  listModules,
  openPhase,
  type FileContent,
  type ModuleSummary,
  type RunDetail,
} from '../api/client';
import { connectPhase, type PhaseSocket } from '../api/ws';
import { relativeTime, statusBadgeClass, statusLabel } from '../format';

interface Orientation {
  workflowRunId: string;
  workflowName: string;
  moduleName: string;
  phaseNumber: number;
  phaseTotal: number;
  phaseDisplayName: string;
  role: string;
  skill: string;
  kind: string;
  status: number | null;
}

export default function Phase() {
  const { phaseRunId } = useParams<{ phaseRunId: string }>();
  const navigate = useNavigate();
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [connected, setConnected] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [refreshSignal, setRefreshSignal] = useState(0);
  const [opened, setOpened] = useState(false);
  const [retryKey, setRetryKey] = useState(0);
  const [thinking, setThinking] = useState(false);
  const [pendingConfirms, setPendingConfirms] = useState<PendingConfirmation[]>([]);
  const [waitingBuilds, setWaitingBuilds] = useState<Set<string>>(() => new Set());
  const [workflowRunId, setWorkflowRunId] = useState<string | null>(null);
  const [previewFile, setPreviewFile] = useState<FileContent | null>(null);
  const [previewLoading, setPreviewLoading] = useState(false);
  const [previewError, setPreviewError] = useState<string | null>(null);
  const socketRef = useRef<PhaseSocket | null>(null);
  const assistantBufferRef = useRef<string>('');

  // Modules + run data drive the orientation banner. Both should be cached
  // already if the user came in from the inbox / dashboard.
  const { data: modules } = useQuery({ queryKey: ['modules'], queryFn: listModules });
  const { data: run } = useQuery({
    queryKey: ['run', workflowRunId],
    queryFn: () => getRun(workflowRunId!),
    enabled: !!workflowRunId,
    refetchInterval: 10000,
  });
  const { data: diagnostics } = useQuery({
    queryKey: ['phase-diagnostics', phaseRunId],
    queryFn: () => getPhaseDiagnostics(phaseRunId!),
    enabled: !!phaseRunId && opened,
    refetchInterval: 15000,
    retry: false,
  });

  const orientation: Orientation | null = useMemo(
    () => (run && modules && phaseRunId ? buildOrientation(run, modules, phaseRunId) : null),
    [run, modules, phaseRunId],
  );

  useEffect(() => {
    if (!phaseRunId) return;
    let cancelled = false;
    setError(null);
    openPhase(phaseRunId)
      .then((res) => {
        if (cancelled) return;
        setWorkflowRunId(res.workflowRunId);
        const sock = connectPhase(
          phaseRunId,
          (evt) => {
            switch (evt.type) {
              case 'assistant_chunk':
                setThinking(false);
                assistantBufferRef.current += evt.text;
                setMessages((prev) => upsertAssistant(prev, assistantBufferRef.current));
                break;
              case 'assistant_turn_complete':
                assistantBufferRef.current = '';
                setThinking(false);
                setMessages((prev) => prev.map((m) => (m.pending ? { ...m, pending: false } : m)));
                break;
              case 'file_changed':
                setRefreshSignal((s) => s + 1);
                setMessages((prev) => appendFileEvent(prev, evt.path, evt.kind));
                break;
              case 'token_usage':
                // Token usage is admin-relevant; hidden from the visible UI.
                // Cost surfacing belongs in a separate admin pane.
                break;
              case 'confirmation_request':
                setPendingConfirms((prev) => {
                  if (prev.some((p) => p.confirmationId === evt.confirmationId)) return prev;
                  return [
                    ...prev,
                    {
                      confirmationId: evt.confirmationId,
                      classification: evt.classification,
                      summary: evt.summary,
                      targetPath: evt.targetPath,
                      commandLine: evt.commandLine,
                    },
                  ];
                });
                break;
              case 'build_semaphore_waiting':
                setWaitingBuilds((prev) => {
                  const next = new Set(prev);
                  next.add(evt.confirmationId);
                  return next;
                });
                break;
              case 'build_semaphore_acquired':
                setWaitingBuilds((prev) => {
                  if (!prev.has(evt.confirmationId)) return prev;
                  const next = new Set(prev);
                  next.delete(evt.confirmationId);
                  return next;
                });
                break;
              case 'phase_completed':
                setMessages((prev) => [
                  ...prev,
                  {
                    id: crypto.randomUUID(),
                    role: 'assistant',
                    text: `✓ Phase complete (${evt.skill}${evt.verified ? ', verified' : ''}).`,
                  },
                ]);
                break;
              default:
                break;
            }
          },
          () => setError('connection error'),
          () => setConnected(false),
        );
        socketRef.current = sock;
        setConnected(true);
        setOpened(true);
      })
      .catch((e: Error) => {
        if (!cancelled) setError(e.message);
      });

    return () => {
      cancelled = true;
      socketRef.current?.close();
    };
  }, [phaseRunId, retryKey]);

  const sendUser = useMemo(() => (text: string) => {
    if (!socketRef.current) return;
    setMessages((prev) => [...prev, { id: crypto.randomUUID(), role: 'user', text }]);
    setThinking(true);
    socketRef.current.send(text);
  }, []);

  const decideConfirmation = useMemo(() => (confirmationId: string, confirmed: boolean, note: string | null) => {
    const sock = socketRef.current;
    if (!sock) return;
    sock.decide(confirmationId, confirmed, note ?? undefined);
    setPendingConfirms((prev) => prev.filter((p) => p.confirmationId !== confirmationId));
  }, []);

  const onSelectFile = useMemo(() => (path: string) => {
    if (!phaseRunId) return;
    setPreviewLoading(true);
    setPreviewError(null);
    getFile(phaseRunId, path)
      .then((c) => setPreviewFile(c))
      .catch((e: Error) => setPreviewError(e.message))
      .finally(() => setPreviewLoading(false));
  }, [phaseRunId]);

  async function leave() {
    if (phaseRunId) await closePhase(phaseRunId).catch(() => undefined);
    navigate('/inbox');
  }

  return (
    <div className="h-screen flex flex-col bg-gray-50">
      {/* Top app bar */}
      <header className="border-b bg-white px-4 py-2 flex items-center justify-between shrink-0">
        <div className="flex items-center gap-3 min-w-0">
          <Link to="/inbox" className="text-sm text-gray-700 hover:underline shrink-0">← Inbox</Link>
          {orientation ? (
            <>
              <span className="text-gray-300 shrink-0">/</span>
              {orientation.workflowRunId && (
                <Link to={`/runs/${orientation.workflowRunId}`} className="text-sm text-gray-700 hover:underline truncate">
                  {orientation.workflowName}
                </Link>
              )}
              <span className="text-gray-300 shrink-0">/</span>
              <span className="text-sm font-medium truncate">{orientation.phaseDisplayName}</span>
            </>
          ) : (
            <span className="text-sm font-medium">Phase session</span>
          )}
          <span
            className={`text-xs ml-1 shrink-0 ${connected ? 'text-green-600' : 'text-gray-400'}`}
            aria-live="polite"
          >
            {connected ? '● live' : '○ offline'}
          </span>
        </div>
        <button
          onClick={leave}
          className="text-sm text-gray-600 hover:text-gray-900 hover:underline shrink-0"
        >
          Close phase
        </button>
      </header>

      {/* Orientation panel — what is this phase, what's expected */}
      {orientation && (
        <section className="bg-white border-b px-4 py-3 shrink-0">
          <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-sm">
            <span className="text-xs uppercase tracking-wide text-gray-500">
              Step {orientation.phaseNumber} of {orientation.phaseTotal}
            </span>
            <span className="font-medium">{orientation.phaseDisplayName}</span>
            <span className="text-gray-300">·</span>
            <span className="text-gray-700">
              <span className="text-gray-500">Role: </span>
              {orientation.role}
            </span>
            <span className="text-gray-300">·</span>
            <span className="text-gray-700">
              <span className="text-gray-500">Skill: </span>
              <code className="text-xs">{orientation.skill}</code>
            </span>
            {orientation.kind && orientation.kind !== 'standard' && (
              <span className="inline-block text-xs uppercase tracking-wide rounded px-2 py-0.5 border bg-gray-50 text-gray-600 border-gray-200">
                {orientation.kind}
              </span>
            )}
            {orientation.status !== null && (
              <span className={statusBadgeClass(orientation.status)}>{statusLabel(orientation.status)}</span>
            )}
            {diagnostics && (
              <span
                className={`text-xs ${diagnostics.isStalled ? 'text-amber-700 font-medium' : 'text-gray-500'}`}
                title={`Last bridge event: ${diagnostics.lastEventKind} at ${new Date(diagnostics.lastEventAt).toLocaleTimeString()}`}
              >
                {diagnostics.isStalled ? '⚠ Stalled — ' : 'Last activity '}
                {relativeTime(diagnostics.lastEventAt)}
              </span>
            )}
          </div>
        </section>
      )}

      {/* Error banners */}
      {error && /Run paused/i.test(error) ? (
        <div className="bg-amber-50 text-amber-900 text-sm px-4 py-3 border-b border-amber-200 shrink-0">
          <div className="font-medium">Run paused</div>
          <div className="mt-1">{error}</div>
          <div className="mt-2 text-xs text-amber-800">
            Admins: raise the per-run cost cap (or disable it for dev) and reopen the phase.
          </div>
        </div>
      ) : error ? (
        <div className="bg-red-50 text-red-700 text-sm px-4 py-2 flex items-center justify-between shrink-0">
          <span>Couldn't open phase session: {error}</span>
          <button
            onClick={() => {
              setError(null);
              setRetryKey((k) => k + 1);
            }}
            className="ml-3 underline"
          >
            Retry
          </button>
        </div>
      ) : null}

      {waitingBuilds.size > 0 && (
        <div className="bg-blue-50 text-blue-900 text-xs px-4 py-2 border-b border-blue-200 shrink-0">
          Waiting for build slot ({waitingBuilds.size} queued, host cap = 2)…
        </div>
      )}

      {/* Main: chat | (file tree + preview) */}
      <main className="flex-1 grid grid-cols-[1fr_360px] gap-4 p-4 min-h-0">
        <ChatPane messages={messages} onSend={sendUser} disabled={!connected} thinking={thinking} />
        {phaseRunId && opened ? (
          <div className="flex flex-col gap-3 min-h-0">
            <div className="border rounded bg-white flex-1 min-h-0 overflow-hidden">
              <FileTree phaseRunId={phaseRunId} refreshSignal={refreshSignal} onSelect={onSelectFile} />
            </div>
            {(previewFile || previewLoading || previewError) && (
              <FilePreview
                file={previewFile}
                loading={previewLoading}
                error={previewError}
                onClose={() => {
                  setPreviewFile(null);
                  setPreviewError(null);
                }}
              />
            )}
          </div>
        ) : (
          <div className="border rounded h-full flex items-center justify-center text-sm text-gray-500 p-4 text-center bg-white">
            Files will appear once the phase session is running.
          </div>
        )}
      </main>

      <ConfirmationDialog pending={pendingConfirms} onDecide={decideConfirmation} />
    </div>
  );
}

function FilePreview({
  file,
  loading,
  error,
  onClose,
}: {
  file: FileContent | null;
  loading: boolean;
  error: string | null;
  onClose: () => void;
}) {
  return (
    <div className="border rounded bg-white flex flex-col max-h-[40vh] min-h-[8rem] overflow-hidden">
      <div className="flex items-center justify-between px-3 py-2 border-b bg-gray-50">
        <code className="text-xs truncate">{file?.path ?? (loading ? 'Loading…' : 'Preview')}</code>
        <button onClick={onClose} className="text-xs text-gray-500 hover:text-gray-900" aria-label="Close preview">
          ✕
        </button>
      </div>
      <div className="flex-1 overflow-auto">
        {loading && <div className="p-3 text-xs text-gray-500">Loading file…</div>}
        {error && <div className="p-3 text-xs text-red-600">{error}</div>}
        {file && !loading && !error && (
          <pre className="p-3 text-xs whitespace-pre-wrap break-words font-mono">{file.content}</pre>
        )}
      </div>
    </div>
  );
}

function buildOrientation(run: RunDetail, modules: ModuleSummary[], phaseRunId: string): Orientation | null {
  const livePhase = run.phases.find((p) => p.id === phaseRunId);
  const mod = modules.find((m) => m.id === run.moduleId);
  const wf = mod?.workflows.find((w) => w.id === run.workflowId);
  const phaseDefIndex = wf?.phases.findIndex((d) => d.id === livePhase?.phaseId) ?? -1;
  const def = phaseDefIndex >= 0 ? wf!.phases[phaseDefIndex] : undefined;
  if (!def || !wf || !mod) {
    return null;
  }
  return {
    workflowRunId: run.id,
    workflowName: wf.name,
    moduleName: mod.name,
    phaseNumber: phaseDefIndex + 1,
    phaseTotal: wf.phases.length,
    phaseDisplayName: def.name,
    role: def.role,
    skill: def.skill,
    kind: def.kind,
    status: livePhase?.status ?? null,
  };
}

function upsertAssistant(prev: ChatMessage[], text: string): ChatMessage[] {
  const last = prev[prev.length - 1];
  if (last && last.role === 'assistant' && last.pending) {
    return [...prev.slice(0, -1), { ...last, text }];
  }
  return [...prev, { id: crypto.randomUUID(), role: 'assistant', text, pending: true }];
}

const FILE_KIND_ICON: Record<string, string> = {
  created: '✚',
  modified: '✎',
  deleted: '✕',
};

function appendFileEvent(prev: ChatMessage[], path: string, kind: string): ChatMessage[] {
  // Coalesce rapid-fire writes to the same file (e.g. multi-step edits) into
  // a single most-recent system entry instead of spamming the chat.
  const icon = FILE_KIND_ICON[kind] ?? '·';
  const text = `${icon} ${kind} ${path}`;
  const last = prev[prev.length - 1];
  if (last && last.role === 'system' && last.text === text) {
    return prev;
  }
  return [...prev, { id: crypto.randomUUID(), role: 'system', text }];
}
