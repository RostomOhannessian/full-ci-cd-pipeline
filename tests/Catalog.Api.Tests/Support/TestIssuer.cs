using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Catalog.Api.Tests.Support;

/// <summary>A signing key of the test issuer, made at run time. No key is committed to the repository (plan section 9).</summary>
internal sealed class TestKey : IDisposable
{
    private readonly RSA? _rsa;
    private readonly ECDsa? _ecdsa;

    private TestKey(string id, RSA? rsa, ECDsa? ecdsa)
    {
        Id = id;
        _rsa = rsa;
        _ecdsa = ecdsa;
    }

    public string Id { get; }

    public SecurityKey SecurityKey => _rsa is not null ? new RsaSecurityKey(_rsa) { KeyId = Id } : new ECDsaSecurityKey(_ecdsa!) { KeyId = Id };

    public string Algorithm => _rsa is not null ? SecurityAlgorithms.RsaSha256 : SecurityAlgorithms.EcdsaSha256;

    public static TestKey Rsa(string id) => new(id, RSA.Create(2048), null);

    public static TestKey Ec(string id) => new(id, null, ECDsa.Create(ECCurve.NamedCurves.nistP256));

    public SigningCredentials Credentials(string? algorithm = null) => new(SecurityKey, algorithm ?? Algorithm);

    // The public part of the key as an entry of a JSON Web Key Set (RFC 7517).
    public Dictionary<string, string> PublicJwk()
    {
        if (_rsa is not null)
        {
            var parameters = _rsa.ExportParameters(false);

            return new() { ["kty"] = "RSA", ["use"] = "sig", ["alg"] = SecurityAlgorithms.RsaSha256, ["kid"] = Id, ["n"] = Base64UrlEncoder.Encode(parameters.Modulus!), ["e"] = Base64UrlEncoder.Encode(parameters.Exponent!) };
        }

        var point = _ecdsa!.ExportParameters(false);

        return new() { ["kty"] = "EC", ["use"] = "sig", ["alg"] = SecurityAlgorithms.EcdsaSha256, ["kid"] = Id, ["crv"] = "P-256", ["x"] = Base64UrlEncoder.Encode(point.Q.X!), ["y"] = Base64UrlEncoder.Encode(point.Q.Y!) };
    }

    public void Dispose()
    {
        _rsa?.Dispose();
        _ecdsa?.Dispose();
    }
}

/// <summary>What a test token says. Every member has the value of a token that the API accepts, so a test changes the one member that it is about.</summary>
internal sealed record TokenSpec
{
    public string Subject { get; init; } = "11111111-2222-3333-4444-555555555555";

    public string? Issuer { get; init; }

    public string Audience { get; init; } = "catalog-api";

    public string[] Scopes { get; init; } = ["catalog.read"];

    public string[] Roles { get; init; } = ["catalog-reader"];

    public TimeSpan NotBeforeOffset { get; init; } = TimeSpan.FromMinutes(-1);

    public TimeSpan LifetimeFromNow { get; init; } = TimeSpan.FromMinutes(5);

    public bool OmitExpiry { get; init; }

    public TestKey? Key { get; init; }

    public string? Algorithm { get; init; }
}

/// <summary>
/// The identity provider of the API tests (plan section 8.7: "an in-process test issuer that exercises the same validation pipeline"). It serves the OpenID Connect metadata and the signing keys over HTTP on a
/// loopback port, exactly where the API reads them in the Compose stack, and it signs tokens with keys that it made at run time. A test rotates the keys by publishing another set.
/// </summary>
internal sealed class TestIssuer : IAsyncDisposable
{
    private static readonly string[] SupportedAlgorithms = ["RS256", "ES256"];

    private readonly WebApplication _app;
    private readonly Lock _gate = new();
    private List<TestKey> _published;

    private TestIssuer(WebApplication app, string address, TestKey signing)
    {
        _app = app;
        Address = address;
        Signing = signing;
        _published = [signing];
    }

    public string Address { get; }

    public string Issuer => $"{Address}/realms/catalog";

    /// <summary>Gets or sets the issuer that the metadata names, when a test wants it to differ from the issuer of the tokens that the API accepts.</summary>
    public string? AdvertisedIssuer { get; set; }

    public string MetadataAddress => $"{Issuer}/.well-known/openid-configuration";

    public TestKey Signing { get; }

    public static async Task<TestIssuer> StartAsync()
    {
        var signing = TestKey.Rsa("key-1");
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        TestIssuer? issuer = null;

        app.MapGet("/realms/catalog/.well-known/openid-configuration", () => Results.Json(new Dictionary<string, object>
        {
            ["issuer"] = issuer!.AdvertisedIssuer ?? issuer.Issuer,
            ["jwks_uri"] = $"{issuer.Issuer}/protocol/openid-connect/certs",
            ["id_token_signing_alg_values_supported"] = SupportedAlgorithms,
        }));
        app.MapGet("/realms/catalog/protocol/openid-connect/certs", () => Results.Json(new Dictionary<string, object> { ["keys"] = issuer!.PublishedKeys() }));

        await app.StartAsync();

        var address = app.Urls.Single();
        issuer = new TestIssuer(app, address, signing);

        return issuer;
    }

    /// <summary>Replaces the set of keys that the metadata publishes. The key that signs a token need not be in it.</summary>
    public void Publish(params TestKey[] keys)
    {
        lock (_gate)
        {
            _published = [.. keys];
        }
    }

    /// <summary>
    /// Makes a token and signs it. The times are relative to the clock of the machine, because the token handler of Microsoft.IdentityModel reads the system clock and cannot be given another one. Every boundary
    /// that a test checks keeps a margin of a second on each side, which a test run never uses up.
    /// </summary>
    public string Token(TokenSpec? spec = null)
    {
        spec ??= new TokenSpec();
        var now = DateTime.UtcNow;
        var key = spec.Key ?? Signing;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = spec.Issuer ?? Issuer,
            Audience = spec.Audience,
            IssuedAt = now + spec.NotBeforeOffset,
            NotBefore = now + spec.NotBeforeOffset,
            Expires = spec.OmitExpiry ? null : now + spec.LifetimeFromNow,
            SigningCredentials = key.Credentials(spec.Algorithm),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = spec.Subject,
                ["azp"] = "catalog-cli",
                ["scope"] = string.Join(' ', spec.Scopes),
                ["realm_access"] = new Dictionary<string, object> { ["roles"] = spec.Roles },
            },
        };

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }

    /// <summary>Makes a token with a symmetric key, which is how an attacker confuses an algorithm: the public key serves as the secret of HS256.</summary>
    public string HmacToken(TokenSpec spec)
    {
        var now = DateTime.UtcNow;
        var secret = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Convert.ToBase64String(Signing.SecurityKey is RsaSecurityKey rsa ? rsa.Rsa.ExportRSAPublicKey() : [1])));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = spec.Audience,
            NotBefore = now - TimeSpan.FromMinutes(1),
            Expires = now + TimeSpan.FromMinutes(5),
            SigningCredentials = new SigningCredentials(secret, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object> { ["sub"] = spec.Subject, ["scope"] = string.Join(' ', spec.Scopes), ["realm_access"] = new Dictionary<string, object> { ["roles"] = spec.Roles } },
        });
    }

    /// <summary>Makes a token whose header says <c>alg: none</c> and whose signature is empty.</summary>
    public string UnsignedToken(TokenSpec spec)
    {
        var now = DateTimeOffset.UtcNow;
        var header = Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "none", typ = "JWT" }));
        var payload = Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["iss"] = Issuer,
            ["aud"] = spec.Audience,
            ["sub"] = spec.Subject,
            ["nbf"] = now.AddMinutes(-1).ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(5).ToUnixTimeSeconds(),
            ["scope"] = string.Join(' ', spec.Scopes),
            ["realm_access"] = new Dictionary<string, object> { ["roles"] = spec.Roles },
        }));

        return $"{header}.{payload}.";
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        Signing.Dispose();
    }

    private List<Dictionary<string, string>> PublishedKeys()
    {
        lock (_gate)
        {
            return [.. _published.Select(key => key.PublicJwk())];
        }
    }
}
