using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Bridge;
using AgentPlatform.Application.Modules;
using AgentPlatform.Application.RunContainers;
using AgentPlatform.Application.Runs;
using AgentPlatform.Application.Secrets;
using AgentPlatform.Application.Usage;
using AgentPlatform.Domain.Audit;
using AgentPlatform.Domain.Modules;
using AgentPlatform.Domain.Runs;
using AgentPlatform.Domain.Tenants;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Runs;

public sealed class PhaseSessionServiceTests
{
    [Fact]
    public async Task OpenAsync_starts_container_and_returns_session_handle()
    {
        var harness = new Harness();
        var result = await harness.Service.OpenAsync(harness.TenantId, harness.UserId, harness.PhaseRunId, default);

        result.Succeeded.Should().BeTrue();
        result.Session.Should().NotBeNull();
        result.Session!.ContainerId.Should().Be("container-1");
        result.Session.WorkflowRunId.Should().Be(harness.RunId);

        harness.Driver.Started.Should().HaveCount(1);
        harness.Driver.Started[0].PhaseRunId.Should().Be(harness.PhaseRunId);

        await harness.Audit.Received(1).WriteAsync(
            Arg.Is<AuditEntry>(a => a.Category == "phase" && a.Action == "open"),
            Arg.Any<CancellationToken>());

        harness.Service.GetActive(harness.PhaseRunId).Should().NotBeNull();
    }

    [Fact]
    public async Task OpenAsync_is_idempotent_for_an_active_phase()
    {
        var harness = new Harness();
        var first = await harness.Service.OpenAsync(harness.TenantId, harness.UserId, harness.PhaseRunId, default);
        var second = await harness.Service.OpenAsync(harness.TenantId, harness.UserId, harness.PhaseRunId, default);

        first.Session.Should().BeSameAs(second.Session);
        harness.Driver.Started.Should().HaveCount(1);
    }

    [Fact]
    public async Task OpenAsync_returns_failure_when_phase_unknown()
    {
        var harness = new Harness();
        var unknown = Guid.NewGuid();

        var result = await harness.Service.OpenAsync(harness.TenantId, harness.UserId, unknown, default);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("phase not found");
    }

    [Fact]
    public async Task CloseAsync_stops_container_and_clears_active_handle()
    {
        var harness = new Harness();
        var open = await harness.Service.OpenAsync(harness.TenantId, harness.UserId, harness.PhaseRunId, default);
        open.Succeeded.Should().BeTrue();

        await harness.Service.CloseAsync(harness.PhaseRunId, default);

        harness.Driver.Stopped.Should().Contain("container-1");
        harness.Service.GetActive(harness.PhaseRunId).Should().BeNull();
    }

    private sealed class Harness
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid RunId { get; } = Guid.NewGuid();
        public Guid PhaseRunId { get; } = Guid.NewGuid();
        public Guid PhaseDefId { get; } = Guid.NewGuid();
        public StubDriver Driver { get; } = new();
        public StubBridgeFactory Bridges { get; } = new();
        public IAuditLog Audit { get; } = Substitute.For<IAuditLog>();
        public PhaseSessionService Service { get; }

        public Harness()
        {
            var phaseRun = new PhaseRun
            {
                Id = PhaseRunId,
                TenantId = TenantId,
                WorkflowRunId = RunId,
                PhaseDefId = PhaseDefId,
                Order = 0,
                PhaseId = "init",
                Status = RunStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            var workflowRun = new WorkflowRun
            {
                Id = RunId,
                TenantId = TenantId,
                ModuleId = "df-client-launchpad",
                WorkflowId = "onboard-client",
                StartedAt = DateTimeOffset.UtcNow,
                Status = RunStatus.Running,
                WorkingDirPath = "/tmp/agp/run",
            };
            var phaseRepo = new InMemoryPhaseRepo(new[] { (phaseRun, workflowRun) });

            var workflow = new WorkflowDef
            {
                Id = Guid.NewGuid(),
                ModuleVersionId = Guid.NewGuid(),
                WorkflowId = "onboard-client",
                DisplayName = "Onboard Client",
                InputsSchemaJson = "[]",
                Phases = new()
                {
                    new PhaseDef
                    {
                        Id = PhaseDefId,
                        WorkflowDefId = Guid.NewGuid(),
                        PhaseId = "init",
                        DisplayName = "Init",
                        Skill = "init",
                        Role = Role.Engineer,
                        Order = 0,
                        Kind = PhaseKind.Standard,
                    },
                },
            };
            var modules = Substitute.For<IModuleRegistry>();
            modules.GetWorkflowAsync("df-client-launchpad", "onboard-client", Arg.Any<CancellationToken>())
                .Returns(workflow);

            var secrets = Substitute.For<ISecretStore>();
            secrets.ListKeysAsync(TenantId, Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());

            var meter = Substitute.For<IUsageMeter>();

            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(DateTimeOffset.UtcNow);

            var services = new ServiceCollection();
            services.AddSingleton<IPhaseRunRepository>(phaseRepo);
            services.AddSingleton(modules);
            services.AddSingleton(secrets);
            services.AddSingleton(meter);
            services.AddSingleton(Audit);
            services.AddSingleton(clock);
            var sp = services.BuildServiceProvider();

            Service = new PhaseSessionService(
                sp.GetRequiredService<IServiceScopeFactory>(),
                Driver,
                Bridges,
                clock,
                Options.Create(new PhaseSessionOptions
                {
                    BridgeConnectTimeout = TimeSpan.FromMilliseconds(500),
                }),
                NullLogger<PhaseSessionService>.Instance);
        }
    }

    private sealed class InMemoryPhaseRepo : IPhaseRunRepository
    {
        private readonly Dictionary<Guid, PhaseRunWithRun> _byPhase;

        public InMemoryPhaseRepo(IEnumerable<(PhaseRun phase, WorkflowRun run)> rows)
        {
            _byPhase = rows.ToDictionary(t => t.phase.Id, t => new PhaseRunWithRun(t.phase, t.run));
        }

        public Task<PhaseRunWithRun?> GetAsync(Guid tenantId, Guid phaseRunId, CancellationToken cancellationToken)
            => Task.FromResult(_byPhase.TryGetValue(phaseRunId, out var v) ? v : null);

        public Task UpdateAsync(PhaseRun phase, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubDriver : IRunContainerDriver
    {
        public List<RunContainerSpec> Started { get; } = new();
        public List<string> Stopped { get; } = new();
        private int _counter;

        public Task<RunContainerHandle> StartAsync(RunContainerSpec spec, CancellationToken cancellationToken)
        {
            Started.Add(spec);
            var id = $"container-{Interlocked.Increment(ref _counter)}";
            return Task.FromResult(new RunContainerHandle(id, DateTimeOffset.UtcNow));
        }

        public Task StopAsync(string containerId, CancellationToken cancellationToken)
        {
            Stopped.Add(containerId);
            return Task.CompletedTask;
        }

        public Task EnsureVolumeAsync(string runId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ArchiveVolumeAsync(string runId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubBridgeFactory : IBridgeChannelFactory
    {
        public Task<IBridgeChannel> WaitForConnectionAsync(Guid phaseRunId, TimeSpan timeout, CancellationToken cancellationToken)
            => Task.FromResult<IBridgeChannel>(new StubChannel());

        public string IssueConnectToken(Guid phaseRunId, Guid tenantId) => "stub-token";
    }

    private sealed class StubChannel : IBridgeChannel
    {
        public IAsyncEnumerable<BridgeEvent> ReadEventsAsync(CancellationToken cancellationToken)
            => AsyncEnumerable.Empty();

        public Task SendUserInputAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResolveConfirmationAsync(Guid confirmationId, bool confirmed, string? note, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public Task CloseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static class AsyncEnumerable
    {
        public static async IAsyncEnumerable<BridgeEvent> Empty()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
