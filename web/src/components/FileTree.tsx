import { useEffect, useState } from 'react';
import { listFiles, type FileEntry } from '../api/client';

interface Props {
  phaseRunId: string;
  refreshSignal: number;
  onSelect?: (path: string) => void;
}

interface NodeProps {
  phaseRunId: string;
  path: string;
  name: string;
  isDirectory: boolean;
  refreshSignal: number;
  depth: number;
  onSelect?: (path: string) => void;
}

function FileNode({ phaseRunId, path, name, isDirectory, refreshSignal, depth, onSelect }: NodeProps) {
  const [open, setOpen] = useState(depth === 0);
  const [children, setChildren] = useState<FileEntry[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!isDirectory || !open) return;
    let cancelled = false;
    listFiles(phaseRunId, path)
      .then((entries) => {
        if (!cancelled) setChildren(entries);
      })
      .catch((e: Error) => {
        if (!cancelled) setError(e.message);
      });
    return () => {
      cancelled = true;
    };
  }, [phaseRunId, path, open, isDirectory, refreshSignal]);

  if (!isDirectory) {
    return (
      <div
        className="cursor-pointer hover:bg-gray-50 px-2 py-1 text-sm font-mono"
        style={{ paddingLeft: 8 + depth * 12 }}
        onClick={() => onSelect?.(path)}
      >
        {name}
      </div>
    );
  }

  return (
    <div>
      <div
        className="cursor-pointer hover:bg-gray-50 px-2 py-1 text-sm font-mono select-none"
        style={{ paddingLeft: 8 + depth * 12 }}
        onClick={() => setOpen((v) => !v)}
      >
        {open ? '▾' : '▸'} {name || '/'}
      </div>
      {open && error && (
        <div className="text-xs text-red-600 px-2" style={{ paddingLeft: 16 + depth * 12 }}>
          {error}
        </div>
      )}
      {open && children?.map((c) => (
        <FileNode
          key={c.path}
          phaseRunId={phaseRunId}
          path={c.path}
          name={c.name}
          isDirectory={c.isDirectory}
          refreshSignal={refreshSignal}
          depth={depth + 1}
          onSelect={onSelect}
        />
      ))}
    </div>
  );
}

export default function FileTree({ phaseRunId, refreshSignal, onSelect }: Props) {
  return (
    <div className="border rounded h-full overflow-y-auto">
      <FileNode
        phaseRunId={phaseRunId}
        path=""
        name="workspace"
        isDirectory
        refreshSignal={refreshSignal}
        depth={0}
        onSelect={onSelect}
      />
    </div>
  );
}
