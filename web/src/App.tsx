import { Navigate, Route, Routes } from 'react-router-dom';
import { getSession } from './api/client';
import SignIn from './pages/SignIn';
import SignUp from './pages/SignUp';
import Dashboard from './pages/Dashboard';
import StartRun from './pages/StartRun';

function Protected({ children }: { children: React.ReactNode }) {
  const session = getSession();
  if (!session) return <Navigate to="/signin" replace />;
  return <>{children}</>;
}

export default function App() {
  return (
    <Routes>
      <Route path="/signin" element={<SignIn />} />
      <Route path="/signup" element={<SignUp />} />
      <Route
        path="/"
        element={
          <Protected>
            <Dashboard />
          </Protected>
        }
      />
      <Route
        path="/runs/new"
        element={
          <Protected>
            <StartRun />
          </Protected>
        }
      />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
