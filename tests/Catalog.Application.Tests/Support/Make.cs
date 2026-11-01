using Catalog.Application.Idempotency;

namespace Catalog.Application.Tests.Support;

/// <summary>Builders for values that need a valid form.</summary>
internal static class Make
{
    /// <summary>A valid UUID of version 4, which the identifiers of the model refuse. It is a literal, because a test never calls <c>Guid.NewGuid()</c> (plan section 10.3).</summary>
    public static readonly Guid NotUuid7 = new("3f2504e0-4f89-41d3-9a0c-0305e82c3301");

    public static IdempotencyKey Key(string text) =>
        IdempotencyKey.TryCreate(text, out var key) ? key : throw new ArgumentException($"'{text}' is not a valid idempotency key.", nameof(text));
}
