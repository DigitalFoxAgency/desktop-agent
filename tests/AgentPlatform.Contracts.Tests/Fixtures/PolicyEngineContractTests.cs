using AgentPlatform.Application.Policies;
using AgentPlatform.Domain.Policies;
using FluentAssertions;
using Xunit;

namespace AgentPlatform.Contracts.Tests.Fixtures;

/// <summary>
/// Behaviour any IPolicyEngine implementation must satisfy. Real implementations should
/// inherit from this base and supply their concrete engine via <see cref="CreateEngine"/>.
/// </summary>
public abstract class PolicyEngineContractTests
{
    protected abstract IPolicyEngine CreateEngine();

    [Fact]
    public async Task Bash_rm_is_classified_as_DeleteFile()
    {
        var engine = CreateEngine();
        var decision = await engine.ClassifyAsync(
            new IntentDescriptor("Bash", "rm -rf node_modules/foo", null, null, null),
            CancellationToken.None);
        decision.Classification.Should().Be(ActionClassification.DeleteFile);
        decision.RequiresConfirmation.Should().BeTrue();
    }

    [Fact]
    public async Task Git_push_is_classified_as_GitPush()
    {
        var engine = CreateEngine();
        var decision = await engine.ClassifyAsync(
            new IntentDescriptor("Bash", "git push origin main", null, null, null),
            CancellationToken.None);
        decision.Classification.Should().Be(ActionClassification.GitPush);
    }

    [Fact]
    public async Task Npm_install_is_InstallPackage_and_takes_build_semaphore()
    {
        var engine = CreateEngine();
        var decision = await engine.ClassifyAsync(
            new IntentDescriptor("Bash", "npm install", null, null, null),
            CancellationToken.None);
        decision.Classification.Should().Be(ActionClassification.InstallPackage);
        decision.RequiresBuildSemaphore.Should().BeTrue();
    }

    [Fact]
    public async Task Edit_in_working_dir_is_safe()
    {
        var engine = CreateEngine();
        var decision = await engine.ClassifyAsync(
            new IntentDescriptor("Edit", null, "/work/index.ts", null, null),
            CancellationToken.None);
        decision.Classification.Should().Be(ActionClassification.Safe);
        decision.RequiresConfirmation.Should().BeFalse();
    }
}

public sealed class DefaultPolicyEngineContractTests : PolicyEngineContractTests
{
    protected override IPolicyEngine CreateEngine() => new DefaultPolicyEngine();
}
