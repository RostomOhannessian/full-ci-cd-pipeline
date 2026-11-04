using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Catalog.Api.Http;

/// <summary>Writes the successful responses of the API with the headers that the contract names: <c>ETag</c>, <c>Location</c>, and <c>Link</c>.</summary>
internal static class Responses
{
    /// <summary>Returns a JSON response, with the <c>ETag</c> and the <c>Location</c> when they are given.</summary>
    /// <typeparam name="T">The type of the body.</typeparam>
    /// <param name="http">The context of the request.</param>
    /// <param name="status">The status code.</param>
    /// <param name="body">The body.</param>
    /// <param name="typeInfo">The metadata of the body.</param>
    /// <param name="tag">The token of the version of the resource, or null for a response that is not one resource.</param>
    /// <param name="location">The address of a created resource, or null.</param>
    public static IResult Json<T>(HttpContext http, int status, T body, JsonTypeInfo<T> typeInfo, string? tag = null, string? location = null)
    {
        ArgumentNullException.ThrowIfNull(http);

        if (tag is not null)
        {
            http.Response.Headers[HeaderNames.ETag] = Quote(tag);
        }

        if (location is not null)
        {
            http.Response.Headers[HeaderNames.Location] = location;
        }

        return TypedResults.Json(body, typeInfo, statusCode: status);
    }

    /// <summary>Returns <c>304</c> with the <c>ETag</c> and no body.</summary>
    /// <param name="http">The context of the request.</param>
    /// <param name="tag">The token of the version that the client already holds.</param>
    public static IResult NotModified(HttpContext http, string tag)
    {
        ArgumentNullException.ThrowIfNull(http);

        http.Response.Headers[HeaderNames.ETag] = Quote(tag);

        return TypedResults.StatusCode(StatusCodes.Status304NotModified);
    }

    /// <summary>Returns the absolute address of one resource of a collection, in the version of the request.</summary>
    /// <param name="request">The request.</param>
    /// <param name="collection">The name of the collection, such as <c>products</c>.</param>
    /// <param name="id">The identifier of the resource.</param>
    public static string LocationOf(HttpRequest request, string collection, Guid id)
    {
        ArgumentNullException.ThrowIfNull(request);

        var version = request.RouteValues["version"] as string ?? "1";

        return $"{request.Scheme}://{request.Host}{request.PathBase}/api/v{version}/{collection}/{id:D}";
    }

    /// <summary>Adds the <c>Link</c> header with <c>rel="next"</c> when a next page exists. It keeps every parameter of the request and replaces the cursor.</summary>
    /// <param name="http">The context of the request.</param>
    /// <param name="nextCursor">The cursor of the next page, or null on the last page.</param>
    public static void AddNextLink(HttpContext http, string? nextCursor)
    {
        ArgumentNullException.ThrowIfNull(http);

        if (nextCursor is null)
        {
            return;
        }

        var request = http.Request;
        var parameters = request.Query
            .Where(pair => !string.Equals(pair.Key, "cursor", StringComparison.Ordinal))
            .SelectMany(pair => pair.Value.Select(value => new KeyValuePair<string, string?>(pair.Key, value)))
            .Append(new KeyValuePair<string, string?>("cursor", nextCursor));
        var address = QueryHelpers.AddQueryString($"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}", parameters);

        http.Response.Headers[HeaderNames.Link] = $"<{address}>; rel=\"next\"";
    }

    private static string Quote(string tag) => $"\"{tag}\"";
}
