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
      const body = (await res.json()) as { error?: string };
      if (body.error) message = body.error;
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
