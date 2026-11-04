using Catalog.Api.Configuration;
using Catalog.Api.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Catalog.Api.Security;

/// <summary>
/// Configures the bearer handler from <see cref="AuthOptions"/> (plan section 8.7, REQ-IDN-003, ADR-0040). The token must name the exact issuer and the exact audience, carry a signature that one of the keys of the
/// metadata verifies, use RS256 or ES256, and be inside its lifetime with at most 30 seconds of skew. The algorithms are written here and not in a setting, so no configuration can widen them. The keys come from the
/// metadata of the issuer, which the handler reads again when it meets a key that it does not know, so a rotation of the signing key needs no restart. The handler keeps the claim names of the token as they are, and it puts
/// nothing of the failure in the answer, because the description of an invalid token tells an attacker what to fix.
/// </summary>
/// <param name="auth">The settings of the token validation.</param>
internal sealed class ConfigureJwtBearer(IOptions<AuthOptions> auth) : IConfigureNamedOptions<JwtBearerOptions>, IPostConfigureOptions<JwtBearerOptions>
{
    /// <summary>The algorithms that a token may use (plan section 8.7).</summary>
    public static readonly IReadOnlyList<string> AllowedAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.EcdsaSha256];

    /// <summary>The realm that the challenge names.</summary>
    public const string Realm = "catalog";

    private readonly AuthOptions _auth = auth?.Value ?? throw new ArgumentNullException(nameof(auth));

    /// <inheritdoc />
    public void Configure(string? name, JwtBearerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (name != JwtBearerDefaults.AuthenticationScheme)
        {
            return;
        }

        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = _auth.RequireHttpsMetadata;
        options.MetadataAddress = _auth.EffectiveMetadataAddress;
        options.RefreshInterval = _auth.MetadataRefreshInterval;
        options.SaveToken = false;
        options.IncludeErrorDetails = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _auth.Issuer,
            IssuerValidator = ValidateIssuer,
            ValidateAudience = true,
            ValidAudience = _auth.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = _auth.ClockSkew,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            ValidAlgorithms = AllowedAlgorithms,
        };
        options.Events = new JwtBearerEvents { OnChallenge = ChallengeAsync };
    }

    /// <inheritdoc />
    public void Configure(JwtBearerOptions options) => Configure(JwtBearerDefaults.AuthenticationScheme, options);

    /// <summary>
    /// Runs after the handler built its manager of the metadata. Microsoft.IdentityModel keeps the previous key set for an hour after a refresh and validates a token that fails with the new set against the old one (the
    /// last known good configuration). That would keep a key that the issuer withdrew working for an hour, so the API turns it off: the keys that the issuer publishes now are the keys that count. A rotation that keeps
    /// the old key published for a while, which is how Keycloak rotates, never needs the old set.
    /// </summary>
    /// <param name="name">The name of the scheme.</param>
    /// <param name="options">The options.</param>
    public void PostConfigure(string? name, JwtBearerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (name == JwtBearerDefaults.AuthenticationScheme && options.ConfigurationManager is BaseConfigurationManager manager)
        {
            manager.UseLastKnownGoodConfiguration = false;
        }
    }

    // The handler adds the issuer of the metadata to the valid issuers, which would accept a token of an issuer that the settings do not name when the identity provider advertises another one. The comparison is exact and
    // ordinal, so the issuer is the one string that the settings hold.
    private string ValidateIssuer(string issuer, SecurityToken token, TokenValidationParameters parameters) =>
        string.Equals(issuer, _auth.Issuer, StringComparison.Ordinal)
            ? issuer
            : throw new SecurityTokenInvalidIssuerException("The issuer of the token is not the issuer of this API.") { InvalidIssuer = issuer };

    // The challenge names the realm, and it names the error only when a token was sent and was refused (RFC 6750). The body is the 401 of the error catalog.
    private static Task ChallengeAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();

        var challenge = context.AuthenticateFailure is null ? $"Bearer realm=\"{Realm}\"" : $"Bearer realm=\"{Realm}\", error=\"invalid_token\"";
        context.Response.Headers.WWWAuthenticate = challenge;

        return Problems.Of(ApiProblems.Unauthorized, "Send a valid bearer token in the Authorization header.").ExecuteAsync(context.HttpContext);
    }
}
