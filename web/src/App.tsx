import { Navigate, Route, Routes } from 'react-router-dom';
import { getSession } from './api/client';
import AppShell from './components/AppShell';
import SignIn from './pages/SignIn';
import SignUp from './pages/SignUp';
import Dashboard from './pages/Dashboard';
import StartRun from './pages/StartRun';
import Catalogue from './pages/Catalogue';
import { NotFound, OfflineBanner } from './pages/Error';
import Inbox from './pages/Inbox';
import Phase from './pages/Phase';
import Run from './pages/Run';

function Protected({ children }: { children: React.ReactNode }) {
  const session = getSession();
  if (!session) return <Navigate to="/signin" replace />;
  return <>{children}</>;
}

// Pages that share the standard top-nav. Phase is intentionally excluded —
// it owns its own full-height layout (chat + file tree).
function Shelled({ children }: { children: React.ReactNode }) {
  return (
    <Protected>
      <AppShell>{children}</AppShell>
    </Protected>
  );
}

export default function App() {
  return (
    <>
      <OfflineBanner />
      <Routes>
        <Route path="/signin" element={<SignIn />} />
        <Route path="/signup" element={<SignUp />} />
        <Route path="/" element={<Shelled><Dashboard /></Shelled>} />
        <Route path="/runs/new" element={<Shelled><StartRun /></Shelled>} />
        <Route path="/catalogue" element={<Shelled><Catalogue /></Shelled>} />
        <Route path="/inbox" element={<Shelled><Inbox /></Shelled>} />
        <Route path="/runs/:id" element={<Shelled><Run /></Shelled>} />
        <Route path="/phase/:phaseRunId" element={<Protected><Phase /></Protected>} />
        <Route path="*" element={<NotFound />} />
      </Routes>
    </>
  );
}
