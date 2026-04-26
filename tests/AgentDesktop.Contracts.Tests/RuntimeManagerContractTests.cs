using AgentDesktop.Application.Chat;
using AgentDesktop.Application.Runtime;
using AgentDesktop.Contracts.Tests.Fakes;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Chat;
using AgentDesktop.Domain.Policies;

namespace AgentDesktop.Contracts.Tests;

/// <summary>
/// Contract tests for <see cref="IRuntimeManager"/> — verified against
/// <see cref="FakeRuntimeManager"/> at MVP and against the real
/// <c>ProcessRuntimeManager</c> in Phase 7 (gated by
/// <see cref="RequiresLiveRuntimeAttribute"/>).
/// </summary>
public sealed class RuntimeManagerContractTests
{
    private static readonly ModuleId Launchpad = new("df-client-launchpad");

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
    public async Task DelegateAsync_throws_when_not_Ready_and_names_status()
    {
        await using var rt = new FakeRuntimeManager();
        var callbacks = new RecordingCallbacks();

        var act = async () => await rt.DelegateAsync(
            Launchpad,
            "onboard-client",
            new Dictionary<string, object?>(),
            ConversationId.New(),
            callbacks,
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("NotInstalled");
    }

    [Fact]
    public async Task DelegateAsync_routes_to_programmed_responder()
    {
        await using var rt = new FakeRuntimeManager();
        rt.ProgramOperation(Launchpad, "onboard-client", async ctx =>
        {
            await ctx.Callbacks.EmitProgressAsync("step 1", ctx.Cancellation);
            await ctx.Callbacks.EmitProgressAsync("step 2", ctx.Cancellation);
            return new DelegationResult(true, new Dictionary<string, object?> { ["siteUrl"] = "https://example.test" });
        });
        await rt.StartAsync(CancellationToken.None);

        var callbacks = new RecordingCallbacks();
        var result = await rt.DelegateAsync(
            Launchpad,
            "onboard-client",
            new Dictionary<string, object?> { ["niche"] = "yoga" },
            ConversationId.New(),
            callbacks,
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Outputs.Should().ContainKey("siteUrl").WhoseValue.Should().Be("https://example.test");
        callbacks.Progress.Should().BeEquivalentTo(new[] { "step 1", "step 2" }, opt => opt.WithStrictOrdering());
        rt.DelegationLog.Should().ContainSingle().Which.OperationId.Should().Be("onboard-client");
    }

    [Fact]
    public async Task DelegateAsync_unknown_operation_returns_failed_result()
    {
        await using var rt = new FakeRuntimeManager();
        await rt.StartAsync(CancellationToken.None);

        var result = await rt.DelegateAsync(
            Launchpad,
            "no-such-operation",
            new Dictionary<string, object?>(),
            ConversationId.New(),
            new RecordingCallbacks(),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("no-such-operation");
    }

    [Fact]
    public async Task DelegateAsync_propagates_confirmation_request_through_callbacks()
    {
        await using var rt = new FakeRuntimeManager();
        rt.ProgramOperation(Launchpad, "onboard-client", async ctx =>
        {
            var action = new DangerousAction(
                PolicyDecisionId.New(),
                DangerousActionKind.DeleteFile,
                "/tmp/foo",
                new PolicyOrigin.FromDelegation(ctx.ModuleId, ctx.OperationId),
                DateTimeOffset.UtcNow);

            var confirmed = await ctx.Callbacks.RequestConfirmationAsync(action, ctx.Cancellation);
            return new DelegationResult(
                Succeeded: confirmed,
                Outputs: new Dictionary<string, object?> { ["confirmed"] = confirmed });
        });
        await rt.StartAsync(CancellationToken.None);

        var callbacks = new RecordingCallbacks { ConfirmAnswer = true };
        var result = await rt.DelegateAsync(
            Launchpad,
            "onboard-client",
            new Dictionary<string, object?>(),
            ConversationId.New(),
            callbacks,
            CancellationToken.None);

        callbacks.ConfirmationRequests.Should().ContainSingle()
            .Which.Kind.Should().Be(DangerousActionKind.DeleteFile);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task DelegateAsync_propagates_human_handoff_request()
    {
        await using var rt = new FakeRuntimeManager();
        rt.ProgramOperation(Launchpad, "onboard-client", async ctx =>
        {
            await ctx.Callbacks.RequestHumanHandoffAsync(
                "brief",
                "Hold the Discovery call with the client.",
                ctx.Cancellation);
            return new DelegationResult(true, new Dictionary<string, object?>());
        });
        await rt.StartAsync(CancellationToken.None);

        var callbacks = new RecordingCallbacks();
        await rt.DelegateAsync(
            Launchpad,
            "onboard-client",
            new Dictionary<string, object?>(),
            ConversationId.New(),
            callbacks,
            CancellationToken.None);

        callbacks.Handoffs.Should().ContainSingle()
            .Which.StepName.Should().Be("brief");
    }

    [Fact]
    public async Task InvokeSkillAsync_uses_programmed_responder()
    {
        await using var rt = new FakeRuntimeManager();
        var skillId = new SkillId("pre-research");
        rt.ProgramSkill(Launchpad, skillId, _ => Task.FromResult(
            new SkillInvocationResult(true, new Dictionary<string, object?> { ["report"] = "ok" })));
        await rt.StartAsync(CancellationToken.None);

        var result = await rt.InvokeSkillAsync(Launchpad, skillId, new Dictionary<string, object?>(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Outputs.Should().ContainKey("report").WhoseValue.Should().Be("ok");
    }

    [Fact]
    public async Task InvokeSkillAsync_throws_when_not_Ready()
    {
        await using var rt = new FakeRuntimeManager();
        var act = async () => await rt.InvokeSkillAsync(
            Launchpad,
            new SkillId("init"),
            new Dictionary<string, object?>(),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("NotInstalled");
    }

    [Fact]
    public async Task StreamChatAsync_finalises_exactly_once()
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
        chunks.Count(c => c.IsFinal).Should().Be(1);
        chunks.Last().IsFinal.Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_is_idempotent()
    {
        var rt = new FakeRuntimeManager();
        await rt.DisposeAsync();
        await rt.DisposeAsync();
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

    private sealed class RecordingCallbacks : IDelegationCallbacks
    {
        public List<string> Progress { get; } = new();
        public List<DangerousAction> ConfirmationRequests { get; } = new();
        public List<(string StepName, string Instructions)> Handoffs { get; } = new();
        public bool ConfirmAnswer { get; set; }

        public Task EmitProgressAsync(string text, CancellationToken ct)
        {
            Progress.Add(text);
            return Task.CompletedTask;
        }

        public Task<bool> RequestConfirmationAsync(DangerousAction action, CancellationToken ct)
        {
            ConfirmationRequests.Add(action);
            return Task.FromResult(ConfirmAnswer);
        }

        public Task RequestHumanHandoffAsync(string stepName, string instructions, CancellationToken ct)
        {
            Handoffs.Add((stepName, instructions));
            return Task.CompletedTask;
        }
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
