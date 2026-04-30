// Lightweight auth-aware fetch wrapper. Token + tenant live in localStorage so a refresh
// keeps you signed in. Replaced by HttpOnly cookies + refresh tokens once US3 lands.

const TOKEN_KEY = 'agp.token';
const SESSION_KEY = 'agp.session';

export interface Session {
  token: string;
  tenantId: string;
  userId: string;
  roles: string[];
}

export function getSession(): Session | null {
  const raw = localStorage.getItem(SESSION_KEY);
  if (!raw) return null;
  try {
    return JSON.parse(raw) as Session;
  } catch {
    return null;
  }
}

export function setSession(session: Session): void {
  localStorage.setItem(TOKEN_KEY, session.token);
  localStorage.setItem(SESSION_KEY, JSON.stringify(session));
}

export function clearSession(): void {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(SESSION_KEY);
}

const baseUrl = import.meta.env.VITE_API_BASE ?? '';

export async function api<T = unknown>(
  path: string,
  init: RequestInit = {},
): Promise<T> {
  const token = localStorage.getItem(TOKEN_KEY);
  const headers = new Headers(init.headers);
  headers.set('Content-Type', 'application/json');
  if (token) headers.set('Authorization', `Bearer ${token}`);

  const res = await fetch(`${baseUrl}${path}`, { ...init, headers });
  if (!res.ok) {
    let message = `${res.status} ${res.statusText}`;
    try {
      const body = (await res.json()) as { error?: string; detail?: string; title?: string };
      const detail = body.error ?? body.detail ?? body.title;
      if (detail) message = detail;
    } catch {
      // body wasn't JSON; keep status text
    }
    throw new Error(message);
  }
  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

export interface SignInResponse {
  token: string;
  tenantId: string;
  userId: string;
  roles: string[];
}

export async function signIn(email: string, password: string): Promise<Session> {
  const body = await api<SignInResponse>('/api/auth/signin', {
    method: 'POST',
    body: JSON.stringify({ email, password }),
  });
  const session: Session = body;
  setSession(session);
  return session;
}

export interface SignUpInput {
  agencyName: string;
  agencySlug: string;
  email: string;
  password: string;
  displayName: string;
}

export async function signUp(input: SignUpInput): Promise<Session> {
  const body = await api<SignInResponse>('/api/auth/signup', {
    method: 'POST',
    body: JSON.stringify(input),
  });
  setSession(body);
  return body;
}

export interface ModuleSummary {
  id: string;
  name: string;
  status: string;
  unavailableReason: string | null;
  workflows: WorkflowSummary[];
}

export interface WorkflowSummary {
  id: string;
  name: string;
  description: string | null;
  inputs: WorkflowInput[];
  phases: PhaseSummary[];
}

export interface WorkflowInput {
  name: string;
  kind: string;
  required: boolean;
  description?: string;
}

export interface PhaseSummary {
  id: string;
  name: string;
  role: string;
  skill: string;
  kind: string;
}

export const listModules = () => api<ModuleSummary[]>('/api/modules');

export interface RunListItem {
  id: string;
  moduleId: string;
  workflowId: string;
  status: string;
  startedAt: string;
  completedAt: string | null;
}

export const listRuns = () => api<RunListItem[]>('/api/runs');

export interface StartRunPayload {
  moduleId: string;
  workflowId: string;
  inputs: Record<string, string>;
}

export interface StartRunResponse {
  runId: string;
  phaseRunId: string;
  assignedUserId: string | null;
}

export const startRun = (payload: StartRunPayload) =>
  api<StartRunResponse>('/api/runs', {
    method: 'POST',
    body: JSON.stringify(payload),
  });

export interface InboxItem {
  id: string;
  phaseRunId: string;
  kind: string;
  title: string;
  subtitle: string | null;
  createdAt: string;
}

export const listInbox = () => api<InboxItem[]>('/api/inbox');

export interface OpenPhaseResponse {
  phaseRunId: string;
  workflowRunId: string;
  containerId: string;
  startedAt: string;
}

// React StrictMode mounts effects twice in dev, which would otherwise launch two
// concurrent /open calls for the same phase and lose the race inside Docker
// (container name collision). Dedupe in-flight requests per phaseRunId.
const openInFlight = new Map<string, Promise<OpenPhaseResponse>>();

export const openPhase = (phaseRunId: string): Promise<OpenPhaseResponse> => {
  const existing = openInFlight.get(phaseRunId);
  if (existing) return existing;
  const p = api<OpenPhaseResponse>(`/api/phases/${phaseRunId}/open`, { method: 'POST' })
    .finally(() => openInFlight.delete(phaseRunId));
  openInFlight.set(phaseRunId, p);
  return p;
};

export const closePhase = (phaseRunId: string) =>
  api<void>(`/api/phases/${phaseRunId}/close`, { method: 'POST' });

export interface FileEntry {
  name: string;
  path: string;
  isDirectory: boolean;
  size: number;
  modifiedAt: string | null;
}

export const listFiles = (phaseRunId: string, path = '') =>
  api<FileEntry[]>(`/api/phases/${phaseRunId}/files${path ? `?path=${encodeURIComponent(path)}` : ''}`);

export interface FileContent {
  path: string;
  size: number;
  content: string;
}

export const getFile = (phaseRunId: string, path: string) =>
  api<FileContent>(`/api/phases/${phaseRunId}/files/content?path=${encodeURIComponent(path)}`);

export function getToken(): string | null {
  return localStorage.getItem('agp.token');
}

export function phaseSocketUrl(phaseRunId: string): string {
  const token = getToken();
  const base = (import.meta.env.VITE_API_BASE ?? '').replace(/^http/, 'ws') || `${window.location.protocol === 'https:' ? 'wss:' : 'ws:'}//${window.location.host}`;
  return `${base}/ws/phase/${phaseRunId}?token=${encodeURIComponent(token ?? '')}`;
}

export function inboxSocketUrl(): string {
  const token = getToken();
  const base = (import.meta.env.VITE_API_BASE ?? '').replace(/^http/, 'ws') || `${window.location.protocol === 'https:' ? 'wss:' : 'ws:'}//${window.location.host}`;
  return `${base}/ws/inbox?token=${encodeURIComponent(token ?? '')}`;
}

export interface RunDetail {
  id: string;
  moduleId: string;
  workflowId: string;
  status: string;
  startedAt: string;
  completedAt: string | null;
  phases: { id: string; phaseId: string; order: number; status: string }[];
}

export const getRun = (runId: string) => api<RunDetail>(`/api/runs/${runId}`);

export interface TenantUser {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
}

export const listTenantUsers = (tenantId: string) =>
  api<TenantUser[]>(`/api/tenants/${tenantId}/users`);

export const reassignPhase = (phaseRunId: string, userId: string) =>
  api<{ assignmentId: string; userId: string }>(
    `/api/phases/${phaseRunId}/reassign`,
    { method: 'POST', body: JSON.stringify({ userId }) },
  );

export const assignWaiting = (runId: string, phaseRunId: string, userId: string) =>
  api<{ assignmentId: string; userId: string }>(
    `/api/runs/${runId}/assign`,
    { method: 'POST', body: JSON.stringify({ phaseRunId, userId }) },
  );
