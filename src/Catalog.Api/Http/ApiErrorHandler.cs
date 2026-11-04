using System.Data.Common;
using Catalog.Application.Results;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

namespace Catalog.Api.Http;

/// <summary>
/// Turns every failure that no endpoint answered into an error of the catalog (plan section 8.6, ADR-0028, ADR-0040): an exception that escaped a request, and a response that has a status of 400 or more and no body. The
/// text of an exception never reaches the caller, and the log holds it with the trace identifier that the caller sees in the answer.
/// </summary>
/// <param name="logger">The logger.</param>
internal sealed partial class ApiErrorHandler(ILogger<ApiErrorHandler> logger)
{
    private readonly ILogger<ApiErrorHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Answers a request that threw. It is the handler of the exception middleware.</summary>
    /// <param name="context">The context of the request.</param>
    public Task HandleExceptionAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;

        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            LogAborted();

            return Task.CompletedTask;
        }

        return Classify(exception) switch
        {
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } => Problems.Of(ApiProblems.PayloadTooLarge, "The request body is larger than the limit of this API.").ExecuteAsync(context),
            BadHttpRequestException { StatusCode: StatusCodes.Status415UnsupportedMediaType } => Problems.Of(ApiProblems.UnsupportedMediaType, "Send the header Content-Type: application/json.").ExecuteAsync(context),
            BadHttpRequestException => Problems.Of(ApiProblems.MalformedRequest, "The request could not be read.").ExecuteAsync(context),
            DbException unavailable => Unavailable(context, unavailable),
            _ => Fault(context, exception),
        };
    }

    /// <summary>Writes the body of a response that has an error status and no body. It is the handler of the status code pages.</summary>
    /// <param name="context">The context of the request.</param>
    public static Task WriteStatusAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var status = context.Response.StatusCode;

        return status switch
        {
            StatusCodes.Status401Unauthorized => Unauthorized(context),
            StatusCodes.Status403Forbidden => Problems.Of(ProblemMapping.Forbidden, "The token does not allow this operation.").ExecuteAsync(context),
            StatusCodes.Status404NotFound => Problems.Of(ProblemMapping.NotFound, "No resource is served at this address.").ExecuteAsync(context),
            StatusCodes.Status413PayloadTooLarge => Problems.Of(ApiProblems.PayloadTooLarge, "The request body is larger than the limit of this API.").ExecuteAsync(context),
            StatusCodes.Status415UnsupportedMediaType => Problems.Of(ApiProblems.UnsupportedMediaType, "Send the header Content-Type: application/json.").ExecuteAsync(context),
            StatusCodes.Status429TooManyRequests => RateLimited(context),
            StatusCodes.Status503ServiceUnavailable => Problems.Unavailable().ExecuteAsync(context),
            >= 500 => Problems.Of(ApiProblems.InternalError, "The server could not answer the request. Report the traceId.").ExecuteAsync(context),
            _ => Problems.Of(ApiProblems.Other(status), "The request cannot be served.").ExecuteAsync(context),
        };
    }

    /// <summary>Writes the answer to a request that the rate limiter refused, with the time to wait.</summary>
    /// <param name="context">The context of the request.</param>
    /// <param name="retryAfter">How long the caller waits, or null when the limiter gave no time.</param>
    public static Task RateLimited(HttpContext context, TimeSpan? retryAfter = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var seconds = Math.Max(1, (int)Math.Ceiling((retryAfter ?? TimeSpan.FromSeconds(1)).TotalSeconds));

        return Problems.Of(
            ApiProblems.RateLimited,
            "The caller sent more requests than the limit allows. Wait for the time in Retry-After, then continue at a lower rate.",
            new Dictionary<string, string> { [HeaderNames.RetryAfter] = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) }).ExecuteAsync(context);
    }

    // The status codes that an exception of the database maps to are found anywhere in the chain, because EF Core wraps the exception of the driver.
    private static Exception? Classify(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException or BadHttpRequestException)
            {
                return current;
            }
        }

        return exception;
    }

    private static Task Unauthorized(HttpContext context)
    {
        context.Response.Headers.WWWAuthenticate = $"Bearer realm=\"{Security.ConfigureJwtBearer.Realm}\"";

        return Problems.Of(ApiProblems.Unauthorized, "Send a valid bearer token in the Authorization header.").ExecuteAsync(context);
    }

    private Task Unavailable(HttpContext context, Exception exception)
    {
        LogUnavailable(exception.GetType().Name, RequestTrace.IdOf());

        return Problems.Unavailable().ExecuteAsync(context);
    }

    private Task Fault(HttpContext context, Exception? exception)
    {
        LogFault(exception, RequestTrace.IdOf());

        return Problems.Of(ApiProblems.InternalError, "The server failed while it handled the request. Report the traceId.").ExecuteAsync(context);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "An unhandled exception ended a request. TraceId {TraceId}.")]
    private partial void LogFault(Exception? exception, string traceId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "A dependency of the API could not be reached ({ExceptionType}). TraceId {TraceId}.")]
    private partial void LogUnavailable(string exceptionType, string traceId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "The client ended the request before the answer.")]
    private partial void LogAborted();
}

/// <summary>The settings of the rate limiter, which answers a refused request with the error of the catalog.</summary>
internal static class RateLimitRejection
{
    /// <summary>Writes the 429 answer for a request that the limiter refused.</summary>
    /// <param name="context">The context of the rejection.</param>
    public static ValueTask WriteAsync(OnRejectedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        TimeSpan? retryAfter = context.Lease.TryGetMetadata(System.Threading.RateLimiting.MetadataName.RetryAfter, out var value) ? value : null;

        return new ValueTask(ApiErrorHandler.RateLimited(context.HttpContext, retryAfter));
    }
}
