using AgentPlatform.Application.RunContainers;
using FluentAssertions;
using Xunit;

namespace AgentPlatform.Contracts.Tests.Fixtures;

public abstract class RunContainerDriverContractTests
{
    protected abstract IRunContainerDriver CreateDriver();

    private static RunContainerSpec NewSpec() => new(
        TenantId: Guid.NewGuid(),
        WorkflowRunId: Guid.NewGuid(),
        PhaseRunId: Guid.NewGuid(),
        Image: "agentplatform/run-base:latest",
        WorkingDirHostPath: "/tmp/work",
        Environment: new Dictionary<string, string> { ["ANTHROPIC_API_KEY"] = "sk-test" },
        MemoryMegabytes: 2048,
        CpuLimit: 1.0);

    [Fact]
    public async Task Start_returns_handle_with_container_id()
    {
        var driver = CreateDriver();
        var handle = await driver.StartAsync(NewSpec(), CancellationToken.None);
        handle.ContainerId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Stop_after_Start_does_not_throw()
    {
        var driver = CreateDriver();
        var h = await driver.StartAsync(NewSpec(), CancellationToken.None);
        var act = async () => await driver.StopAsync(h.ContainerId, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureVolume_is_idempotent()
    {
        var driver = CreateDriver();
        var runId = Guid.NewGuid().ToString("N");
        await driver.EnsureVolumeAsync(runId, CancellationToken.None);
        var act = async () => await driver.EnsureVolumeAsync(runId, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }
}
