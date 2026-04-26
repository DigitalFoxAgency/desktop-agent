using AgentDesktop.Domain;
using AgentDesktop.Infrastructure.Subscription;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentDesktop.Infrastructure.Tests.Subscription;

/// <summary>
/// Integration test for <see cref="HttpSubscriptionValidator"/>
/// against an in-process instance of <c>AgentDesktop.Api</c>.
/// </summary>
public sealed class HttpSubscriptionValidatorTests : IClassFixture<WebApplicationFactory<AgentDesktop.Api.Program>>
{
    private readonly WebApplicationFactory<AgentDesktop.Api.Program> _factory;

    public HttpSubscriptionValidatorTests(WebApplicationFactory<AgentDesktop.Api.Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ValidateAsync_returns_Active_for_a_non_empty_token()
    {
        var client = _factory.CreateClient();
        var validator = new HttpSubscriptionValidator(client, NullLogger<HttpSubscriptionValidator>.Instance);

        var account = await validator.ValidateAsync("any-non-empty-token", CancellationToken.None);

        account.SubscriptionStatus.Should().Be(SubscriptionStatus.Active);
        account.AccountId.Value.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ValidateAsync_throws_for_empty_token()
    {
        var client = _factory.CreateClient();
        var validator = new HttpSubscriptionValidator(client, NullLogger<HttpSubscriptionValidator>.Instance);

        var act = async () => await validator.ValidateAsync(string.Empty, CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
