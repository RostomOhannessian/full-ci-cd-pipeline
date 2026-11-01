using Catalog.Application.Idempotency;

namespace Catalog.Application.Tests.Support;

/// <summary>Builders for values that need a valid form.</summary>
internal static class Make
{
    public static IdempotencyKey Key(string text) =>
        IdempotencyKey.TryCreate(text, out var key) ? key : throw new ArgumentException($"'{text}' is not a valid idempotency key.", nameof(text));
}
