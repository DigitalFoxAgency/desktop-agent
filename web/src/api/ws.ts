import { inboxSocketUrl, phaseSocketUrl } from './client';

export type InboxEvent = {
  type: 'inbox_item_added';
  id: string;
  phaseRunId: string;
  kind: string;
  title: string;
  subtitle: string | null;
  createdAt: string;
};

export function connectInbox(onEvent: (e: InboxEvent) => void): { close: () => void } {
  const ws = new WebSocket(inboxSocketUrl());
  ws.onmessage = (msg) => {
    try {
      const parsed = JSON.parse(msg.data) as InboxEvent;
      onEvent(parsed);
    } catch {
      // ignore malformed frames
    }
  };
  return {
    close() {
      if (ws.readyState === WebSocket.OPEN) ws.close(1000, 'bye');
    },
  };
}

export type PhaseEvent =
  | { type: 'assistant_chunk'; text: string }
  | { type: 'assistant_turn_complete' }
  | { type: 'file_changed'; path: string; kind: 'created' | 'modified' | 'deleted' }
  | {
      type: 'confirmation_request';
      confirmationId: string;
      classification: string;
      summary: string;
      targetPath: string | null;
      commandLine: string | null;
    }
  | {
      type: 'token_usage';
      model: string;
      inputTokens: number;
      outputTokens: number;
      cacheCreationTokens: number;
      cacheReadTokens: number;
    }
  | { type: 'phase_completed'; skill: string; verified: boolean };

export interface PhaseSocket {
  send: (text: string) => void;
  decide: (confirmationId: string, confirmed: boolean, note?: string) => void;
  close: () => void;
}

export function connectPhase(
  phaseRunId: string,
  onEvent: (e: PhaseEvent) => void,
  onError?: (err: Event) => void,
  onClose?: () => void,
): PhaseSocket {
  const ws = new WebSocket(phaseSocketUrl(phaseRunId));
  ws.onmessage = (msg) => {
    try {
      const parsed = JSON.parse(msg.data) as PhaseEvent;
      onEvent(parsed);
    } catch {
      // ignore malformed frames
    }
  };
  if (onError) ws.onerror = onError;
  if (onClose) ws.onclose = onClose;

  return {
    send(text) {
      if (ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify({ type: 'user_input', text }));
      }
    },
    decide(confirmationId, confirmed, note) {
      if (ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify({ type: 'confirmation_decision', confirmationId, confirmed, note }));
      }
    },
    close() {
      if (ws.readyState === WebSocket.OPEN) ws.close(1000, 'bye');
    },
  };
}
