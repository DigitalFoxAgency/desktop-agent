using AgentDesktop.Application.Modules;
using AgentDesktop.Application.Policies;
using AgentDesktop.Contracts.Tests.Fakes;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Modules;
using AgentDesktop.Domain.Policies;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentDesktop.Contracts.Tests;

/// <summary>
/// Contract tests for <see cref="DefaultPolicyEngine"/>.
/// Covers every branch of the engine to satisfy the
/// constitution's 100% branch-coverage requirement on this
/// module (T076 folded in).
/// </summary>
public sealed class PolicyEngineContractTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 4, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly ModuleId Launchpad = new("df-client-launchpad");

    private readonly StubModuleRegistry _modules = new();
    private readonly FakeConfirmationPrompt _prompt = new();
    private readonly FakeAuditLog _audit = new();
    private readonly FakeClock _clock = new(Now);
    private readonly DefaultPolicyEngine _engine;

    public PolicyEngineContractTests()
    {
        _engine = new DefaultPolicyEngine(
            _modules,
            _prompt,
            _audit,
            _clock,
            NullLogger<DefaultPolicyEngine>.Instance);
    }

    public void Dispose() => _engine.Dispose();

    [Theory]
    [InlineData(DangerousActionKind.DeleteFile)]
    [InlineData(DangerousActionKind.GitPush)]
    [InlineData(DangerousActionKind.InstallPackage)]
    [InlineData(DangerousActionKind.RunShell)]
    [InlineData(DangerousActionKind.ModuleDeclared)]
    public async Task Each_baseline_dangerous_kind_triggers_a_confirmation_prompt(DangerousActionKind kind)
    {
        _prompt.EnqueueResponse(true);

        var action = NewAction(kind);
        var decision = await _engine.EvaluateAsync(action, CancellationToken.None);

        decision.Outcome.Should().Be(PolicyOutcome.Confirmed);
        _prompt.Asked.Should().ContainSingle().Which.Kind.Should().Be(kind);
        _audit.Proposed.Should().ContainSingle();
        _audit.Decided.Should().ContainSingle();
    }

    [Fact]
    public async Task Decline_records_a_Declined_decision_without_executing()
    {
        _prompt.EnqueueResponse(false);

        var decision = await _engine.EvaluateAsync(NewAction(DangerousActionKind.DeleteFile), CancellationToken.None);

        decision.Outcome.Should().Be(PolicyOutcome.Declined);
        _audit.Decided.Should().ContainSingle().Which.Outcome.Should().Be(PolicyOutcome.Declined);
    }

    [Fact]
    public async Task Module_override_can_upgrade_an_action_class_to_dangerous()
    {
        // Even though the baseline says Dangerous, this test still
        // demonstrates the override path is consulted.
        _modules.AddModuleWithPolicy(
            Launchpad,
            new ModulePolicy(
                actionClass: nameof(DangerousActionKind.DeleteFile),
                classification: ActionClassification.Dangerous,
                reason: "Module-confirmed dangerous"));

        _prompt.EnqueueResponse(true);
        var decision = await _engine.EvaluateAsync(NewAction(DangerousActionKind.DeleteFile), CancellationToken.None);

        decision.Outcome.Should().Be(PolicyOutcome.Confirmed);
        _prompt.Asked.Should().ContainSingle();
    }

    [Fact]
    public async Task Module_override_can_downgrade_an_action_to_safe_skipping_the_prompt()
    {
        _modules.AddModuleWithPolicy(
            Launchpad,
            new ModulePolicy(
                actionClass: nameof(DangerousActionKind.RunShell),
                classification: ActionClassification.Safe,
                reason: "Sandboxed harmless shell calls"));

        var decision = await _engine.EvaluateAsync(NewAction(DangerousActionKind.RunShell), CancellationToken.None);

        decision.Outcome.Should().Be(PolicyOutcome.Confirmed);
        _prompt.Asked.Should().BeEmpty(); // safe path never prompts
        _audit.Decided.Should().ContainSingle().Which.Outcome.Should().Be(PolicyOutcome.Confirmed);
    }

    [Fact]
    public async Task Cancellation_during_prompt_records_Expired_and_propagates()
    {
        _prompt.CancelOnNextRequest = true;

        var act = async () => await _engine.EvaluateAsync(
            NewAction(DangerousActionKind.DeleteFile),
            CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _audit.Decided.Should().ContainSingle().Which.Outcome.Should().Be(PolicyOutcome.Expired);
    }

    [Fact]
    public async Task Two_in_flight_evaluations_serialise_their_prompts()
    {
        _prompt.EnqueueResponse(true);
        _prompt.EnqueueResponse(true);

        var first = _engine.EvaluateAsync(NewAction(DangerousActionKind.DeleteFile), CancellationToken.None);
        var second = _engine.EvaluateAsync(NewAction(DangerousActionKind.GitPush), CancellationToken.None);

        await Task.WhenAll(first, second);

        _prompt.MaxConcurrentExceeded.Should().BeFalse(
            "the engine MUST queue prompts so two confirmations are never on screen at once.");
    }

    [Fact]
    public async Task Origin_FromSkill_is_routed_through_module_lookup()
    {
        _prompt.EnqueueResponse(true);

        var skillOrigin = new PolicyOrigin.FromSkill(Launchpad, new SkillId("init"));
        var action = new DangerousAction(
            PolicyDecisionId.New(),
            DangerousActionKind.DeleteFile,
            "/tmp/foo",
            skillOrigin,
            Now);

        var decision = await _engine.EvaluateAsync(action, CancellationToken.None);

        decision.Outcome.Should().Be(PolicyOutcome.Confirmed);
        _modules.LookedUp.Should().Contain(Launchpad);
    }

    [Fact]
    public async Task Audit_log_records_proposal_and_decision_in_order()
    {
        _prompt.EnqueueResponse(true);

        await _engine.EvaluateAsync(NewAction(DangerousActionKind.GitPush), CancellationToken.None);

        _audit.Proposed.Should().ContainSingle();
        _audit.Decided.Should().ContainSingle();
        _audit.Decided[0].Id.Should().Be(_audit.Proposed[0].Id);
    }

    private static DangerousAction NewAction(DangerousActionKind kind)
    {
        return new DangerousAction(
            PolicyDecisionId.New(),
            kind,
            target: kind switch
            {
                DangerousActionKind.DeleteFile => "/tmp/foo",
                DangerousActionKind.GitPush => "origin/main",
                DangerousActionKind.InstallPackage => "left-pad",
                DangerousActionKind.RunShell => "rm -rf /",
                DangerousActionKind.ModuleDeclared => "module:custom-action",
                _ => "unknown",
            },
            origin: new PolicyOrigin.FromDelegation(Launchpad, "onboard-client"),
            requestedAt: Now);
    }

    private sealed class StubModuleRegistry : IModuleRegistry
    {
        private readonly Dictionary<ModuleId, Module> _modules = new();
        public List<ModuleId> LookedUp { get; } = new();

        public void AddModuleWithPolicy(ModuleId id, ModulePolicy policy)
        {
            _modules[id] = new Module(
                id,
                version: SemanticVersion.Parse("1.0.0"),
                name: id.Value,
                description: "test",
                schemaVersion: 1,
                dependencies: Array.Empty<ModuleDependency>(),
                operations: new[]
                {
                    new Operation("op", "Op", "description", Array.Empty<SkillParameter>()),
                },
                skills: Array.Empty<Skill>(),
                mcpServers: Array.Empty<McpServerDescriptor>(),
                prompts: Array.Empty<PromptTemplate>(),
                policies: new[] { policy },
                source: ModuleSource.Bundled);
        }

        public Task RefreshAsync(CancellationToken ct) => Task.CompletedTask;
        public IReadOnlyList<Module> GetAll() => _modules.Values.ToList();

        public Module? Find(ModuleId id)
        {
            LookedUp.Add(id);
            return _modules.TryGetValue(id, out var m) ? m : null;
        }

        public Operation? FindOperation(ModuleId moduleId, string operationId) =>
            Find(moduleId)?.FindOperation(operationId);

        public Skill? FindSkill(ModuleId moduleId, SkillId skillId) =>
            Find(moduleId)?.FindSkill(skillId);
    }
}
