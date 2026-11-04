using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Catalog.Application.Results;
using Catalog.Contracts.V1;

namespace Catalog.Api.Http;

/// <summary>The serializer context of the error bodies. The names are camel case and a missing value is left out, as the contract writes them.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(ValidationProblemDetails))]
internal sealed partial class ProblemJsonContext : JsonSerializerContext;

/// <summary>
/// An error response in the shape of RFC 9457 (ADR-0028). It adds what only the API knows: the address of the entry, the path of the request, and the trace identifier, and it writes <c>application/problem+json</c>.
/// Every error of the API, from a refused request to an unhandled exception, goes through this one writer, so a client reads one shape.
/// </summary>
/// <param name="type">The entry of the error catalog.</param>
/// <param name="detail">An explanation of this occurrence. It never holds a stack trace, a query, a secret, or a value that the caller sent.</param>
/// <param name="errors">The messages for each invalid field, or an empty list.</param>
/// <param name="headers">Headers to add to the response, such as <c>Retry-After</c>.</param>
internal sealed class ProblemResult(ProblemType type, string detail, IReadOnlyList<FieldError> errors, IReadOnlyDictionary<string, string>? headers = null) : IResult
{
    /// <summary>The media type of an error body.</summary>
    public const string MediaType = "application/problem+json";

    /// <inheritdoc />
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var response = httpContext.Response;

        if (response.HasStarted)
        {
            return;
        }

        response.StatusCode = type.Status;
        response.ContentType = MediaType;

        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            response.Headers[name] = value;
        }

        var address = ApiProblems.AddressOf(type);
        var instance = httpContext.Request.PathBase.Add(httpContext.Request.Path).ToUriComponent();
        var traceId = RequestTrace.IdOf();

        if (errors.Count > 0)
        {
            var grouped = errors.GroupBy(error => error.Field).ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray());
            await JsonSerializer.SerializeAsync(response.Body, new ValidationProblemDetails(address, type.Title, type.Status, detail, instance, traceId, grouped), ProblemJsonContext.Default.ValidationProblemDetails, httpContext.RequestAborted);

            return;
        }

        await JsonSerializer.SerializeAsync(response.Body, new ProblemDetails(address, type.Title, type.Status, detail, instance, traceId), ProblemJsonContext.Default.ProblemDetails, httpContext.RequestAborted);
    }
}

/// <summary>The factory of error results, so an endpoint, a filter, and a handler of unhandled exceptions all make the same ones.</summary>
internal static class Problems
{
    /// <summary>The time in seconds that a 503 asks a client to wait.</summary>
    public const int UnavailableRetryAfterSeconds = 5;

    /// <summary>Makes the result of an entry of the catalog.</summary>
    /// <param name="type">The entry.</param>
    /// <param name="detail">The explanation of this occurrence.</param>
    /// <param name="headers">Headers to add to the response.</param>
    public static ProblemResult Of(ProblemType type, string detail, IReadOnlyDictionary<string, string>? headers = null) => new(type, detail, [], headers);

    /// <summary>Makes a <c>validation-failed</c> result for one or more fields, parameters, or headers.</summary>
    /// <param name="errors">The messages.</param>
    public static ProblemResult Validation(params FieldError[] errors) =>
        new(ProblemMapping.ValidationFailed, "One or more fields are not valid. The errors member lists each one.", errors);

    /// <summary>Makes the result of an error that a use case returned.</summary>
    /// <param name="error">The refusal.</param>
    public static ProblemResult For(ApplicationError error)
    {
        var problem = ProblemMapping.Map(error);

        return new ProblemResult(problem.Type, problem.Detail, problem.Errors);
    }

    /// <summary>Makes a <c>service-unavailable</c> result, which asks the client to retry.</summary>
    public static ProblemResult Unavailable() =>
        Of(ApiProblems.ServiceUnavailable, "A dependency of the API cannot be reached. Retry after the time in Retry-After.", new Dictionary<string, string> { ["Retry-After"] = UnavailableRetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) });
}

/// <summary>The W3C trace identifier of the request, which every error carries so a person can find the request in the traces and the logs.</summary>
internal static class RequestTrace
{
    /// <summary>Returns the trace identifier of the current request as 32 lower-case hexadecimal characters.</summary>
    public static string IdOf() => Activity.Current is { } activity && activity.TraceId != default ? activity.TraceId.ToHexString() : ActivityTraceId.CreateRandom().ToHexString();
}

/// <summary>
/// Makes sure that every request has an activity, so the trace identifier of an error is the one of the request even when no tracing listener is attached. The observability work (WP1.11) adds the listeners, and
/// the activity that the host creates then takes the place of this one.
/// </summary>
/// <param name="next">The next middleware.</param>
internal sealed class RequestTraceMiddleware(RequestDelegate next)
{
    /// <summary>Runs the request inside an activity.</summary>
    /// <param name="context">The context of the request.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        if (Activity.Current is not null)
        {
            await next(context);

            return;
        }

        using var activity = new Activity("Catalog.Api.Request");
        activity.Start();

        try
        {
            await next(context);
        }
        finally
        {
            activity.Stop();
        }
    }
}
