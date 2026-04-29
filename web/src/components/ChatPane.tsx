import { useEffect, useRef, useState } from 'react';

export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  text: string;
  pending?: boolean;
}

interface Props {
  messages: ChatMessage[];
  onSend: (text: string) => void;
  disabled?: boolean;
}

export default function ChatPane({ messages, onSend, disabled }: Props) {
  const [draft, setDraft] = useState('');
  const scrollRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight });
  }, [messages]);

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
        {messages.map((m) => (
          <div key={m.id} className={m.role === 'user' ? 'text-right' : ''}>
            <div
              className={`inline-block max-w-[85%] rounded px-3 py-2 text-sm whitespace-pre-wrap ${
                m.role === 'user' ? 'bg-blue-600 text-white' : 'bg-gray-100 text-gray-900'
              } ${m.pending ? 'opacity-60' : ''}`}
            >
              {m.text}
            </div>
          </div>
        ))}
      </div>
      <form onSubmit={submit} className="border-t p-3 flex gap-2">
        <input
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          placeholder={disabled ? 'Disconnected' : 'Type a message…'}
          disabled={disabled}
          className="flex-1 border rounded px-3 py-2 text-sm"
        />
        <button
          type="submit"
          disabled={disabled || !draft.trim()}
          className="rounded bg-blue-600 px-3 py-2 text-white text-sm disabled:opacity-50"
        >
          Send
        </button>
      </form>
    </div>
  );
}
