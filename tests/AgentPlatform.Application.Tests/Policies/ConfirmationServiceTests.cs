using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Bridge;
using AgentPlatform.Application.Policies;
using AgentPlatform.Application.Runs;
using AgentPlatform.Domain.Audit;
using AgentPlatform.Domain.Inbox;
using AgentPlatform.Domain.Policies;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Policies;

public sealed class ConfirmationServiceTests
{
    [Fact]
    public async Task RecordProposalAsync_persists_request_and_audit()
    {
        // T138: audit row per ConfirmationRequest — proposed branch.
        var harness = new Harness();

        await harness.Service.RecordProposalAsync(
            harness.TenantId,
            harness.PhaseRunId,
            requestedByUserId: null,
            confirmationId: harness.ConfirmationId,
            classification: ActionClassification.DeleteFile,
            summary: "Delete /workspace/foo",
            targetPath: "/workspace/foo",
            commandLine: "rm /workspace/foo",
            cancellationToken: default);

        harness.Repo.Confirmations.Should().HaveCount(1);
        harness.Repo.Confirmations[0].Id.Should().Be(harness.ConfirmationId);
        harness.Repo.Confirmations[0].Classification.Should().Be(ActionClassification.DeleteFile);
        harness.Repo.DangerousActions.Should().HaveCount(1);
        await harness.Audit.Received(1).WriteAsync(
            Arg.Is<AuditEntry>(a => a.Category == "confirmation" && a.Action == "proposed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DecideAsync_returns_NotFound_for_unknown_id()
    {
        var harness = new Harness();
        var result = await harness.Service.DecideAsync(harness.TenantId, harness.UserId, Guid.NewGuid(), confirmed: true, note: null, default);
        result.Should().Be(DecideResult.NotFound);
    }

    [Fact]
    public async Task DecideAsync_records_decision_and_dispatches_to_bridge()
    {
        var harness = new Harness();
        await harness.SeedRequestAsync();

        var result = await harness.Service.DecideAsync(
            harness.TenantId,
            harness.UserId,
            harness.ConfirmationId,
            confirmed: true,
            note: "lgtm",
            default);

        result.Should().Be(DecideResult.Ok);
        var stored = harness.Repo.Confirmations[0];
        stored.Decision.Should().NotBeNull();
        stored.Decision!.Confirmed.Should().BeTrue();
        stored.Decision.Note.Should().Be("lgtm");

        await harness.Channel.Received(1).ResolveConfirmationAsync(
            harness.ConfirmationId,
            true,
            "lgtm",
            Arg.Any<CancellationToken>());

        await harness.Audit.Received(1).WriteAsync(
            Arg.Is<AuditEntry>(a => a.Category == "confirmation" && a.Action == "confirmed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DecideAsync_decline_branch_audits_declined()
    {
        var harness = new Harness();
        await harness.SeedRequestAsync();

        var result = await harness.Service.DecideAsync(
            harness.TenantId,
            harness.UserId,
            harness.ConfirmationId,
            confirmed: false,
            note: null,
            default);

        result.Should().Be(DecideResult.Ok);
        await harness.Audit.Received(1).WriteAsync(
            Arg.Is<AuditEntry>(a => a.Category == "confirmation" && a.Action == "declined"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DecideAsync_returns_AlreadyDecided_when_request_has_decision()
    {
        var harness = new Harness();
        await harness.SeedRequestAsync();

        var first = await harness.Service.DecideAsync(harness.TenantId, harness.UserId, harness.ConfirmationId, true, null, default);
        var second = await harness.Service.DecideAsync(harness.TenantId, harness.UserId, harness.ConfirmationId, true, null, default);

        first.Should().Be(DecideResult.Ok);
        second.Should().Be(DecideResult.AlreadyDecided);
    }

    [Fact]
    public async Task DecideAsync_returns_OkSessionGone_when_phase_session_dropped()
    {
        var harness = new Harness(activeSession: false);
        await harness.SeedRequestAsync();

        var result = await harness.Service.DecideAsync(harness.TenantId, harness.UserId, harness.ConfirmationId, true, null, default);

        result.Should().Be(DecideResult.OkSessionGone);
    }

    private sealed class Harness
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid PhaseRunId { get; } = Guid.NewGuid();
        public Guid ConfirmationId { get; } = Guid.NewGuid();
        public InMemoryConfirmationRepo Repo { get; } = new();
        public IAuditLog Audit { get; } = Substitute.For<IAuditLog>();
        public IBridgeChannel Channel { get; } = Substitute.For<IBridgeChannel>();
        public ConfirmationService Service { get; }

        public Harness(bool activeSession = true)
        {
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(DateTimeOffset.UtcNow);

            var sessions = Substitute.For<IPhaseSessionService>();
            if (activeSession)
            {
                sessions.GetActive(PhaseRunId).Returns(new PhaseSessionHandle
                {
                    PhaseRunId = PhaseRunId,
                    TenantId = TenantId,
                    WorkflowRunId = Guid.NewGuid(),
                    ContainerId = "c1",
                    WorkingDir = "/tmp",
                    Channel = Channel,
                    StartedAt = DateTimeOffset.UtcNow,
                });
            }

            Service = new ConfirmationService(Repo, Audit, clock, sessions, NullLogger<ConfirmationService>.Instance);
        }

        public Task SeedRequestAsync()
            => Service.RecordProposalAsync(
                TenantId,
                PhaseRunId,
                null,
                ConfirmationId,
                ActionClassification.DeleteFile,
                "Delete x",
                "/workspace/x",
                "rm /workspace/x",
                default);
    }

    private sealed class InMemoryConfirmationRepo : IConfirmationRepository
    {
        public List<ConfirmationRequest> Confirmations { get; } = [];
        public List<DangerousAction> DangerousActions { get; } = [];

        public Task AddAsync(ConfirmationRequest request, CancellationToken cancellationToken)
        {
            Confirmations.Add(request);
            return Task.CompletedTask;
        }

        public Task<ConfirmationRequest?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
            => Task.FromResult(Confirmations.FirstOrDefault(c => c.Id == id && c.TenantId == tenantId));

        public Task UpdateAsync(ConfirmationRequest request, CancellationToken cancellationToken)
        {
            var idx = Confirmations.FindIndex(c => c.Id == request.Id);
            if (idx >= 0) { Confirmations[idx] = request; }
            return Task.CompletedTask;
        }

        public Task AddDangerousActionAsync(DangerousAction action, CancellationToken cancellationToken)
        {
            DangerousActions.Add(action);
            return Task.CompletedTask;
        }
    }
}
