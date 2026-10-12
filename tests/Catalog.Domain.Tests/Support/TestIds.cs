using System.Buffers.Binary;
using Catalog.Domain.Categories;
using Catalog.Domain.Products;

namespace Catalog.Domain.Tests.Support;

/// <summary>
/// Identifiers for tests. Each one is a valid UUID version 7 that is built from a counter, so the same call gives the same identifier on every run and
/// on every host, and a failure names an identifier that a person can find again. <see cref="Guid.NewGuid()"/> is never used in these tests (plan section 10.3).
/// </summary>
internal static class TestIds
{
    /// <summary>Builds a UUID version 7 whose 48-bit timestamp field is <paramref name="counter"/>, so identifiers sort in the order of their counters.</summary>
    public static Guid V7(long counter)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteInt64BigEndian(bytes[..8], counter << 16);
        bytes[6] = 0x70;
        bytes[7] = 0x01;
        bytes[8] = 0x80;
        bytes[15] = 0x2A;

        return new Guid(bytes, bigEndian: true);
    }

    public static ProductId Product(long counter = 1) => ProductId.Create(V7(counter)).Value;

    public static CategoryId Category(long counter = 1) => CategoryId.Create(V7(counter)).Value;
}
