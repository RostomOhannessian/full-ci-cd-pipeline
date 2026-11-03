using System.Text.Json.Serialization;
using Catalog.Application.Paging;
using Catalog.Application.Products;
using ZiggyCreatures.Caching.Fusion.Internals.Distributed;

namespace Catalog.CrossCutting.Caching;

/// <summary>
/// The System.Text.Json contract of the entries that the shared cache holds, generated at build time, so the cache needs no reflection and an entry whose type is not listed here cannot be written by accident
/// (plan section 8.5, ADR-0038). The shape of each cached type is a contract with the entries that already sit in Valkey, and <see cref="CacheContract.Version"/> is part of every key, so a change of a shape
/// raises the version and the old entries are never read. The snapshot tests hold each shape. FusionCache wraps every value in an entry that carries its metadata, and each wrapper is listed too.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ProductView))]
[JsonSerializable(typeof(Page<ProductView>))]
[JsonSerializable(typeof(FusionCacheDistributedEntry<ProductView>))]
[JsonSerializable(typeof(FusionCacheDistributedEntry<Page<ProductView>>))]
[JsonSerializable(typeof(FusionCacheDistributedEntry<long>))]
internal sealed partial class CacheJsonContext : JsonSerializerContext
{
}

/// <summary>The version of the contract of the entries that the shared cache holds, and the one place that builds the name of an entry (plan section 8.5).</summary>
public static class CacheContract
{
    /// <summary>
    /// Gets the version of the shape of the cached values. It is part of every key, as <c>v1</c>, so entries of an old shape are never read. Raise it when the shape of a cached type changes. Two versions of the
    /// application that run side by side during a roll-out keep separate entries, and a tag removal reaches both, because a tag carries no version.
    /// </summary>
    public const int Version = 1;

    /// <summary>The prefix of the name of every list, which has a shorter time to live than a product.</summary>
    public const string ListNamePrefix = "products:list:";

    /// <summary>The longest name of an entry, so a key stays well below the limits of the shared cache and a log line.</summary>
    public const int MaxNameLength = 200;

    /// <summary>Builds the key of an entry from the name that the application gave it, such as <c>v1:product:{id}</c>. The adapter adds the prefix of the environment.</summary>
    /// <param name="name">The name that the application gave the entry, such as <c>product:{id}</c>.</param>
    public static string KeyOf(string name) => $"v{Version}:{name}";
}
