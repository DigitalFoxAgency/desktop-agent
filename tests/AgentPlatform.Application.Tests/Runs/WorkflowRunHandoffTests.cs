using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Modules;
using AgentPlatform.Application.RunContainers;
using AgentPlatform.Application.Runs;
using AgentPlatform.Application.Subscription;
using AgentPlatform.Application.Tenants;
using AgentPlatform.Domain.Inbox;
using AgentPlatform.Domain.Modules;
using AgentPlatform.Domain.Runs;
using AgentPlatform.Domain.Tenants;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Runs;

/// <summary>
/// US3 hand-off coverage (T117–T119 application-layer slice). Tests run against
/// in-memory fakes for the repo/registry/tenants surfaces; full WebApplication
/// integration coverage lives under <c>tests/AgentPlatform.Api.Tests</c> and is
/// gated separately.
/// </summary>
public sealed class WorkflowRunHandoffTests
{
    [Fact]
    public async Task OnPhaseCompletedAsync_creates_next_phase_assigned_to_role_holder()
    {
        var h = new Harness();
        var result = await h.Service.OnPhaseCompletedAsync(h.TenantId, h.PhaseRunId1, verified: true, default);

        result.Succeeded.Should().BeTrue();
        result.NextPhaseRunId.Should().NotBeNull();
        result.NextAssignedUserId.Should().Be(h.MarketerId);
        result.Waiting.Should().BeFalse();

        h.Repo.Append.Should().NotBeNull();
        h.Repo.Append!.NextPhase!.PhaseId.Should().Be("pre-research");
        h.Repo.Append.Inbox.Should().NotBeNull();
        h.Repo.Append.Inbox!.UserId.Should().Be(h.MarketerId);
        h.Repo.Run.Status.Should().Be(RunStatus.Running);
    }

    [Fact]
    public async Task OnPhaseCompletedAsync_marks_run_completed_when_no_next_phase()
    {
        var h = new Harness();
        // Drive completion of the second (last) phase by first completing the first.
        await h.Service.OnPhaseCompletedAsync(h.TenantId, h.PhaseRunId1, true, default);
        // Now complete the newly-created next phase.
        var nextId = h.Repo.Append!.NextPhase!.Id;
        h.Repo.PromoteAppendedToCurrent();

        var result = await h.Service.OnPhaseCompletedAsync(h.TenantId, nextId, verified: true, default);

        result.Succeeded.Should().BeTrue();
        result.NextPhaseRunId.Should().BeNull();
        h.Repo.Run.Status.Should().Be(RunStatus.Completed);
        h.Driver.Archived.Should().Contain(h.RunId.ToString("N"));
    }

    [Fact]
    public async Task OnPhaseCompletedAsync_pauses_run_when_next_role_has_no_holder()
    {
        var h = new Harness(includeMarketer: false);
        var result = await h.Service.OnPhaseCompletedAsync(h.TenantId, h.PhaseRunId1, true, default);

        result.Succeeded.Should().BeTrue();
        result.Waiting.Should().BeTrue();
        result.NextAssignedUserId.Should().BeNull();
        h.Repo.Append!.Inbox.Should().BeNull();
        h.Repo.Run.Status.Should().Be(RunStatus.Waiting);
    }

    [Fact]
    public async Task ReassignAsync_rejects_user_without_required_role()
    {
        var h = new Harness();
        var stranger = Guid.NewGuid();
        h.TenantRepo.GetRolesAsync(h.TenantId, stranger, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Role>>(Array.Empty<Role>()));

        h.Repo.SeedAssignment(h.PhaseRunId1, Role.Engineer);
        var result = await h.Service.ReassignAsync(h.TenantId, h.AdminId, h.PhaseRunId1, stranger, default);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("required role");
    }

    [Fact]
    public async Task ReassignAsync_resolves_waiting_assignment_and_resumes_run()
    {
        var h = new Harness(includeMarketer: false);
        await h.Service.OnPhaseCompletedAsync(h.TenantId, h.PhaseRunId1, true, default);

        // A marketer is added later.
        var marketer = Guid.NewGuid();
        h.TenantRepo.GetRolesAsync(h.TenantId, marketer, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Role>>(new[] { Role.Marketer }));

        var nextPhaseId = h.Repo.Append!.NextPhase!.Id;
        h.Repo.PromoteAppendedToCurrent();
        h.Repo.SeedAssignment(nextPhaseId, Role.Marketer, state: AssignmentState.Waiting);

        var result = await h.Service.ReassignAsync(h.TenantId, h.AdminId, nextPhaseId, marketer, default);

        result.Succeeded.Should().BeTrue();
        result.NewUserId.Should().Be(marketer);
        h.Repo.Run.Status.Should().Be(RunStatus.Running);
    }

    private sealed class Harness
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid RunId { get; } = Guid.NewGuid();
        public Guid PhaseDef1Id { get; } = Guid.NewGuid();
        public Guid PhaseDef2Id { get; } = Guid.NewGuid();
        public Guid PhaseRunId1 { get; } = Guid.NewGuid();
        public Guid AdminId { get; } = Guid.NewGuid();
        public Guid EngineerId { get; } = Guid.NewGuid();
        public Guid MarketerId { get; } = Guid.NewGuid();

        public ITenantRepository TenantRepo { get; }
        public TenantService Tenants { get; }
        public IModuleRegistry Modules { get; }
        public InMemoryRunRepo Repo { get; }
        public PhaseAssignmentService Assignments { get; }
        public IAuditLog Audit { get; } = Substitute.For<IAuditLog>();
        public IInboxNotifier Notifier { get; } = Substitute.For<IInboxNotifier>();
        public TestRunDriver Driver { get; } = new();
        public WorkflowRunService Service { get; }

        public Harness(bool includeMarketer = true)
        {
            TenantRepo = Substitute.For<ITenantRepository>();
            List<User> marketers = includeMarketer
                ? [new() { Id = MarketerId, TenantId = TenantId, Email = "m@x", DisplayName = "M", CreatedAt = DateTimeOffset.UtcNow }]
                : [];
            TenantRepo.FindUsersByRoleAsync(TenantId, Role.Marketer, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<User>>(marketers));
            TenantRepo.FindUsersByRoleAsync(TenantId, Role.Engineer, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<User>>(new List<User> { new() { Id = EngineerId, TenantId = TenantId, Email = "e@x", DisplayName = "E", CreatedAt = DateTimeOffset.UtcNow } }));
            TenantRepo.GetRolesAsync(TenantId, MarketerId, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyCollection<Role>>(new[] { Role.Marketer }));
            TenantRepo.GetRolesAsync(TenantId, EngineerId, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyCollection<Role>>(new[] { Role.Engineer }));

            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(DateTimeOffset.UtcNow);

            Tenants = new TenantService(TenantRepo, clock);
            Assignments = new PhaseAssignmentService(Tenants, clock);

            var workflow = new WorkflowDef
            {
                Id = Guid.NewGuid(),
                WorkflowId = "onboard-client",
                DisplayName = "Onboard Client",
                Phases =
                {
                    new PhaseDef { Id = PhaseDef1Id, Order = 0, PhaseId = "init", DisplayName = "Init", Role = Role.Engineer, Skill = "init" },
                    new PhaseDef { Id = PhaseDef2Id, Order = 1, PhaseId = "pre-research", DisplayName = "Pre-research", Role = Role.Marketer, Skill = "pre-research" },
                },
            };
            Modules = Substitute.For<IModuleRegistry>();
            Modules.GetWorkflowAsync("df-client-launchpad", "onboard-client", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<WorkflowDef?>(workflow));

            var run = new WorkflowRun
            {
                Id = RunId,
                TenantId = TenantId,
                ModuleId = "df-client-launchpad",
                WorkflowId = "onboard-client",
                Status = RunStatus.Running,
                StartedAt = DateTimeOffset.UtcNow,
                WorkingDirPath = "/tmp/run",
                StartedByUserId = AdminId,
            };
            run.Phases.Add(new PhaseRun
            {
                Id = PhaseRunId1,
                TenantId = TenantId,
                WorkflowRunId = RunId,
                PhaseDefId = PhaseDef1Id,
                Order = 0,
                PhaseId = "init",
                Status = RunStatus.Running,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            Repo = new InMemoryRunRepo(run);

            var subscription = Substitute.For<ISubscriptionGate>();
            subscription.CheckAsync(TenantId, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new SubscriptionDecision(true, null)));

            var workingDir = Substitute.For<IWorkingDirectoryProvider>();

            Service = new WorkflowRunService(
                Modules, subscription, Repo, Assignments, Audit, Notifier, workingDir, Tenants, Driver, clock);
        }
    }

    private sealed class InMemoryRunRepo(WorkflowRun run) : IWorkflowRunRepository
    {
        public WorkflowRun Run { get; } = run;
        public AppendCall? Append { get; private set; }
        public Assignment? CurrentAssignment { get; private set; }

        public Task<WorkflowRun?> GetAsync(Guid tenantId, Guid runId, CancellationToken cancellationToken)
            => Task.FromResult<WorkflowRun?>(Run.Id == runId ? Run : null);

        public Task<WorkflowRun?> GetRunByPhaseAsync(Guid tenantId, Guid phaseRunId, CancellationToken cancellationToken)
            => Task.FromResult<WorkflowRun?>(Run.Phases.Any(p => p.Id == phaseRunId) ? Run : null);

        public Task SaveNewRunAsync(WorkflowRun run, PhaseRun firstPhase, Assignment assignment, InboxItem? inboxItem, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AppendNextPhaseAsync(PhaseRun completedPhase, WorkflowRun run, PhaseRun? nextPhase, Assignment? assignment, InboxItem? inboxItem, CancellationToken cancellationToken)
        {
            Append = new AppendCall(completedPhase, nextPhase, assignment, inboxItem);
            if (nextPhase is not null) { Run.Phases.Add(nextPhase); }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WorkflowRun>> ListAsync(Guid tenantId, int limit, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<WorkflowRun>>(new[] { Run });

        public Task<IReadOnlyList<InboxItem>> ListInboxAsync(Guid tenantId, Guid userId, int limit, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<InboxItem>>(Array.Empty<InboxItem>());

        public Task<IReadOnlyList<InboxItemWithContext>> ListInboxWithContextAsync(Guid tenantId, Guid userId, int limit, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<InboxItemWithContext>>(Array.Empty<InboxItemWithContext>());

        public Task<AssignmentLookup?> GetCurrentAssignmentAsync(Guid tenantId, Guid phaseRunId, CancellationToken cancellationToken)
        {
            var phase = Run.Phases.FirstOrDefault(p => p.Id == phaseRunId);
            if (phase is null || CurrentAssignment is null || CurrentAssignment.PhaseRunId != phaseRunId) { return Task.FromResult<AssignmentLookup?>(null); }
            return Task.FromResult<AssignmentLookup?>(new AssignmentLookup(CurrentAssignment, CurrentAssignment.RequiredRole, phase, Run));
        }

        public Task ReassignAsync(Assignment current, InboxItem? newInbox, WorkflowRun run, CancellationToken cancellationToken)
        {
            CurrentAssignment = current;
            return Task.CompletedTask;
        }

        public void SeedAssignment(Guid phaseRunId, Role role, AssignmentState state = AssignmentState.Assigned)
        {
            CurrentAssignment = new Assignment
            {
                Id = Guid.NewGuid(),
                TenantId = Run.TenantId,
                PhaseRunId = phaseRunId,
                RequiredRole = role,
                State = state,
                AssignedUserId = state == AssignmentState.Waiting ? null : Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
            };
        }

        public void PromoteAppendedToCurrent()
        {
            if (Append?.Assignment is not null) { CurrentAssignment = Append.Assignment; }
        }

        public sealed record AppendCall(PhaseRun CompletedPhase, PhaseRun? NextPhase, Assignment? Assignment, InboxItem? Inbox);
    }

    private sealed class TestRunDriver : IRunContainerDriver
    {
        public List<string> Archived { get; } = [];
        public Task<RunContainerHandle> StartAsync(RunContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(new RunContainerHandle("c", DateTimeOffset.UtcNow));
        public Task StopAsync(string containerId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task EnsureVolumeAsync(string runId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ArchiveVolumeAsync(string runId, CancellationToken cancellationToken)
        {
            Archived.Add(runId);
            return Task.CompletedTask;
        }
    }
}
