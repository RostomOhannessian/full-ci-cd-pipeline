namespace Catalog.Domain.Common;

/// <summary>The check for an identifier that is a UUID version 7, which RFC 9562 defines as a 48-bit Unix time in milliseconds followed by random bits.</summary>
internal static class Uuid7
{
    private const int Version = 7;

    // The two high bits of the variant field are 10 for the layout of RFC 9562.
    private const byte VariantMask = 0b1100_0000;

    private const byte Rfc9562Variant = 0b1000_0000;

    /// <summary>
    /// Gets a value that says whether a GUID is a version 7 UUID of the RFC 9562 variant. The empty GUID is not. The check reads the bytes in the order of the RFC,
    /// because <see cref="Guid.Variant"/> reports the top four bits of the variant byte and not the two bits that the RFC names.
    /// </summary>
    /// <param name="value">The GUID to check.</param>
    public static bool IsValid(Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        value.TryWriteBytes(bytes, bigEndian: true, out _);

        return (bytes[6] >> 4) == Version && (bytes[8] & VariantMask) == Rfc9562Variant;
    }
}
