import { useState } from 'react';

export interface PendingConfirmation {
  confirmationId: string;
  classification: string;
  summary: string;
  targetPath: string | null;
  commandLine: string | null;
}

interface Props {
  pending: PendingConfirmation[];
  onDecide: (confirmationId: string, confirmed: boolean, note: string | null) => void;
}

// One-at-a-time queue. The bridge issues a per-occurrence id for every
// dangerous tool-use, and confirming once never auto-applies to the next
// prompt — the user always sees and chooses each one.
export default function ConfirmationDialog({ pending, onDecide }: Props) {
  const current = pending[0];
  const [note, setNote] = useState('');

  if (!current) return null;

  const handle = (confirmed: boolean) => {
    onDecide(current.confirmationId, confirmed, note.trim() ? note.trim() : null);
    setNote('');
  };

  const remaining = pending.length - 1;

  return (
    <div className="fixed inset-0 z-40 flex items-center justify-center bg-black/40">
      <div className="bg-white rounded-lg shadow-xl border max-w-md w-full p-5">
        <div className="flex items-center justify-between mb-3">
          <h2 className="text-base font-medium">Confirm action</h2>
          <span className="inline-block text-xs uppercase tracking-wide text-amber-700 bg-amber-50 border border-amber-200 rounded px-2 py-0.5">
            {current.classification}
          </span>
        </div>
        <p className="text-sm text-gray-800 mb-3">{current.summary}</p>
        {current.commandLine && (
          <pre className="text-xs bg-gray-50 border rounded px-2 py-1 mb-2 overflow-x-auto whitespace-pre-wrap break-all">
            {current.commandLine}
          </pre>
        )}
        {current.targetPath && (
          <div className="text-xs text-gray-600 mb-3">
            <span className="font-medium">Target:</span> {current.targetPath}
          </div>
        )}
        <label className="block text-xs text-gray-600 mb-1" htmlFor="confirmation-note">
          Note (optional)
        </label>
        <textarea
          id="confirmation-note"
          value={note}
          onChange={(e) => setNote(e.target.value)}
          rows={2}
          className="w-full text-sm border rounded px-2 py-1 mb-3"
          placeholder="Reason / context for the audit log"
        />
        <div className="flex items-center justify-between text-xs text-gray-500">
          <span>{remaining > 0 ? `${remaining} more pending` : 'No further prompts'}</span>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => handle(false)}
              className="px-3 py-1.5 rounded border text-sm hover:bg-gray-50"
            >
              Decline
            </button>
            <button
              type="button"
              onClick={() => handle(true)}
              className="px-3 py-1.5 rounded bg-amber-600 text-white text-sm hover:bg-amber-700"
            >
              Confirm
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
