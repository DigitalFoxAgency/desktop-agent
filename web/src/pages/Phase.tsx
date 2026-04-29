import { useEffect, useMemo, useRef, useState } from 'react';
import { useParams, Link, useNavigate } from 'react-router-dom';
import ChatPane, { type ChatMessage } from '../components/ChatPane';
import FileTree from '../components/FileTree';
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
                assistantBufferRef.current += evt.text;
                setMessages((prev) => upsertAssistant(prev, assistantBufferRef.current));
                break;
              case 'assistant_turn_complete':
                assistantBufferRef.current = '';
                setMessages((prev) => prev.map((m) => (m.pending ? { ...m, pending: false } : m)));
                break;
              case 'file_changed':
                setRefreshSignal((s) => s + 1);
                break;
              case 'token_usage':
                setUsage({ input: evt.inputTokens, output: evt.outputTokens });
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
    socketRef.current.send(text);
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
      {error && (
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
      )}
      <main className="flex-1 grid grid-cols-[1fr_320px] gap-4 p-4 min-h-0">
        <ChatPane messages={messages} onSend={sendUser} disabled={!connected} />
        {phaseRunId && opened ? (
          <FileTree phaseRunId={phaseRunId} refreshSignal={refreshSignal} />
        ) : (
          <div className="border rounded h-full flex items-center justify-center text-sm text-gray-500 p-4 text-center">
            Files will appear once the phase session is running.
          </div>
        )}
      </main>
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
