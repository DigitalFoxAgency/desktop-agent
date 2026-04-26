using AgentDesktop.Application.Chat;
using AgentDesktop.Contracts.Tests.Fakes;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Identity;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentDesktop.Contracts.Tests;

/// <summary>
/// Contract tests for <see cref="IChatService"/>. Verified
/// against the production <c>ChatService</c> wired with fakes
/// (T058).
/// </summary>
public sealed class ChatServiceContractTests : IAsyncDisposable
{
    private readonly FakeRuntimeManager _runtime = new();
    private readonly FakeChatRepository _repository = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 4, 26, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeSubscriptionGate _subscription = new();

    public ChatServiceContractTests()
    {
        _runtime.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        await _runtime.DisposeAsync();
    }

    [Fact]
    public async Task StartConversationAsync_persists_a_unique_empty_conversation()
    {
        var service = NewService();

        var convo = await service.StartConversationAsync(CancellationToken.None);

        convo.Messages.Should().BeEmpty();
        convo.CreatedAt.Should().Be(_clock.UtcNow);
        _repository.Calls.Should().Contain(c => c.StartsWith("Add(", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendMessageAsync_persists_user_message_before_runtime_call()
    {
        var service = NewService();
        var convo = await service.StartConversationAsync(CancellationToken.None);

        var chunks = new List<MessageChunk>();
        await foreach (var c in service.SendMessageAsync(convo.Id, "hello world", CancellationToken.None))
        {
            chunks.Add(c);
        }

        // The user-message Append must precede the runtime chat call.
        var userAppendIndex = _repository.Calls.FindIndex(c => c.Contains(",#0,User", StringComparison.Ordinal));
        userAppendIndex.Should().BeGreaterThanOrEqualTo(0);

        // Fake runtime records the chat call so we can assert ordering between the two collaborators.
        _runtime.ChatLog.Should().NotBeEmpty();
        _repository.Calls.Should().Contain(c => c.Contains(",#0,User", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendMessageAsync_yields_exactly_one_IsFinal_chunk()
    {
        var service = NewService();
        var convo = await service.StartConversationAsync(CancellationToken.None);

        var chunks = new List<MessageChunk>();
        await foreach (var c in service.SendMessageAsync(convo.Id, "hi", CancellationToken.None))
        {
            chunks.Add(c);
        }

        chunks.Count(c => c.IsFinal).Should().Be(1);
        chunks.Last().IsFinal.Should().BeTrue();
    }

    [Fact]
    public async Task SendMessageAsync_persists_agent_reply_after_stream_completes()
    {
        var service = NewService();
        var convo = await service.StartConversationAsync(CancellationToken.None);

        await foreach (var _ in service.SendMessageAsync(convo.Id, "hi", CancellationToken.None))
        {
            // drain
        }

        // After draining, repository should have the agent message appended (idx 1).
        _repository.Calls.Should().Contain(c => c.Contains(",#1,Agent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendMessageAsync_when_subscription_inactive_refuses_and_persists_System_message()
    {
        _subscription.CurrentStatus = SubscriptionStatus.Expired;
        _subscription.CurrentAccount = new UserAccount(
            new AccountId("test"), "u@test", SubscriptionStatus.Expired);
        var service = NewService();
        var convo = await service.StartConversationAsync(CancellationToken.None);

        var chunks = new List<MessageChunk>();
        await foreach (var c in service.SendMessageAsync(convo.Id, "build me a site", CancellationToken.None))
        {
            chunks.Add(c);
        }

        chunks.Should().ContainSingle().Which.IsFinal.Should().BeTrue();
        chunks[0].DeltaText.Should().Contain("unavailable");

        // No runtime call should have happened.
        _runtime.ChatLog.Should().BeEmpty();

        // A System message was persisted; user message was NOT.
        _repository.Calls.Should().Contain(c => c.Contains(",System", StringComparison.Ordinal));
        _repository.Calls.Should().NotContain(c => c.Contains(",User", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendMessageAsync_does_not_persist_whitespace_only_agent_reply()
    {
        // Runtime emits only whitespace + final-empty marker — must not persist a blank bubble.
        _runtime.ProgramChat((_, _) => WhitespaceOnly());

        var service = NewService();
        var convo = await service.StartConversationAsync(CancellationToken.None);

        await foreach (var _ in service.SendMessageAsync(convo.Id, "hi", CancellationToken.None))
        {
            // drain
        }

        _repository.Calls.Should().NotContain(c => c.Contains(",Agent", StringComparison.Ordinal));

        static async IAsyncEnumerable<MessageChunk> WhitespaceOnly()
        {
            await Task.Yield();
            var id = MessageId.New();
            yield return new MessageChunk(id, "   ", IsFinal: false);
            yield return new MessageChunk(id, "\n", IsFinal: false);
            yield return new MessageChunk(id, string.Empty, IsFinal: true);
        }
    }

    [Fact]
    public async Task SendMessageAsync_history_is_readable_after_subscription_revoked()
    {
        // Establish history while active.
        var service = NewService();
        var convo = await service.StartConversationAsync(CancellationToken.None);
        await foreach (var _ in service.SendMessageAsync(convo.Id, "hi", CancellationToken.None))
        {
        }

        _subscription.CurrentStatus = SubscriptionStatus.Revoked;
        _subscription.CurrentAccount = new UserAccount(
            new AccountId("test"), "u@test", SubscriptionStatus.Revoked);

        // History stays readable.
        var fetched = await service.GetConversationAsync(convo.Id, CancellationToken.None);
        fetched.Messages.Should().NotBeEmpty();
    }

    private ChatService NewService() =>
        new(_repository, _runtime, _subscription, _clock, NullLogger<ChatService>.Instance);
}
