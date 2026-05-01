import { useEffect, useMemo, useRef, useState } from 'react';
import { useParams, Link, useNavigate } from 'react-router-dom';
import ChatPane, { type ChatMessage } from '../components/ChatPane';
import FileTree from '../components/FileTree';
import ConfirmationDialog, { type PendingConfirmation } from '../components/ConfirmationDialog';
import { closePhase, openPhase } from '../api/client';
import { connectPhase, type PhaseSocket } from '../api/ws';

export default function Phase() {
  const { phaseRunId } = useParams<{ phaseRunId: string }>();
  const navigate = useNavigate();
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [connected, setConnected] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [refreshSignal, setRefreshSignal] = useState(0);
  const [usage, setUsage] = useState<{ input: number; output: number } | null>(null);
  const [opened, setOpened] = useState(false);
  const [retryKey, setRetryKey] = useState(0);
  const [thinking, setThinking] = useState(false);
  const [pendingConfirms, setPendingConfirms] = useState<PendingConfirmation[]>([]);
  const [waitingBuilds, setWaitingBuilds] = useState<Set<string>>(() => new Set());
  const socketRef = useRef<PhaseSocket | null>(null);
  const assistantBufferRef = useRef<string>('');

  useEffect(() => {
    if (!phaseRunId) return;
    let cancelled = false;
    setError(null);
    openPhase(phaseRunId)
      .then(() => {
        if (cancelled) return;
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
                break;
              case 'token_usage':
                setUsage({ input: evt.inputTokens, output: evt.outputTokens });
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
                    text: `Phase complete (${evt.skill}, verified=${evt.verified}).`,
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

  async function leave() {
    if (phaseRunId) await closePhase(phaseRunId).catch(() => undefined);
    navigate('/inbox');
  }

  return (
    <div className="h-screen flex flex-col">
      <header className="border-b px-4 py-3 flex items-center justify-between">
        <div className="flex items-center gap-3">
          <Link to="/inbox" className="text-sm underline">← Inbox</Link>
          <h1 className="text-lg font-medium">Phase session</h1>
          <span className={`text-xs ${connected ? 'text-green-600' : 'text-gray-400'}`}>
            {connected ? '● live' : '○ offline'}
          </span>
        </div>
        <div className="flex items-center gap-3 text-xs text-gray-500">
          {usage && <span>tokens in/out: {usage.input}/{usage.output}</span>}
          <button onClick={leave} className="underline">Close phase</button>
        </div>
      </header>
      {error && /Run paused/i.test(error) ? (
        <div className="bg-amber-50 text-amber-900 text-sm px-4 py-3 border-b border-amber-200">
          <div className="font-medium">Run paused</div>
          <div className="mt-1">{error}</div>
          <div className="mt-2 text-xs text-amber-800">
            Admins: raise <code>AgentPlatform__PhaseSession__PerRunCostCapCents</code> on the API, or set
            {' '}<code>AgentPlatform__PhaseSession__DisableCostCap=true</code> for dev. Restart the API and reopen the phase.
          </div>
        </div>
      ) : error ? (
        <div className="bg-red-50 text-red-700 text-sm px-4 py-2 flex items-center justify-between">
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
        <div className="bg-blue-50 text-blue-900 text-xs px-4 py-2 border-b border-blue-200">
          Waiting for build slot ({waitingBuilds.size} queued, host cap = 2)…
        </div>
      )}
      <main className="flex-1 grid grid-cols-[1fr_320px] gap-4 p-4 min-h-0">
        <ChatPane messages={messages} onSend={sendUser} disabled={!connected} thinking={thinking} />
        {phaseRunId && opened ? (
          <FileTree phaseRunId={phaseRunId} refreshSignal={refreshSignal} />
        ) : (
          <div className="border rounded h-full flex items-center justify-center text-sm text-gray-500 p-4 text-center">
            Files will appear once the phase session is running.
          </div>
        )}
      </main>
      <ConfirmationDialog pending={pendingConfirms} onDecide={decideConfirmation} />
    </div>
  );
}

function upsertAssistant(prev: ChatMessage[], text: string): ChatMessage[] {
  const last = prev[prev.length - 1];
  if (last && last.role === 'assistant' && last.pending) {
    return [...prev.slice(0, -1), { ...last, text }];
  }
  return [...prev, { id: crypto.randomUUID(), role: 'assistant', text, pending: true }];
}
