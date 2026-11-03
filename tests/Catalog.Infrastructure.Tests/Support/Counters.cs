namespace Catalog.Infrastructure.Tests.Support;

/// <summary>Counters for synthetic data, so two tests of a class that share a database never build the same identifier or SKU, and a failure names a value that a person can find again (plan section 10.3).</summary>
internal static class Counters
{
    private static long LastValue = 1_000;

    /// <summary>Returns a number that no earlier call of this run returned.</summary>
    public static long Next() => Interlocked.Increment(ref LastValue);

    /// <summary>Returns a valid SKU that no earlier call of this run returned.</summary>
    public static string Sku() => $"SKU-{Next()}";
}
