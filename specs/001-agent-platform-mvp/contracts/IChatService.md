# Contract: `IChatService`

**Project**: `AgentDesktop.Application` (`Chat/IChatService.cs`)
**Consumers**: UI view-models, scenario runner.
**Implementations**: `ChatService` (Application), `FakeChatService` (tests).

## Purpose

The single entry point for everything chat-related. The UI never
talks to the model provider, the runtime, or the database directly —
it talks to `IChatService`, which orchestrates persistence, runtime
calls, and policy evaluation.

## Interface

```csharp
public interface IChatService
{
    Task<Conversation> StartConversationAsync(CancellationToken ct);
    Task<Conversation> GetConversationAsync(ConversationId id, CancellationToken ct);
    IAsyncEnumerable<Conversation> ListConversationsAsync(CancellationToken ct);

    IAsyncEnumerable<MessageChunk> SendMessageAsync(
        ConversationId conversationId,
        string body,
        CancellationToken ct);
}

public readonly record struct MessageChunk(
    MessageId MessageId,
    string DeltaText,
    bool IsFinal);
```

## Behavioural contract

1. `StartConversationAsync` MUST return a persisted, empty
   `Conversation` whose `Id` is unique and stable.
2. `SendMessageAsync` MUST:
   - Append the user message **before** any model call.
   - Yield agent `MessageChunk`s incrementally as the runtime streams
     tokens; the first chunk MUST arrive within the SC-002 budget on
     a healthy connection.
   - Mark the final chunk with `IsFinal = true` exactly once.
   - Persist the agent message in full when the stream completes.
   - Surface a `System` message and complete the stream with a final
     chunk if the runtime reports `Degraded` or `Stopped` mid-stream.
3. Cancellation tokens MUST stop the stream and persist whatever has
   been received so far (no half-saved messages).
4. The service MUST NOT execute any side effect classified as
   `Dangerous`; those flow through `IPolicyEngine` first.

## Required tests (contract)

- `StartConversationAsync` returns a unique, persisted conversation.
- `SendMessageAsync` persists the user message before invoking the
  runtime (verified by ordering on a fake repository).
- The final chunk has `IsFinal = true` exactly once.
- Cancelling mid-stream leaves the conversation in a consistent
  state (no partial agent message, user message preserved).
- A `Degraded` runtime status produces a `System` message and a final
  chunk.
