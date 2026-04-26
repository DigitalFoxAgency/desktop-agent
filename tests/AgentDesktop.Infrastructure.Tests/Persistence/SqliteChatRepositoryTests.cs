using AgentDesktop.Contracts.Tests.Fakes;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Chat;
using AgentDesktop.Infrastructure.Persistence.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentDesktop.Infrastructure.Tests.Persistence;

public sealed class SqliteChatRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteChatRepository _repo;
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 4, 26, 10, 0, 0, TimeSpan.Zero));

    public SqliteChatRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "agentdesktop-chat-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var options = Options.Create(new SqliteDatabaseOptions
        {
            DatabasePath = Path.Combine(_tempDir, "chat.db"),
        });

        _factory = new SqliteConnectionFactory(options, _clock, NullLogger<SqliteConnectionFactory>.Instance);
        _repo = new SqliteChatRepository(_factory);
    }

    public void Dispose()
    {
        _factory.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }

    [Fact]
    public async Task Add_then_Get_round_trips_an_empty_conversation()
    {
        var convo = NewDraft();

        await _repo.AddAsync(convo, CancellationToken.None);
        var loaded = await _repo.GetAsync(convo.Id, CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(convo.Id);
        loaded.Title.Should().Be(convo.Title);
        loaded.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task AppendMessage_then_Get_returns_messages_in_order()
    {
        var convo = NewDraft();
        await _repo.AddAsync(convo, CancellationToken.None);

        await _repo.AppendMessageAsync(NewUserMessage(convo.Id, 0, "hello", _clock.UtcNow.AddSeconds(1)), CancellationToken.None);
        await _repo.AppendMessageAsync(NewAgentMessage(convo.Id, 1, "world", _clock.UtcNow.AddSeconds(2)), CancellationToken.None);

        var loaded = await _repo.GetAsync(convo.Id, CancellationToken.None);
        loaded!.Messages.Should().HaveCount(2);
        loaded.Messages[0].Index.Should().Be(0);
        loaded.Messages[0].Author.Should().Be(MessageAuthor.User);
        loaded.Messages[1].Index.Should().Be(1);
        loaded.Messages[1].Author.Should().Be(MessageAuthor.Agent);
    }

    [Fact]
    public async Task Append_persists_originating_delegation()
    {
        var convo = NewDraft();
        await _repo.AddAsync(convo, CancellationToken.None);

        var delegation = new DelegationRef(new ModuleId("df-client-launchpad"), "onboard-client");
        var message = new Message(
            id: MessageId.New(),
            conversationId: convo.Id,
            index: 0,
            author: MessageAuthor.Agent,
            body: "Initialising client workspace…",
            createdAt: _clock.UtcNow.AddSeconds(1),
            originatingDelegation: delegation);
        await _repo.AppendMessageAsync(message, CancellationToken.None);

        var loaded = await _repo.GetAsync(convo.Id, CancellationToken.None);
        loaded!.Messages[0].OriginatingDelegation.Should().Be(delegation);
        loaded.Messages[0].OriginatingSkill.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_orders_by_last_activity_desc()
    {
        var oldConvo = new Conversation(
            ConversationId.New(),
            "old",
            createdAt: _clock.UtcNow.AddMinutes(-15),
            lastActivityAt: _clock.UtcNow.AddMinutes(-10));
        var newConvo = new Conversation(
            ConversationId.New(),
            "new",
            createdAt: _clock.UtcNow,
            lastActivityAt: _clock.UtcNow);

        await _repo.AddAsync(oldConvo, CancellationToken.None);
        await _repo.AddAsync(newConvo, CancellationToken.None);

        var listed = new List<Conversation>();
        await foreach (var c in _repo.ListAsync(CancellationToken.None))
        {
            listed.Add(c);
        }

        listed.Should().HaveCount(2);
        listed[0].Id.Should().Be(newConvo.Id);
        listed[1].Id.Should().Be(oldConvo.Id);
    }

    [Fact]
    public async Task UpdateMetadata_changes_title_and_last_activity()
    {
        var convo = NewDraft();
        await _repo.AddAsync(convo, CancellationToken.None);

        var newActivity = _clock.UtcNow.AddSeconds(5);
        await _repo.UpdateMetadataAsync(convo.Id, "New Title", newActivity, CancellationToken.None);

        var loaded = await _repo.GetAsync(convo.Id, CancellationToken.None);
        loaded!.Title.Should().Be("New Title");
        loaded.LastActivityAt.Should().Be(newActivity);
    }

    private Conversation NewDraft()
    {
        var now = _clock.UtcNow;
        return new Conversation(ConversationId.New(), "draft", now, now);
    }

    private static Message NewUserMessage(ConversationId convo, int idx, string body, DateTimeOffset at) =>
        new(MessageId.New(), convo, idx, MessageAuthor.User, body, at);

    private static Message NewAgentMessage(ConversationId convo, int idx, string body, DateTimeOffset at) =>
        new(MessageId.New(), convo, idx, MessageAuthor.Agent, body, at);
}
