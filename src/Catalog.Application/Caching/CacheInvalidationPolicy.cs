using System.Text.Json;
using Catalog.Application.Events;

namespace Catalog.Application.Caching;

/// <summary>
/// Says which cache entries a domain event makes stale, as the tags to remove (plan section 8.5, ADR-0039). It is the one table that both paths of an invalidation use: the best-effort dispatch right after the
/// commit and the outbox that guarantees it call the same subscriber, so they cannot disagree. Every list of products carries the tag <see cref="CacheTags.ProductList"/>, so one removal of it covers the lists with
/// any filter, and the tag of a category is left for the events of the category. An event that the table does not know, a payload that is not in the shape of its event, and a version of the shape that this code does
/// not read are refused with an exception, because a silent skip would leave a stale entry in the cache until it expires. The outbox then retries and finally dead-letters the message, which is a metric and an alert.
/// A test fails when the Domain gets an event that this table lacks, so a new event cannot ship without a rule.
/// </summary>
public static class CacheInvalidationPolicy
{
    private const int SupportedSchemaVersion = 1;

    /// <summary>Gets the names of the event types that the policy knows, which are the names of the event types of the Domain.</summary>
    public static IReadOnlyCollection<string> EventTypes { get; } =
    [
        "ProductCreated",
        "ProductDetailsChanged",
        "ProductPriceChanged",
        "ProductCategoryChanged",
        "ProductDiscontinued",
        "CategoryCreated",
        "CategoryRenamed",
        "CategoryDiscontinued",
    ];

    /// <summary>Returns the tags that the event makes stale, in the order in which they are removed.</summary>
    /// <param name="message">The message of the outbox that carries the event.</param>
    /// <exception cref="InvalidOperationException">The event type is not known, the version of the payload is not read, or the payload lacks a field that the rule needs. The text names the event type and the message and never the payload.</exception>
    public static IReadOnlyList<string> TagsFor(OutboxMessageEnvelope message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!EventTypes.Contains(message.EventType))
        {
            throw Refusal(message, "no rule says which cache entries the event makes stale");
        }

        if (message.SchemaVersion != SupportedSchemaVersion)
        {
            throw Refusal(message, $"the payload has the version {message.SchemaVersion} and only version {SupportedSchemaVersion} is read");
        }

        try
        {
            using var document = JsonDocument.Parse(message.Payload);
            var payload = document.RootElement;

            return message.EventType switch
            {
                "CategoryCreated" or "CategoryRenamed" or "CategoryDiscontinued" => [CacheTags.Category(Identifier(payload, "categoryId"))],
                _ => [CacheTags.Product(Identifier(payload, "productId")), CacheTags.ProductList],
            };
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        {
            throw Refusal(message, "the payload is not in the shape of its event", exception);
        }
    }

    private static Guid Identifier(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.TryGetGuid(out var id)
            ? id
            : throw new KeyNotFoundException($"The field {name} is missing or is not an identifier.");

    private static InvalidOperationException Refusal(OutboxMessageEnvelope message, string reason, Exception? inner = null) =>
        new($"The cache cannot tell what the event {message.EventType} of message {message.MessageId} makes stale: {reason}.", inner);
}
