using System.Net;
using AwesomeAssertions;
using Catalog.Api.Tests.Support;
using Catalog.Infrastructure.Tests.Support;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// The API learns about a rotated signing key without a restart (plan section 8.7, REQ-IDN-003). The handler keeps the key set of the metadata, and it reads the metadata again when a token names a key that it does not
/// hold, at most once for each refresh interval, which these hosts set to one second. A test waits for the answer that it expects with a bounded poll, and never with a pause.
/// </summary>
public sealed class JwksRotationTests : IAsyncLifetime
{
    private TestIssuer _issuer = null!;
    private ApiTestHost _host = null!;

    public async ValueTask InitializeAsync()
    {
        _issuer = await TestIssuer.StartAsync();
        _host = await ApiTestHost.StartAsync(new HostSettings(_issuer), TestEndpoints.Map);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _issuer.DisposeAsync();
    }

    private async Task<HttpStatusCode> StatusAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/read");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var client = _host.Client;
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        return response.StatusCode;
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    public async Task A_token_signed_with_a_new_key_is_accepted_after_the_issuer_publishes_the_key()
    {
        using var rotated = TestKey.Rsa("key-2");
        var current = _issuer.Token();
        var next = _issuer.Token(new TokenSpec { Key = rotated });

        (await StatusAsync(current)).Should().Be(HttpStatusCode.OK, "the API reads the first key set when it meets the first token");
        (await StatusAsync(next)).Should().Be(HttpStatusCode.Unauthorized, "the new key is not published yet");

        _issuer.Publish(_issuer.Signing, rotated);

        await Poll.UntilAsync(async () => await StatusAsync(next) == HttpStatusCode.OK, "the API reads the metadata again and finds the new key", TestContext.Current.CancellationToken);
        (await StatusAsync(current)).Should().Be(HttpStatusCode.OK, "the old key is published too during a rotation");
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    public async Task A_token_signed_with_a_key_that_the_issuer_stopped_publishing_is_refused_after_the_next_refresh()
    {
        using var rotated = TestKey.Rsa("key-2");
        using var stranger = TestKey.Rsa("key-3");
        var old = _issuer.Token();
        var unknown = _issuer.Token(new TokenSpec { Key = stranger });

        (await StatusAsync(old)).Should().Be(HttpStatusCode.OK);

        _issuer.Publish(rotated);

        // A token that names an unknown key makes the handler read the metadata again, which is the only trigger of a refresh in this host. The previous key set is not kept as a fallback, so the withdrawn key stops working.
        await Poll.UntilAsync(
            async () =>
            {
                _ = await StatusAsync(unknown);

                return await StatusAsync(_issuer.Token()) == HttpStatusCode.Unauthorized;
            },
            "the API reads the metadata again and drops the key that is no longer published",
            TestContext.Current.CancellationToken);
        (await StatusAsync(_issuer.Token(new TokenSpec { Key = rotated }))).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    public async Task The_issuer_is_the_one_of_the_settings_even_when_the_metadata_advertises_another()
    {
        await using var other = await TestIssuer.StartAsync();
        other.AdvertisedIssuer = "http://other.example.test/realms/catalog";
        await using var host = await ApiTestHost.StartAsync(new HostSettings(other), TestEndpoints.Map);

        async Task<HttpStatusCode> SendAsync(string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/test/read");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var client = host.Client;
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            return response.StatusCode;
        }

        (await SendAsync(other.Token())).Should().Be(HttpStatusCode.OK);
        (await SendAsync(other.Token(new TokenSpec { Issuer = other.AdvertisedIssuer }))).Should().Be(HttpStatusCode.Unauthorized, "a token of the advertised issuer is not a token of the configured one");
    }
}
