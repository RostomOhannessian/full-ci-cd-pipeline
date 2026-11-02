using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Catalog.Domain.Categories;
using Catalog.Domain.Common;
using Catalog.Domain.Pricing;
using Catalog.Domain.Products;

namespace Catalog.Infrastructure.Outbox;

/// <summary>
/// Writes a domain event as the JSON that the outbox stores (plan sections 8.3 and 8.4, ADR-0035). The shape of each event is written by hand, with plain values and camel-case names, so a rename of a property of the
/// Domain never changes a stored message, and the shape is a contract with the rows that already exist. Every payload starts with <c>eventType</c>, <c>schemaVersion</c>, and <c>occurredAt</c>. The snapshot tests hold each
/// shape, and a change to one is the moment to raise the <c>SchemaVersion</c> of the event and to write the upgrade for the old payloads.
/// </summary>
public static class OutboxPayloads
{
    /// <summary>Writes the payload of an event.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <exception cref="InvalidOperationException">No shape is defined for the type of the event, so a new event cannot reach the outbox until its shape is written and tested.</exception>
    public static string Serialize(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return domainEvent switch
        {
            ProductCreated created => Write(created, writer =>
            {
                writer.WriteString("productId", created.ProductId.Value);
                writer.WriteString("sku", created.Sku.Value);
                writer.WriteString("name", created.Name.Value);
                WriteOptionalText(writer, "description", created.Description?.Value);
                WriteMoney(writer, "price", created.Price);
                writer.WriteString("categoryId", created.CategoryId.Value);
            }),
            ProductDetailsChanged changed => Write(changed, writer =>
            {
                writer.WriteString("productId", changed.ProductId.Value);
                writer.WriteString("name", changed.Name.Value);
                WriteOptionalText(writer, "description", changed.Description?.Value);
            }),
            ProductPriceChanged priced => Write(priced, writer =>
            {
                writer.WriteString("productId", priced.ProductId.Value);
                WriteMoney(writer, "oldPrice", priced.OldPrice);
                WriteMoney(writer, "newPrice", priced.NewPrice);
            }),
            ProductCategoryChanged moved => Write(moved, writer =>
            {
                writer.WriteString("productId", moved.ProductId.Value);
                writer.WriteString("oldCategoryId", moved.OldCategoryId.Value);
                writer.WriteString("newCategoryId", moved.NewCategoryId.Value);
            }),
            ProductDiscontinued discontinued => Write(discontinued, writer => writer.WriteString("productId", discontinued.ProductId.Value)),
            CategoryCreated created => Write(created, writer =>
            {
                writer.WriteString("categoryId", created.CategoryId.Value);
                writer.WriteString("name", created.Name.Value);
            }),
            CategoryRenamed renamed => Write(renamed, writer =>
            {
                writer.WriteString("categoryId", renamed.CategoryId.Value);
                writer.WriteString("oldName", renamed.OldName.Value);
                writer.WriteString("newName", renamed.NewName.Value);
            }),
            CategoryDiscontinued discontinued => Write(discontinued, writer => writer.WriteString("categoryId", discontinued.CategoryId.Value)),
            _ => throw new InvalidOperationException($"No outbox payload is defined for the event type {domainEvent.GetType().Name}. Write its shape in OutboxPayloads and add a snapshot test before the event can be saved."),
        };
    }

    private static string Write(IDomainEvent domainEvent, Action<Utf8JsonWriter> body)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("eventType", domainEvent.EventType);
            writer.WriteNumber("schemaVersion", domainEvent.SchemaVersion);
            writer.WriteString("occurredAt", domainEvent.OccurredAt.ToString("O", CultureInfo.InvariantCulture));
            body(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteOptionalText(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    private static void WriteMoney(Utf8JsonWriter writer, string name, Money money)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("amount", money.Amount);
        writer.WriteString("currency", money.Currency.Code);
        writer.WriteEndObject();
    }
}
