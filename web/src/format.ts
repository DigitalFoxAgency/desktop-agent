// Shared formatters. Status enums + slug → display-name lookup + relative
// dates. Keep client-side until the API surfaces these directly.

// Mirror of AgentPlatform.Domain.Runs.RunStatus.
export const STATUS_NAMES: Record<number, string> = {
  0: 'Pending',
  1: 'Waiting',
  2: 'Running',
  3: 'Paused',
  4: 'Completed',
  5: 'Failed',
};

export type StatusKind = 'pending' | 'waiting' | 'running' | 'paused' | 'completed' | 'failed' | 'unknown';

export function statusKind(value: number | string | undefined | null): StatusKind {
  if (value === undefined || value === null) return 'unknown';
  const n = typeof value === 'number' ? value : Number(value);
  if (!Number.isFinite(n)) return 'unknown';
  return (STATUS_NAMES[n]?.toLowerCase() as StatusKind) ?? 'unknown';
}

export function statusLabel(value: number | string | undefined | null): string {
  if (value === undefined || value === null) return '—';
  const n = typeof value === 'number' ? value : Number(value);
  return STATUS_NAMES[n] ?? String(value);
}

const STATUS_BADGE: Record<StatusKind, string> = {
  pending: 'bg-gray-100 text-gray-700 border-gray-200',
  waiting: 'bg-amber-50 text-amber-800 border-amber-200',
  running: 'bg-blue-50 text-blue-800 border-blue-200',
  paused: 'bg-amber-50 text-amber-800 border-amber-200',
  completed: 'bg-green-50 text-green-800 border-green-200',
  failed: 'bg-red-50 text-red-800 border-red-200',
  unknown: 'bg-gray-100 text-gray-600 border-gray-200',
};

export function statusBadgeClass(value: number | string | undefined | null): string {
  return `inline-block text-xs uppercase tracking-wide rounded px-2 py-0.5 border ${STATUS_BADGE[statusKind(value)]}`;
}

// Relative time. "just now", "2 min ago", "3 h ago", "yesterday at 14:32", "Mar 12".
export function relativeTime(iso: string | null | undefined): string {
  if (!iso) return '';
  const t = new Date(iso).getTime();
  if (!Number.isFinite(t)) return '';
  const now = Date.now();
  const sec = Math.round((now - t) / 1000);
  if (sec < 0) return new Date(iso).toLocaleString();
  if (sec < 45) return 'just now';
  if (sec < 90) return '1 min ago';
  const min = Math.round(sec / 60);
  if (min < 45) return `${min} min ago`;
  if (min < 90) return '1 h ago';
  const hr = Math.round(min / 60);
  if (hr < 22) return `${hr} h ago`;
  const day = Math.round(hr / 24);
  const d = new Date(iso);
  const time = d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
  if (day === 1) return `yesterday at ${time}`;
  if (day < 7) return `${day} days ago`;
  return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}

export function isActiveStatus(value: number | string | undefined | null): boolean {
  const k = statusKind(value);
  return k === 'pending' || k === 'waiting' || k === 'running' || k === 'paused';
}

export function isTerminalStatus(value: number | string | undefined | null): boolean {
  const k = statusKind(value);
  return k === 'completed' || k === 'failed';
}
