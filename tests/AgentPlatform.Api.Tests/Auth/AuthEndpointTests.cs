using System.Net;
using System.Net.Http.Json;
using AgentPlatform.Api.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AgentPlatform.Api.Tests.Auth;

[Collection(ApiCollection.Name)]
public sealed class AuthEndpointTests
{
    private readonly ApiFactory _factory;

    public AuthEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Signup_then_signin_returns_token()
    {
        var client = _factory.CreateClient();
        var slug = "auth-" + Guid.NewGuid().ToString("N")[..8];
        var email = $"{slug}@example.com";

        var signup = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            agencyName = "Auth Agency",
            agencySlug = slug,
            email,
            password = "Aa1aaaaaaaa",
            displayName = "Owner",
        });
        signup.StatusCode.Should().Be(HttpStatusCode.OK);

        var signin = await client.PostAsJsonAsync("/api/auth/signin", new { email, password = "Aa1aaaaaaaa" });
        signin.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await signin.Content.ReadFromJsonAsync<ApiFactory.SignInResponseDto>();
        body.Should().NotBeNull();
        body!.Token.Should().NotBeNullOrWhiteSpace();
        body.Roles.Should().Contain("admin");
    }

    [Fact]
    public async Task Signin_with_bad_password_returns_401()
    {
        var client = _factory.CreateClient();
        var slug = "auth-" + Guid.NewGuid().ToString("N")[..8];
        var email = $"{slug}@example.com";

        var signup = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            agencyName = "Bad Pwd Agency",
            agencySlug = slug,
            email,
            password = "Aa1aaaaaaaa",
            displayName = "Owner",
        });
        signup.EnsureSuccessStatusCode();

        var signin = await client.PostAsJsonAsync("/api/auth/signin", new { email, password = "wrong" });
        signin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
