using Catalog.Api.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Catalog.Api.Http;

/// <summary>
/// Adds the security headers to every response, including an error and a response from another middleware (plan section 9, ADR-0040). The API serves JSON to programs, so the policy is the strictest one: no content may be
/// loaded or framed, the type of the content is fixed, the response is never stored by a shared cache, and the referrer is never sent. Transport security is added when the request came over HTTPS, which the forwarded
/// headers establish when a trusted proxy ends the TLS. A header that an endpoint already set is kept, so a viewer in the Development environment can set the policy that it needs.
/// </summary>
/// <param name="next">The next middleware.</param>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string StrictTransportSecurity = "max-age=31536000; includeSubDomains";

    /// <summary>Registers the headers to be written when the response starts, then runs the request.</summary>
    /// <param name="context">The context of the request.</param>
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.OnStarting(static state =>
        {
            var http = (HttpContext)state;
            var headers = http.Response.Headers;

            headers.TryAdd("X-Content-Type-Options", "nosniff");
            headers.TryAdd("X-Frame-Options", "DENY");
            headers.TryAdd("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'");
            headers.TryAdd("Referrer-Policy", "no-referrer");
            headers.TryAdd("Permissions-Policy", "geolocation=(), camera=(), microphone=()");
            headers.TryAdd("Cross-Origin-Resource-Policy", "same-origin");
            headers.TryAdd(HeaderNames.CacheControl, "no-store");

            if (http.Request.IsHttps)
            {
                headers.TryAdd(HeaderNames.StrictTransportSecurity, StrictTransportSecurity);
            }

            return Task.CompletedTask;
        }, context);

        return next(context);
    }
}

/// <summary>Marks an endpoint as one that only the management port serves (plan section 8.9, ADR-0040).</summary>
internal sealed class ManagementEndpointMetadata
{
    /// <summary>Gets the one instance, which carries no state.</summary>
    public static ManagementEndpointMetadata Instance { get; } = new();
}

/// <summary>
/// Keeps the two ports apart (plan sections 8.9 and 8.12, ADR-0040). A request on the management port reaches only the endpoints that are marked for it, and a request on the public port never reaches them, so the
/// health endpoints cannot leak through the gateway and the API cannot be called through the address of a probe. The check reads the port that the connection arrived on, and not a header that a caller can write.
/// A request on the public port must also name an allowed host, because the API builds the <c>Location</c> and <c>Link</c> headers from it. The management port does not check the host, because a probe names the address of the pod.
/// </summary>
/// <param name="next">The next middleware.</param>
/// <param name="options">The settings of the listeners.</param>
internal sealed class PortGateMiddleware(RequestDelegate next, IOptions<HostingOptions> options)
{
    private readonly HostingOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <summary>Refuses a request that is on the wrong port or names a host that is not allowed.</summary>
    /// <param name="context">The context of the request.</param>
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var onManagementPort = context.Connection.LocalPort == _options.ManagementPort;
        var managementEndpoint = context.GetEndpoint()?.Metadata.GetMetadata<ManagementEndpointMetadata>() is not null;

        if (onManagementPort != managementEndpoint)
        {
            context.SetEndpoint(null);
            context.Response.StatusCode = StatusCodes.Status404NotFound;

            return Task.CompletedTask;
        }

        if (!onManagementPort && !HostAllowed(context.Request.Host.Host))
        {
            return Problems.Of(ApiProblems.MalformedRequest, "The Host header names a host that this API does not serve.").ExecuteAsync(context);
        }

        return next(context);
    }

    private bool HostAllowed(string host)
    {
        foreach (var allowed in _options.AllowedHostList)
        {
            if (allowed == "*"
                || string.Equals(allowed, host, StringComparison.OrdinalIgnoreCase)
                || (allowed.StartsWith("*.", StringComparison.Ordinal) && host.EndsWith(allowed[1..], StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}
