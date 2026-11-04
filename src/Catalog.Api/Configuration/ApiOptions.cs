using System.Net;
using Microsoft.Extensions.Options;

namespace Catalog.Api.Configuration;

/// <summary>
/// Where the API listens (plan sections 8.9 and 8.12, ADR-0040). The public port serves the version 1 contract, and the management port serves the health endpoints and nothing else. The two are separate sockets, so
/// the gateway can route the first and never the second, and a probe never competes with a client for a connection. The type prints nothing but its name.
/// </summary>
public sealed class HostingOptions
{
    /// <summary>The name of the configuration section that the composition root binds to this type.</summary>
    public const string SectionName = "Catalog:Hosting";

    /// <summary>Gets or sets the address that both ports bind to. The default is the loopback address, so a developer machine never serves the network. A container sets <c>0.0.0.0</c>.</summary>
    public string ListenAddress { get; set; } = "127.0.0.1";

    /// <summary>Gets or sets the port of the public API. Default 8080.</summary>
    public int PublicPort { get; set; } = 8080;

    /// <summary>Gets or sets the port of the health endpoints. The gateway never routes to it. Default 8081.</summary>
    public int ManagementPort { get; set; } = 8081;

    /// <summary>
    /// Gets or sets the host names that a request to the public port may name in its <c>Host</c> header, separated by semicolons, as the <c>AllowedHosts</c> setting of ASP.NET Core is written. The API builds the <c>Location</c>
    /// and <c>Link</c> headers from the host, so an unchecked host would let a caller choose the address in them. A name is exact (<c>api.example.test</c>), or it starts with <c>*.</c> to match every subdomain. The
    /// management port does not check the host, because a probe names the address of the pod. The list is one string, so a setting from the environment replaces the whole list and never merges with the file.
    /// </summary>
    public string AllowedHosts { get; set; } = "localhost;127.0.0.1;[::1]";

    /// <summary>Gets the allowed host names as a list, without the empty entries that a trailing semicolon leaves.</summary>
    public IReadOnlyList<string> AllowedHostList => ListSetting.Split(AllowedHosts);

    /// <inheritdoc />
    public override string ToString() => nameof(HostingOptions);
}

/// <summary>Reads a setting that holds a list as one string, with the entries separated by semicolons.</summary>
internal static class ListSetting
{
    /// <summary>Splits the text into trimmed, non-empty entries.</summary>
    /// <param name="value">The text, which may be null.</param>
    public static string[] Split(string? value) => value?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
}

/// <summary>Validates <see cref="HostingOptions"/> when the host starts. A problem names a setting and never repeats a value.</summary>
/// <param name="environment">The environment of the host.</param>
internal sealed class HostingOptionsValidator(IHostEnvironment environment) : IValidateOptions<HostingOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, HostingOptions options)
    {
        List<string> problems = [];

        if (!IPAddress.TryParse(options.ListenAddress, out _))
        {
            problems.Add("ListenAddress must be an IP address.");
        }

        if (options.PublicPort is < 1 or > 65535 || options.ManagementPort is < 1 or > 65535)
        {
            problems.Add("PublicPort and ManagementPort must be between 1 and 65535.");
        }
        else if (options.PublicPort == options.ManagementPort)
        {
            problems.Add("PublicPort and ManagementPort must differ, because the management port is never routed publicly.");
        }

        var hosts = options.AllowedHostList;

        if (hosts.Count == 0)
        {
            problems.Add("AllowedHosts must name at least one host. Separate several hosts with semicolons.");
        }
        else if (hosts.Any(host => host == "*") && !environment.IsDevelopment())
        {
            problems.Add("AllowedHosts must not allow every host outside the Development environment.");
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}

/// <summary>
/// How the API validates an access token (plan section 8.7, ADR-0040). The issuer is exact, the audience is exact, and the algorithms are fixed to RS256 and ES256 in code, because a setting that widened them would
/// be the way back to a weaker check. The type prints nothing but its name.
/// </summary>
public sealed class AuthOptions
{
    /// <summary>The name of the configuration section that the composition root binds to this type.</summary>
    public const string SectionName = "Catalog:Auth";

    /// <summary>The largest clock skew that the API accepts, in seconds (plan section 8.7).</summary>
    public const int MaxClockSkewSeconds = 30;

    /// <summary>Gets or sets the issuer that a token must name in <c>iss</c>, compared exactly. Keycloak advertises one fixed external issuer (plan section 8.7).</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Gets or sets the address of the OpenID Connect metadata. It is empty by default, and the API then reads <c>{Issuer}/.well-known/openid-configuration</c>. Set it when the API reaches the identity provider at an internal address while tokens name the external one.</summary>
    public string MetadataAddress { get; set; } = string.Empty;

    /// <summary>Gets or sets the audience that a token must name in <c>aud</c>. Default <c>catalog-api</c>.</summary>
    public string Audience { get; set; } = "catalog-api";

    /// <summary>Gets or sets whether the metadata must come over HTTPS. Default true, and only the Development environment may turn it off, for the plain-HTTP issuer of the Compose stack.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Gets or sets the clock skew that the lifetime check tolerates. Default 30 seconds, which is also the largest value.</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(MaxClockSkewSeconds);

    /// <summary>Gets or sets the shortest time between two reads of the metadata, which bounds how fast the API learns a rotated signing key. Default 5 minutes.</summary>
    public TimeSpan MetadataRefreshInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets the address that the API reads the metadata from.</summary>
    public string EffectiveMetadataAddress => string.IsNullOrWhiteSpace(MetadataAddress) ? $"{Issuer.TrimEnd('/')}/.well-known/openid-configuration" : MetadataAddress;

    /// <inheritdoc />
    public override string ToString() => nameof(AuthOptions);
}

/// <summary>Validates <see cref="AuthOptions"/> when the host starts. A problem names a setting and never repeats a value.</summary>
/// <param name="environment">The environment of the host.</param>
internal sealed class AuthOptionsValidator(IHostEnvironment environment) : IValidateOptions<AuthOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        List<string> problems = [];

        if (!Uri.TryCreate(options.Issuer, UriKind.Absolute, out var issuer) || issuer.Scheme is not ("https" or "http"))
        {
            problems.Add("Issuer must be an absolute http or https URL.");
        }

        if (!string.IsNullOrWhiteSpace(options.MetadataAddress) && (!Uri.TryCreate(options.MetadataAddress, UriKind.Absolute, out var metadata) || metadata.Scheme is not ("https" or "http")))
        {
            problems.Add("MetadataAddress must be empty or an absolute http or https URL.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            problems.Add("Audience is required.");
        }

        if (options.RequireHttpsMetadata)
        {
            if (issuer is not null && issuer.Scheme != "https")
            {
                problems.Add("Issuer must use https while RequireHttpsMetadata is on.");
            }

            if (Uri.TryCreate(options.MetadataAddress, UriKind.Absolute, out var address) && address.Scheme != "https")
            {
                problems.Add("MetadataAddress must use https while RequireHttpsMetadata is on.");
            }
        }
        else if (!environment.IsDevelopment())
        {
            problems.Add("RequireHttpsMetadata may be off only in the Development environment.");
        }

        if (options.ClockSkew < TimeSpan.Zero || options.ClockSkew > TimeSpan.FromSeconds(AuthOptions.MaxClockSkewSeconds))
        {
            problems.Add($"ClockSkew must be between zero and {AuthOptions.MaxClockSkewSeconds} seconds.");
        }

        if (options.MetadataRefreshInterval < TimeSpan.FromSeconds(1) || options.MetadataRefreshInterval > TimeSpan.FromHours(24))
        {
            problems.Add("MetadataRefreshInterval must be between 1 second and 24 hours.");
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}

/// <summary>
/// The limits on a request (plan sections 8.6 and 9, ADR-0040). A body has a size and a depth, and a caller has a number of requests for each window. A write has a lower limit than a read, because it costs a transaction.
/// The type prints nothing but its name.
/// </summary>
public sealed class LimitsOptions
{
    /// <summary>The name of the configuration section that the composition root binds to this type.</summary>
    public const string SectionName = "Catalog:Limits";

    /// <summary>Gets or sets the largest request body in bytes. The largest valid body is a product of a few hundred bytes plus a description of 2000 characters. Default 32 KiB.</summary>
    public int MaxRequestBodyBytes { get; set; } = 32 * 1024;

    /// <summary>Gets or sets the deepest nesting of a JSON body. The deepest valid body has two levels. Default 8.</summary>
    public int MaxJsonDepth { get; set; } = 8;

    /// <summary>Gets or sets the number of read requests that one caller may send in a window. Default 600.</summary>
    public int ReadPermits { get; set; } = 600;

    /// <summary>Gets or sets the number of write requests that one caller may send in a window. Default 120.</summary>
    public int WritePermits { get; set; } = 120;

    /// <summary>Gets or sets the length of the window. Default 1 minute.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public override string ToString() => nameof(LimitsOptions);
}

/// <summary>Validates <see cref="LimitsOptions"/> when the host starts. A problem names a setting and never repeats a value.</summary>
internal sealed class LimitsOptionsValidator : IValidateOptions<LimitsOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, LimitsOptions options)
    {
        List<string> problems = [];

        if (options.MaxRequestBodyBytes is < 1024 or > 1024 * 1024)
        {
            problems.Add("MaxRequestBodyBytes must be between 1 KiB and 1 MiB.");
        }

        if (options.MaxJsonDepth is < 2 or > 64)
        {
            problems.Add("MaxJsonDepth must be between 2 and 64.");
        }

        if (options.ReadPermits is < 1 or > 1_000_000 || options.WritePermits is < 1 or > 1_000_000)
        {
            problems.Add("ReadPermits and WritePermits must be between 1 and 1,000,000.");
        }

        if (options.Window < TimeSpan.FromSeconds(1) || options.Window > TimeSpan.FromHours(1))
        {
            problems.Add("Window must be between 1 second and 1 hour.");
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}

/// <summary>
/// Which proxies the API trusts to tell it the address and the scheme of the caller (plan section 9, ADR-0040). An empty list trusts none, so a client can never spoof <c>X-Forwarded-For</c> by itself. The default
/// of ASP.NET Core trusts the loopback address, and the composition root clears it, so only the networks and the addresses named here are trusted. The type prints nothing but its name.
/// </summary>
public sealed class ForwardedHeadersSettings
{
    /// <summary>The name of the configuration section that the composition root binds to this type.</summary>
    public const string SectionName = "Catalog:ForwardedHeaders";

    /// <summary>Gets or sets the networks of the trusted proxies in CIDR notation, such as <c>10.0.0.0/8</c>, separated by semicolons. Default: none. The list is one string, so a setting from the environment replaces the whole list.</summary>
    public string KnownNetworks { get; set; } = string.Empty;

    /// <summary>Gets or sets the addresses of single trusted proxies, separated by semicolons. Default: none.</summary>
    public string KnownProxies { get; set; } = string.Empty;

    /// <summary>Gets the trusted networks as a list.</summary>
    public IReadOnlyList<string> KnownNetworkList => ListSetting.Split(KnownNetworks);

    /// <summary>Gets the trusted proxies as a list.</summary>
    public IReadOnlyList<string> KnownProxyList => ListSetting.Split(KnownProxies);

    /// <summary>Gets or sets the number of proxies whose entry the API reads from the chain. Default 1.</summary>
    public int ForwardLimit { get; set; } = 1;

    /// <inheritdoc />
    public override string ToString() => nameof(ForwardedHeadersSettings);
}

/// <summary>Validates <see cref="ForwardedHeadersSettings"/> when the host starts. A problem names a setting and never repeats a value.</summary>
internal sealed class ForwardedHeadersSettingsValidator : IValidateOptions<ForwardedHeadersSettings>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ForwardedHeadersSettings options)
    {
        List<string> problems = [];

        foreach (var network in options.KnownNetworkList)
        {
            if (!IPNetwork.TryParse(network, out var parsed))
            {
                problems.Add("Every entry of KnownNetworks must be an address with a prefix length, such as 10.0.0.0/8.");
            }
            else if (parsed.PrefixLength == 0)
            {
                problems.Add("KnownNetworks must not trust every address (a prefix length of 0).");
            }
        }

        foreach (var proxy in options.KnownProxyList)
        {
            if (!IPAddress.TryParse(proxy, out var address))
            {
                problems.Add("Every entry of KnownProxies must be an IP address.");
            }
            else if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            {
                problems.Add("KnownProxies must not contain the unspecified address.");
            }
        }

        if (options.ForwardLimit is < 1 or > 5)
        {
            problems.Add("ForwardLimit must be between 1 and 5.");
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
