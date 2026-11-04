using System.Buffers;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Catalog.Api.Configuration;
using Catalog.Application.Results;

namespace Catalog.Api.Http;

/// <summary>
/// Reads a request body as strict, bounded JSON (plan section 8.6, REQ-API-006). The body must be <c>application/json</c> and no larger than the limit, and its nesting must not be deeper than the limit. The reader
/// stops reading at the limit, so a body of any size costs the server no more than the limit, whatever the host enforces. A member that the contract does not name is a validation error that names its path, and a
/// value of the wrong type is one too. The members that the contract names come from the metadata of the serializer, so no list of them is kept here.
/// </summary>
internal static class JsonBody
{
    private const int ReadChunkBytes = 4096;

    /// <summary>Reads the body of the request into a transport record.</summary>
    /// <typeparam name="T">The type of the record.</typeparam>
    /// <param name="request">The request.</param>
    /// <param name="typeInfo">The metadata of the record.</param>
    /// <param name="limits">The limits of the API.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    public static async Task<Outcome<T>> ReadAsync<T>(HttpRequest request, JsonTypeInfo<T> typeInfo, LimitsOptions limits, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(limits);

        if (!IsJson(request.ContentType))
        {
            return Outcome.Fail<T>(Problems.Of(ApiProblems.UnsupportedMediaType, "Send the header Content-Type: application/json."));
        }

        if (request.ContentLength > limits.MaxRequestBodyBytes)
        {
            return Outcome.Fail<T>(TooLarge());
        }

        var bytes = await ReadBoundedAsync(request.Body, limits.MaxRequestBodyBytes, cancellationToken);

        if (bytes is null)
        {
            return Outcome.Fail<T>(TooLarge());
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(bytes.Value, new JsonDocumentOptions { MaxDepth = limits.MaxJsonDepth });
        }
        catch (JsonException)
        {
            return Outcome.Fail<T>(Problems.Of(ApiProblems.MalformedRequest, $"The body is not valid JSON, or it is nested deeper than {limits.MaxJsonDepth} levels."));
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return Outcome.Fail<T>(Problems.Validation(new FieldError("body", "The body must be a JSON object.")));
            }

            List<FieldError> unknown = [];
            FindUnknownMembers(root, typeInfo, string.Empty, unknown);

            if (unknown.Count > 0)
            {
                return Outcome.Fail<T>(Problems.Validation([.. unknown]));
            }

            try
            {
                return root.Deserialize(typeInfo) is { } value
                    ? Outcome.Ok(value)
                    : Outcome.Fail<T>(Problems.Validation(new FieldError("body", "The body must be a JSON object.")));
            }
            catch (JsonException exception)
            {
                return Outcome.Fail<T>(Problems.Validation(new FieldError(FieldOf(exception.Path), "The value has the wrong type or format.")));
            }
        }
    }

    private static ProblemResult TooLarge() => Problems.Of(ApiProblems.PayloadTooLarge, "The request body is larger than the limit of this API.");

    private static bool IsJson(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var parsed)
        && string.Equals(parsed.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)
        && (parsed.CharSet is null || string.Equals(parsed.CharSet, "utf-8", StringComparison.OrdinalIgnoreCase));

    // Reads at most one byte beyond the limit, which is enough to know that the body is too large.
    private static async Task<ReadOnlyMemory<byte>?> ReadBoundedAsync(Stream body, int limit, CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>();

        while (true)
        {
            var read = await body.ReadAsync(buffer.GetMemory(ReadChunkBytes), cancellationToken);

            if (read == 0)
            {
                return buffer.WrittenMemory;
            }

            buffer.Advance(read);

            if (buffer.WrittenCount > limit)
            {
                return null;
            }
        }
    }

    private static void FindUnknownMembers(JsonElement element, JsonTypeInfo typeInfo, string path, List<FieldError> errors)
    {
        if (element.ValueKind != JsonValueKind.Object || typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        foreach (var member in element.EnumerateObject())
        {
            var memberPath = path.Length == 0 ? member.Name : $"{path}.{member.Name}";
            var known = typeInfo.Properties.FirstOrDefault(property => string.Equals(property.Name, member.Name, StringComparison.Ordinal));

            if (known is null)
            {
                errors.Add(new FieldError(memberPath, "The contract has no such member."));
            }
            else if (member.Value.ValueKind == JsonValueKind.Object)
            {
                FindUnknownMembers(member.Value, typeInfo.Options.GetTypeInfo(known.PropertyType), memberPath, errors);
            }
        }
    }

    // A path of the serializer reads $.price.amount, and the contract names the field price.amount.
    private static string FieldOf(string? path) => string.IsNullOrEmpty(path) || path == "$" ? "body" : path.TrimStart('$', '.');
}
