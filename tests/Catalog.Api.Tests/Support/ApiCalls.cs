using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Catalog.Api.Tests.Support;

/// <summary>What a test reads from an answer of the API: the status, the headers, and the body as text and as JSON.</summary>
/// <param name="Status">The status code.</param>
/// <param name="Headers">The headers of the response.</param>
/// <param name="ContentType">The media type of the body, or null when there is none.</param>
/// <param name="Body">The body as text.</param>
internal sealed record ApiResponse(HttpStatusCode Status, HttpResponseHeaders Headers, string? ContentType, string Body)
{
    public JsonElement Json => JsonDocument.Parse(Body).RootElement.Clone();

    /// <summary>Gets the <c>ETag</c> of the answer without its quotes, or null when it has none.</summary>
    public string? ETag => Headers.ETag?.Tag.Trim('"');

    public string? Header(string name) => Headers.TryGetValues(name, out var values) ? string.Join(", ", values) : null;

    public string Slug => Json.ProblemSlug();

    public Guid Id => Json.GetProperty("id").GetGuid();

    public string? Location => Headers.Location?.ToString();
}

/// <summary>Sends requests to a host with the headers of the contract. A test names the level of the caller, and the helper makes the token, so no test writes a bearer header by hand.</summary>
internal static class ApiCalls
{
    public static readonly string[] ReadScopes = ["catalog.read"];

    public static readonly string[] WriteScopes = ["catalog.read", "catalog.write"];

    public static readonly string[] AdminScopes = ["catalog.read", "catalog.write", "catalog.admin"];

    public static TokenSpec Reader => new() { Scopes = ReadScopes, Roles = ["catalog-reader"] };

    public static TokenSpec Editor => new() { Scopes = WriteScopes, Roles = ["catalog-editor"] };

    public static TokenSpec Admin => new() { Scopes = AdminScopes, Roles = ["catalog-admin"] };

    /// <summary>Sends one request and reads the whole answer.</summary>
    /// <param name="client">The client of the host.</param>
    /// <param name="issuer">The issuer that signs the token.</param>
    /// <param name="method">The method.</param>
    /// <param name="path">The path and the query.</param>
    /// <param name="caller">What the token says, or null to send no token.</param>
    /// <param name="json">The body as JSON text, or null for no body.</param>
    /// <param name="configure">A change to the request, such as a header.</param>
    public static async Task<ApiResponse> SendAsync(this HttpClient client, TestIssuer issuer, HttpMethod method, string path, TokenSpec? caller, string? json = null, Action<HttpRequestMessage>? configure = null)
    {
        using var request = new HttpRequestMessage(method, path);

        if (caller is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", issuer.Token(caller));
        }

        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        configure?.Invoke(request);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        return new ApiResponse(response.StatusCode, response.Headers, response.Content.Headers.ContentType?.MediaType, body);
    }

    /// <summary>Quotes a token as the <c>ETag</c> and <c>If-Match</c> headers write it.</summary>
    public static string Quote(string token) => $"\"{token}\"";

    /// <summary>Makes a new <c>Idempotency-Key</c> value in the form that the contract gives it.</summary>
    public static string NewKey() => Quote($"key-{Guid.NewGuid():N}");

    public static Action<HttpRequestMessage> WithKey(string key) => request => request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

    public static Action<HttpRequestMessage> WithIfMatch(string value) => request => request.Headers.TryAddWithoutValidation("If-Match", value);

    public static Action<HttpRequestMessage> WithIfNoneMatch(string value) => request => request.Headers.TryAddWithoutValidation("If-None-Match", value);

    /// <summary>Makes a new trace identifier as 32 lower-case hexadecimal characters.</summary>
    public static string NewTraceId() => System.Diagnostics.ActivityTraceId.CreateRandom().ToHexString();

    /// <summary>Sends the request as a part of a trace that the test started, so the spans of the host join the trace and the test finds them by its identifier, whichever other test runs at the same time.</summary>
    public static Action<HttpRequestMessage> WithTrace(string traceId) => request => request.Headers.TryAddWithoutValidation("traceparent", $"00-{traceId}-0123456789abcdef-01");
    public static Action<HttpRequestMessage> WithBoth(params Action<HttpRequestMessage>[] changes) => request =>
    {
        foreach (var change in changes)
        {
            change(request);
        }
    };

    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
