import { Routes, Route } from 'react-router-dom';

// Pages are stubs until US1/US2 lands. They are added per the tasks.md
// breakdown: SignIn/SignUp/Dashboard/StartRun (US1), Inbox/Phase (US2),
// Catalogue (US5).

function Placeholder({ name }: { name: string }) {
  return (
    <div className="p-6">
      <h1 className="text-2xl font-semibold">{name}</h1>
      <p className="text-sm text-gray-600">Phase 1 placeholder. See specs/001-agent-platform-mvp/tasks.md.</p>
    </div>
  );
}

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<Placeholder name="Agent Platform" />} />
      <Route path="*" element={<Placeholder name="Not Found" />} />
    </Routes>
  );
}
