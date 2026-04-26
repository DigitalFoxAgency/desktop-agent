using AgentDesktop.Application.Chat;
using AgentDesktop.Application.Runtime;
using AgentDesktop.Contracts.Tests.Fakes;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Chat;

namespace AgentDesktop.Contracts.Tests;

/// <summary>
/// Contract tests for <see cref="IRuntimeManager"/> — verified against
/// <see cref="FakeRuntimeManager"/> at MVP and against the real
/// <c>ProcessRuntimeManager</c> in Phase 7 (gated by
/// <see cref="RequiresLiveRuntimeAttribute"/>).
/// </summary>
public sealed class RuntimeManagerContractTests
{
    [Fact]
    public async Task Initial_status_is_NotInstalled()
    {
        await using var rt = new FakeRuntimeManager();
        rt.Status.Should().Be(RuntimeStatus.NotInstalled);
    }

    [Fact]
    public async Task StartAsync_drives_status_to_Ready()
    {
        await using var rt = new FakeRuntimeManager();

        await rt.StartAsync(CancellationToken.None);

        rt.Status.Should().Be(RuntimeStatus.Ready);
    }

    [Fact]
    public async Task InvokeSkillAsync_throws_when_not_Ready_and_names_status()
    {
        await using var rt = new FakeRuntimeManager();
        // Don't start — status stays NotInstalled.

        var moduleId = new ModuleId("df-client-launchpad");
        var skillId = new SkillId("init");

        var act = async () => await rt.InvokeSkillAsync(
            moduleId,
            skillId,
            new Dictionary<string, object?>(),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("NotInstalled");
    }

    [Fact]
    public async Task InvokeSkillAsync_uses_programmed_responder_when_Ready()
    {
        await using var rt = new FakeRuntimeManager();
        var moduleId = new ModuleId("df-client-launchpad");
        var skillId = new SkillId("pre-research");
        rt.ProgramSkill(moduleId, skillId, _ => Task.FromResult(
            new SkillInvocationResult(true, new Dictionary<string, object?> { ["report"] = "ok" })));

        await rt.StartAsync(CancellationToken.None);

        var result = await rt.InvokeSkillAsync(moduleId, skillId, new Dictionary<string, object?>(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Outputs.Should().ContainKey("report").WhoseValue.Should().Be("ok");
    }

    [Fact]
    public async Task StreamChatAsync_yields_chunks_and_finalises_exactly_once()
    {
        await using var rt = new FakeRuntimeManager();
        await rt.StartAsync(CancellationToken.None);

        var chunks = new List<MessageChunk>();
        await foreach (var chunk in rt.StreamChatAsync(
            ConversationId.New(),
            Array.Empty<Message>(),
            "hello",
            CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        chunks.Should().NotBeEmpty();
        chunks.Count(c => c.IsFinal).Should().Be(1, "every chat stream must contain exactly one IsFinal=true chunk");
        chunks.Last().IsFinal.Should().BeTrue();
    }

    [Fact]
    public async Task StreamChatAsync_throws_when_not_Ready()
    {
        await using var rt = new FakeRuntimeManager();

        var act = () => Drain(rt.StreamChatAsync(
            ConversationId.New(),
            Array.Empty<Message>(),
            "hi",
            CancellationToken.None));

        await act.Should().ThrowAsync<InvalidOperationException>();

        static async Task Drain(IAsyncEnumerable<MessageChunk> source)
        {
            await foreach (var _ in source)
            {
                // Drain
            }
        }
    }

    [Fact]
    public async Task DisposeAsync_is_idempotent()
    {
        var rt = new FakeRuntimeManager();
        await rt.DisposeAsync();
        await rt.DisposeAsync();
        // No throw, no observable side effect — that's the contract.
    }

    [Fact]
    public async Task Status_observable_pushes_each_transition()
    {
        await using var rt = new FakeRuntimeManager();
        var observed = new List<RuntimeStatus>();
        using var sub = rt.StatusChanged.Subscribe(new RecordingObserver(observed));

        await rt.StartAsync(CancellationToken.None);

        observed.Should().ContainInOrder(RuntimeStatus.Starting, RuntimeStatus.Ready);
    }

    private sealed class RecordingObserver : IObserver<RuntimeStatus>
    {
        private readonly List<RuntimeStatus> _sink;
        public RecordingObserver(List<RuntimeStatus> sink) => _sink = sink;
        public void OnNext(RuntimeStatus value) => _sink.Add(value);
        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }
}
