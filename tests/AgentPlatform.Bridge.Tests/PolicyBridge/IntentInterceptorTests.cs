using AgentPlatform.Application.Policies;
using AgentPlatform.Application.RunContainers;
using AgentPlatform.Bridge.ClaudeWrapper;
using AgentPlatform.Bridge.PolicyBridge;
using AgentPlatform.Domain.Policies;
using FluentAssertions;
using Xunit;

namespace AgentPlatform.Bridge.Tests.PolicyBridge;

public sealed class IntentInterceptorTests
{
    [Fact]
    public async Task Safe_edit_forwards_without_confirmation()
    {
        var sut = NewInterceptor();
        var proposed = new ClaudeStreamEvent.ToolUseProposed("Edit", null, "/workspace/foo.txt", "{}");

        var result = await sut.InspectAsync(proposed, default);

        result.ConfirmationRequired.Should().BeFalse();
        result.ConfirmationId.Should().BeNull();
        result.Decision.Classification.Should().Be(ActionClassification.Safe);
    }

    [Fact]
    public async Task Rm_command_emits_confirmation_with_fresh_id()
    {
        var sut = NewInterceptor();
        var proposed = new ClaudeStreamEvent.ToolUseProposed("Bash", "rm /workspace/junk", null, "{}");

        var result = await sut.InspectAsync(proposed, default);

        result.ConfirmationRequired.Should().BeTrue();
        result.ConfirmationId.Should().NotBeNull();
        result.Decision.Classification.Should().Be(ActionClassification.DeleteFile);
        result.Summary.Should().Contain("Delete");
        result.CommandLine.Should().Be("rm /workspace/junk");
    }

    [Fact]
    public async Task Per_occurrence_each_dangerous_intent_gets_its_own_id()
    {
        // T132: confirming once does NOT implicitly confirm subsequent prompts.
        // The interceptor must register a new id every time it sees a
        // dangerous intent — the gate's outstanding count grows, never reused.
        var gate = new ConfirmationGate();
        var sut = NewInterceptor(gate);

        var first = await sut.InspectAsync(new ClaudeStreamEvent.ToolUseProposed("Bash", "rm /workspace/a", null, "{}"), default);
        var second = await sut.InspectAsync(new ClaudeStreamEvent.ToolUseProposed("Bash", "rm /workspace/b", null, "{}"), default);

        first.ConfirmationId.Should().NotBeNull();
        second.ConfirmationId.Should().NotBeNull();
        first.ConfirmationId!.Value.Should().NotBe(second.ConfirmationId!.Value);
        gate.OutstandingCount.Should().Be(2);
    }

    [Fact]
    public async Task Build_class_intent_marks_decision_for_semaphore()
    {
        var sut = NewInterceptor();
        var proposed = new ClaudeStreamEvent.ToolUseProposed("Bash", "npm install lodash", null, "{}");

        var result = await sut.InspectAsync(proposed, default);

        result.ConfirmationRequired.Should().BeTrue();
        result.Decision.RequiresBuildSemaphore.Should().BeTrue();
        result.Decision.Classification.Should().Be(ActionClassification.InstallPackage);
    }

    [Fact]
    public async Task AcquireBuildSlotAsync_blocks_when_capacity_exhausted()
    {
        // T131 in unit form: acquiring more build slots than capacity blocks
        // until one is released. Two slots are held; a third call must not
        // resolve until one disposes.
        var semaphore = new BuildStepSemaphore(capacity: 2);
        var sut = NewInterceptor(buildSemaphore: semaphore);
        var npmInstall = await sut.InspectAsync(new ClaudeStreamEvent.ToolUseProposed("Bash", "npm install", null, "{}"), default);

        var slot1 = await sut.AcquireBuildSlotAsync(npmInstall.Decision, default);
        var slot2 = await sut.AcquireBuildSlotAsync(npmInstall.Decision, default);

        slot1.Should().NotBeNull();
        slot2.Should().NotBeNull();

        var third = sut.AcquireBuildSlotAsync(npmInstall.Decision, default);
        third.IsCompleted.Should().BeFalse();

        slot1!.Dispose();
        var slot3 = await third.WaitAsync(TimeSpan.FromSeconds(1));
        slot3.Should().NotBeNull();
        slot2!.Dispose();
        slot3!.Dispose();
    }

    [Fact]
    public async Task ConfirmationGate_resolves_pending_decision()
    {
        // T135: bridge waits for the API decision before proceeding.
        var gate = new ConfirmationGate();
        var id = gate.Register();
        var pending = gate.WaitForDecisionAsync(id, default);
        pending.IsCompleted.Should().BeFalse();

        gate.Resolve(id, confirmed: true, note: "lgtm").Should().BeTrue();
        var outcome = await pending.WaitAsync(TimeSpan.FromSeconds(1));

        outcome.Confirmed.Should().BeTrue();
        outcome.Note.Should().Be("lgtm");
        gate.OutstandingCount.Should().Be(0);
    }

    [Fact]
    public void ConfirmationGate_resolve_unknown_id_returns_false()
    {
        var gate = new ConfirmationGate();
        gate.Resolve(Guid.NewGuid(), confirmed: false, note: null).Should().BeFalse();
    }

    private static IntentInterceptor NewInterceptor(
        ConfirmationGate? gate = null,
        BuildStepSemaphore? buildSemaphore = null)
    {
        var classifier = new ActionClassifier(new DefaultPolicyEngine());
        return new IntentInterceptor(classifier, gate ?? new ConfirmationGate(), buildSemaphore);
    }
}
