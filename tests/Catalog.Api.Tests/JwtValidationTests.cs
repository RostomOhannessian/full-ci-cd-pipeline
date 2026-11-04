using System.Net;
using AwesomeAssertions;
using Catalog.Api.Tests.Support;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// Token validation through the real pipeline (plan section 8.7, REQ-IDN-003): the exact issuer and audience, the signature, the algorithms RS256 and ES256, and the lifetime with at most 30 seconds of skew. The tokens are
/// signed by an in-process issuer with keys made at run time, and the clock of the validation is a fake one, so no result depends on the time of the run.
/// </summary>
public sealed class JwtValidationTests(SecuredApiFixture api) : IClassFixture<SecuredApiFixture>
{
    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(30);

    private async Task<HttpResponseMessage> ReadAsync(string? token) => await api.SendAsync(HttpMethod.Get, "/test/read", token);

    private async Task AssertRefusedAsync(string? token)
    {
        using var response = await ReadAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await response.JsonAsync()).ProblemSlug().Should().Be("unauthorized");
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    public async Task A_valid_token_signed_with_RS256_is_accepted()
    {
        using var response = await ReadAsync(api.Token());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    public async Task A_valid_token_signed_with_ES256_is_accepted()
    {
        using var response = await ReadAsync(api.Token(new TokenSpec { Key = api.EcKey }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    public async Task A_request_without_a_token_is_challenged_with_the_realm_and_no_error()
    {
        using var response = await ReadAsync(null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Be("Bearer realm=\"catalog\"");
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    public async Task A_token_that_is_refused_is_challenged_with_the_invalid_token_error_and_no_description()
    {
        using var response = await ReadAsync(api.Token(new TokenSpec { Audience = "another-api" }));
        var challenge = response.Headers.WwwAuthenticate.ToString();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        challenge.Should().Be("Bearer realm=\"catalog\", error=\"invalid_token\"");
        challenge.Should().NotContain("error_description");
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    public async Task A_token_with_the_wrong_issuer_is_refused() => await AssertRefusedAsync(api.Token(new TokenSpec { Issuer = $"{api.Issuer.Address}/realms/other" }));

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    public async Task A_token_for_another_audience_is_refused() => await AssertRefusedAsync(api.Token(new TokenSpec { Audience = "account" }));

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    public async Task A_token_signed_with_a_key_that_the_issuer_does_not_publish_is_refused()
    {
        using var stranger = TestKey.Rsa("stranger");

        await AssertRefusedAsync(api.Token(new TokenSpec { Key = stranger }));
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    public async Task A_token_with_a_published_key_id_and_another_key_is_refused()
    {
        // The signature must verify with the key that the token names, so a forged signature under a real key id fails.
        using var impostor = TestKey.Rsa(api.Issuer.Signing.Id);

        await AssertRefusedAsync(api.Token(new TokenSpec { Key = impostor }));
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    public async Task A_token_signed_with_an_algorithm_outside_the_allow_list_is_refused_even_when_the_signature_is_valid()
    {
        // The key is the published one and the signature is correct, so only the allow-list of algorithms can refuse it.
        await AssertRefusedAsync(api.Token(new TokenSpec { Algorithm = "RS512" }));
        await AssertRefusedAsync(api.Token(new TokenSpec { Algorithm = "RS384" }));
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    public async Task A_token_that_uses_the_public_key_as_an_hmac_secret_is_refused() =>
        await AssertRefusedAsync(api.Issuer.HmacToken(new TokenSpec()));

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    public async Task An_unsigned_token_is_refused() => await AssertRefusedAsync(api.Issuer.UnsignedToken(new TokenSpec()));

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    public async Task A_token_without_an_expiry_is_refused() => await AssertRefusedAsync(api.Token(new TokenSpec { OmitExpiry = true }));

    [Theory]
    [Trait("Requirement", "REQ-IDN-003")]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("a.b.c")]
    [InlineData("eyJhbGciOiJSUzI1NiJ9.e30.")]
    public async Task A_value_that_is_not_a_token_is_refused(string value) => await AssertRefusedAsync(value);

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    public async Task A_token_that_expired_inside_the_skew_is_accepted_and_one_that_expired_before_it_is_refused()
    {
        var insideSkew = new TokenSpec { NotBeforeOffset = TimeSpan.FromMinutes(-10), LifetimeFromNow = -(Skew - TimeSpan.FromSeconds(1)) };
        var beyondSkew = new TokenSpec { NotBeforeOffset = TimeSpan.FromMinutes(-10), LifetimeFromNow = -(Skew + TimeSpan.FromSeconds(1)) };

        using var accepted = await ReadAsync(api.Token(insideSkew));
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertRefusedAsync(api.Token(beyondSkew));
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    public async Task A_token_that_is_not_valid_yet_is_accepted_inside_the_skew_and_refused_beyond_it()
    {
        var insideSkew = new TokenSpec { NotBeforeOffset = Skew - TimeSpan.FromSeconds(1), LifetimeFromNow = TimeSpan.FromMinutes(10) };
        var beyondSkew = new TokenSpec { NotBeforeOffset = Skew + TimeSpan.FromSeconds(1), LifetimeFromNow = TimeSpan.FromMinutes(10) };

        using var accepted = await ReadAsync(api.Token(insideSkew));
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertRefusedAsync(api.Token(beyondSkew));
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    public async Task A_token_in_the_query_string_is_not_read()
    {
        var token = api.Token();

        using var response = await api.SendAsync(HttpMethod.Get, $"/test/read?access_token={token}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    [Trait("Requirement", "REQ-SEC-006")]
    public async Task No_token_reaches_a_log_line_or_an_error_body()
    {
        var token = api.Token(new TokenSpec { Audience = "another-api" });

        using var response = await ReadAsync(token);
        var body = await response.TextAsync();
        var signature = token[(token.LastIndexOf('.') + 1)..];

        body.Should().NotContain(signature);
        api.Host.Log.Lines.Should().NotContain(line => line.Contains(signature, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    public async Task The_caller_scopes_and_roles_of_a_valid_token_reach_the_use_cases()
    {
        using var response = await api.SendAsync(HttpMethod.Get, "/test/whoami", api.Token(new TokenSpec { Subject = "caller-7", Scopes = ["catalog.read", "catalog.write"], Roles = ["catalog-editor"] }));
        var body = await response.JsonAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.GetProperty("callerId").GetString().Should().Be("caller-7");
        body.GetProperty("scopes").EnumerateArray().Select(scope => scope.GetString()).Should().Equal("catalog.read", "catalog.write");
        body.GetProperty("roles").EnumerateArray().Select(role => role.GetString()).Should().Equal("catalog-editor");
    }
}
