import { useEffect, useRef, useState } from 'react';

export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant' | 'system';
  text: string;
  pending?: boolean;
}

interface Props {
  messages: ChatMessage[];
  onSend: (text: string) => void;
  disabled?: boolean;
  thinking?: boolean;
}

export default function ChatPane({ messages, onSend, disabled, thinking }: Props) {
  const [draft, setDraft] = useState('');
  const scrollRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight });
  }, [messages, thinking]);

  function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!draft.trim() || disabled) return;
    onSend(draft);
    setDraft('');
  }

  return (
    <div className="flex flex-col h-full border rounded">
      <div ref={scrollRef} className="flex-1 overflow-y-auto p-4 space-y-3">
        {messages.length === 0 && (
          <p className="text-sm text-gray-500">
            {disabled ? 'Disconnected — start the session to chat.' : 'Waiting for Claude…'}
          </p>
        )}
        {messages.map((m) => {
          if (m.role === 'system') {
            return (
              <div key={m.id} className="text-xs text-gray-500 px-1 py-0.5 italic">
                {m.text}
              </div>
            );
          }
          return (
            <div key={m.id} className={m.role === 'user' ? 'text-right' : ''}>
              <div
                className={`inline-block max-w-[85%] rounded px-3 py-2 text-sm whitespace-pre-wrap ${
                  m.role === 'user' ? 'bg-blue-600 text-white' : 'bg-gray-100 text-gray-900'
                } ${m.pending ? 'opacity-60' : ''}`}
              >
                {m.text}
              </div>
            </div>
          );
        })}
        {thinking && (
          <div className="flex items-center gap-2 text-sm text-gray-500" aria-live="polite">
            <span className="inline-flex gap-1">
              <span className="w-1.5 h-1.5 rounded-full bg-gray-400 animate-bounce" style={{ animationDelay: '0ms' }} />
              <span className="w-1.5 h-1.5 rounded-full bg-gray-400 animate-bounce" style={{ animationDelay: '150ms' }} />
              <span className="w-1.5 h-1.5 rounded-full bg-gray-400 animate-bounce" style={{ animationDelay: '300ms' }} />
            </span>
            <span>Claude is thinking…</span>
          </div>
        )}
      </div>
      <form onSubmit={submit} className="border-t p-3 flex gap-2">
        <input
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={(e) => {
            // Cmd/Ctrl+Enter sends. Plain Enter already submits via the form.
            if ((e.metaKey || e.ctrlKey) && e.key === 'Enter') {
              e.preventDefault();
              if (draft.trim() && !disabled) {
                onSend(draft);
                setDraft('');
              }
            }
          }}
          placeholder={disabled ? 'Disconnected' : 'Type a message… (Enter to send)'}
          disabled={disabled}
          className="flex-1 border rounded px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent disabled:bg-gray-50"
        />
        <button
          type="submit"
          disabled={disabled || !draft.trim()}
          className="rounded bg-blue-600 px-3 py-2 text-white text-sm disabled:opacity-50 hover:bg-blue-700"
        >
          Send
        </button>
      </form>
    </div>
  );
}
