using AgentDesktop.Application.Modules;
using AgentDesktop.Application.Policies;
using AgentDesktop.Contracts.Tests.Fakes;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Modules;
using AgentDesktop.Domain.Policies;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentDesktop.Application.Tests.Modules;

/// <summary>
/// Coverage for the policy-engine integration in
/// <see cref="SkillInvocationService"/> (T083 / T084):
/// dangerous skills route through <see cref="IPolicyEngine"/>;
/// declined / skipped outcomes leave a System message in the chat
/// surface naming the <see cref="PolicyDecisionId"/>.
/// </summary>
public sealed class SkillInvocationServiceTests
{
    private static readonly ModuleId TestModuleId = new("test-module");
    private static readonly SkillId DangerousSkillId = new("dangerous-skill");
    private static readonly SkillId SafeSkillId = new("safe-skill");

    [Fact]
    public async Task Declined_dangerous_skill_persists_System_message_with_decision_id()
    {
        var fakes = await BuildAsync(skillIsDangerous: true);
        fakes.Policy
            .EvaluateAsync(Arg.Any<DangerousAction>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var action = call.ArgAt<DangerousAction>(0);
                return new PolicyDecision(action.Id, action, PolicyOutcome.Declined, fakes.Clock.UtcNow);
            });

        var result = await fakes.Service.InvokeAsync(
            TestModuleId,
            DangerousSkillId,
            new Dictionary<string, object?>(),
            fakes.ConversationId,
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();

        var convo = await fakes.Repo.GetAsync(fakes.ConversationId, CancellationToken.None);
        convo!.Messages.Should().Contain(m =>
            m.Author == MessageAuthor.System &&
            m.Body.Contains("declined", StringComparison.Ordinal) &&
            m.Body.Contains(DangerousSkillId.Value, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Declined_skill_does_not_invoke_runtime()
    {
        var fakes = await BuildAsync(skillIsDangerous: true);
        fakes.Policy
            .EvaluateAsync(Arg.Any<DangerousAction>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var action = call.ArgAt<DangerousAction>(0);
                return new PolicyDecision(action.Id, action, PolicyOutcome.Declined, fakes.Clock.UtcNow);
            });

        await fakes.Service.InvokeAsync(
            TestModuleId,
            DangerousSkillId,
            new Dictionary<string, object?>(),
            fakes.ConversationId,
            CancellationToken.None);

        fakes.Runtime.InvocationLog.Should().BeEmpty(
            "the runtime must never be called when the policy engine declines");
    }

    [Fact]
    public async Task Safe_skill_skips_policy_engine_and_runs()
    {
        var fakes = await BuildAsync(skillIsDangerous: false);

        var result = await fakes.Service.InvokeAsync(
            TestModuleId,
            SafeSkillId,
            new Dictionary<string, object?>(),
            fakes.ConversationId,
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        await fakes.Policy.DidNotReceive().EvaluateAsync(
            Arg.Any<DangerousAction>(), Arg.Any<CancellationToken>());
        fakes.Runtime.InvocationLog.Should().HaveCount(1);
    }

    private static async Task<Fakes> BuildAsync(bool skillIsDangerous)
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 4, 26, 10, 0, 0, TimeSpan.Zero));
        var repo = new FakeChatRepository();
        var runtime = new FakeRuntimeManager();
        await runtime.StartAsync(CancellationToken.None);

        var policy = Substitute.For<IPolicyEngine>();
        var registry = Substitute.For<IModuleRegistry>();

        var skill = new Skill(
            id: skillIsDangerous ? DangerousSkillId : SafeSkillId,
            moduleId: TestModuleId,
            name: "Test skill",
            description: string.Empty,
            inputs: Array.Empty<SkillParameter>(),
            outputs: Array.Empty<SkillParameter>(),
            classification: skillIsDangerous ? ActionClassification.Dangerous : ActionClassification.Safe,
            kind: SkillKind.Automated,
            sourcePath: null);
        registry.FindSkill(TestModuleId, Arg.Any<SkillId>()).Returns(skill);

        var convo = new Domain.Chat.Conversation(
            ConversationId.New(),
            "test",
            clock.UtcNow,
            clock.UtcNow);
        await repo.AddAsync(convo, CancellationToken.None);

        var service = new SkillInvocationService(
            registry,
            runtime,
            policy,
            repo,
            clock,
            NullLogger<SkillInvocationService>.Instance);

        return new Fakes(service, policy, runtime, repo, clock, convo.Id);
    }

    private sealed record Fakes(
        SkillInvocationService Service,
        IPolicyEngine Policy,
        FakeRuntimeManager Runtime,
        FakeChatRepository Repo,
        FakeClock Clock,
        ConversationId ConversationId);
}
