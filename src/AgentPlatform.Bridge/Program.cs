// AgentPlatform.Bridge — runs inside each per-run container, wraps the
// `claude` CLI, and proxies chat I/O + file-tree events + dangerous-action
// confirmation prompts back to the API over WebSocket.
//
// This is a placeholder entry point. Phase 2 (T060–T063) wires the real
// bridge: ClaudeWrapper, transport, file watcher.

Console.WriteLine("AgentPlatform.Bridge: placeholder process — Phase 2 will replace this entry point.");
return 0;
