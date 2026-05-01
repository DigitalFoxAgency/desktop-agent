import { useQuery } from '@tanstack/react-query';
import { Link, NavLink, useNavigate } from 'react-router-dom';
import { clearSession, getSession, listInbox } from '../api/client';

interface Props {
  children: React.ReactNode;
}

// Shared chrome for authenticated pages. Sticky top nav with the same links
// everywhere so users always know how to get to inbox / catalogue / dashboard
// without leaning on the back button.
export default function AppShell({ children }: Props) {
  const navigate = useNavigate();
  const session = getSession();

  const { data: inbox } = useQuery({
    queryKey: ['inbox'],
    queryFn: listInbox,
    refetchInterval: 30000,
    enabled: !!session,
  });
  const inboxCount = inbox?.length ?? 0;

  function signOut() {
    clearSession();
    navigate('/signin');
  }

  return (
    <div className="min-h-screen flex flex-col bg-gray-50">
      <header className="sticky top-0 z-30 bg-white border-b">
        <div className="mx-auto max-w-6xl px-4 h-12 flex items-center gap-6">
          <Link to="/" className="font-semibold text-gray-900">
            Agent Platform
          </Link>
          <nav className="flex items-center gap-4 text-sm">
            <NavBarLink to="/" end>Dashboard</NavBarLink>
            <NavBarLink to="/inbox">
              Inbox
              {inboxCount > 0 && (
                <span className="ml-1.5 inline-flex items-center justify-center text-[10px] font-medium rounded-full bg-blue-600 text-white px-1.5 py-px min-w-[1.125rem] h-[1.125rem]">
                  {inboxCount > 99 ? '99+' : inboxCount}
                </span>
              )}
            </NavBarLink>
            <NavBarLink to="/catalogue">Catalogue</NavBarLink>
          </nav>
          <div className="ml-auto flex items-center gap-3">
            <Link
              to="/runs/new"
              className="rounded bg-blue-600 px-3 py-1.5 text-white text-sm hover:bg-blue-700"
            >
              Start a workflow
            </Link>
            {session && (
              <details className="relative">
                <summary className="list-none cursor-pointer text-sm text-gray-700 hover:text-gray-900 select-none">
                  {session.userId.slice(0, 6)}…
                </summary>
                <div className="absolute right-0 mt-2 min-w-[12rem] rounded-md border bg-white shadow-md text-sm py-1">
                  <div className="px-3 py-1.5 text-xs text-gray-500 border-b">
                    Roles: {session.roles.join(', ') || '—'}
                  </div>
                  <button
                    onClick={signOut}
                    className="block w-full text-left px-3 py-1.5 hover:bg-gray-50 text-gray-700"
                  >
                    Sign out
                  </button>
                </div>
              </details>
            )}
          </div>
        </div>
      </header>
      <main className="flex-1">{children}</main>
    </div>
  );
}

function NavBarLink({ to, end, children }: { to: string; end?: boolean; children: React.ReactNode }) {
  return (
    <NavLink
      to={to}
      end={end}
      className={({ isActive }) =>
        `inline-flex items-center px-1 py-0.5 -my-0.5 rounded ${
          isActive ? 'text-gray-900 font-medium' : 'text-gray-600 hover:text-gray-900'
        }`
      }
    >
      {children}
    </NavLink>
  );
}
