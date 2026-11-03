using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using Catalog.Application.Caching;
using Catalog.Application.Categories;
using Catalog.Application.Messaging;
using Catalog.Application.Paging;
using Catalog.Application.Products;
using Catalog.Application.Results;
using Catalog.Application.Tests.Support;
using Catalog.CrossCutting.Caching;
using Catalog.Domain.Common;
using Catalog.Domain.Tests.Support;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace Catalog.Application.Tests;

/// <summary>
/// REQ-CACHE-001: the entries that the shared cache holds are a contract with the nodes that read them, possibly of another version of the application. The shape of each cached type is a snapshot, so a change to a view
/// fails a test here until someone raises <see cref="CacheContract.Version"/>, and a query that returns a type that the contract does not list fails a test, instead of silently falling back to the memory of one node.
/// </summary>
public sealed class CacheContractTests
{
    private static readonly Guid Product = TestIds.Product(1).Value;
    private static readonly Guid Category = TestIds.Category(1).Value;
    private static readonly DateTimeOffset Moment = new(2026, 1, 15, 9, 30, 0, TimeSpan.Zero);

    private static readonly ProductView SampleProduct = new(Product, "KB-100", "Mechanical keyboard", null, 89.90m, "USD", Category, LifecycleStatus.Active, Moment, Moment, "AAAAAAAAB9E");

    private static readonly Dictionary<Type, object> Samples = new()
    {
        [typeof(ProductView)] = SampleProduct,
        [typeof(Page<ProductView>)] = new Page<ProductView>([SampleProduct], "cursor-1"),
    };

    public static TheoryData<string> CachedResultTypes => [.. CachedQueryResults().Select(type => type.Name)];

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public void The_value_of_a_product_in_the_shared_cache_has_the_shape_of_the_snapshot()
    {
        var value = StoredValue(SampleProduct, new CacheKey($"product:{Product:D}", [$"product:{Product:D}"]));

        value.Should().Be(
            $$"""{"id":"{{Product}}","sku":"KB-100","name":"Mechanical keyboard","description":null,"amount":89.90,"currency":"USD","categoryId":"{{Category}}","status":"Active","createdAt":"2026-01-15T09:30:00+00:00","updatedAt":"2026-01-15T09:30:00+00:00","eTag":"AAAAAAAAB9E"}""",
            "a change of this shape must raise CacheContract.Version, so that the old entries are never read");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public void The_value_of_a_page_in_the_shared_cache_has_the_shape_of_the_snapshot()
    {
        var page = (Page<ProductView>)Samples[typeof(Page<ProductView>)];

        var value = StoredValue(page, new CacheKey("products:list:abc", ["products:list"]));

        value.Should().Be(
            $$"""{"items":[{"id":"{{Product}}","sku":"KB-100","name":"Mechanical keyboard","description":null,"amount":89.90,"currency":"USD","categoryId":"{{Category}}","status":"Active","createdAt":"2026-01-15T09:30:00+00:00","updatedAt":"2026-01-15T09:30:00+00:00","eTag":"AAAAAAAAB9E"}],"nextCursor":"cursor-1"}""",
            "a change of this shape must raise CacheContract.Version, so that the old entries are never read");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public void The_version_of_the_contract_is_one_and_every_key_starts_with_it()
    {
        CacheContract.Version.Should().Be(1);
        CacheContract.KeyOf("product:x").Should().StartWith($"v{CacheContract.Version}:");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public void Every_cached_query_returns_a_type_that_has_a_sample_and_therefore_a_place_in_the_contract()
    {
        var results = CachedQueryResults().ToList();

        results.Should().NotBeEmpty();
        results.Should().OnlyContain(type => Samples.ContainsKey(type), "a new cached result needs a sample here and an entry in CacheJsonContext");
    }

    [Theory]
    [MemberData(nameof(CachedResultTypes))]
    [Trait("Requirement", "REQ-CACHE-001")]
    public async Task Every_cached_result_type_goes_through_the_shared_cache_and_comes_back_equal(string typeName)
    {
        var type = CachedQueryResults().Single(candidate => candidate.Name == typeName);

        var equal = await (Task<bool>)typeof(CacheContractTests)
            .GetMethod(nameof(RoundTripAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type)
            .Invoke(null, [Samples[type]])!;

        equal.Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public void The_keys_that_the_queries_build_hold_only_lower_case_words_identifiers_and_a_hash_and_no_value_of_a_request()
    {
        var queries = new ICachedQuery[]
        {
            new GetProduct(Product),
            new ListProducts(Category, null, "secret search text", "name", 20, "opaque-cursor"),
            new ListProducts(),
        };

        foreach (var key in queries.Select(query => query.CacheKey))
        {
            key.Name.Should().MatchRegex(@"^(product:[0-9a-f-]{36}|products:list:[0-9a-f]{32})$");
            key.Name.Should().NotContain("secret").And.NotContain("opaque");
            key.Name.Length.Should().BeLessThanOrEqualTo(CacheContract.MaxNameLength);
            key.Tags.Should().OnlyContain(tag => tag == "products:list" || tag.StartsWith("product:", StringComparison.Ordinal) || tag.StartsWith("category:", StringComparison.Ordinal));
        }
    }

    private static IEnumerable<Type> CachedQueryResults()
    {
        var registry = new QueryRegistry();
        registry.AddCategories().AddProducts();

        return registry.Queries.Where(query => typeof(ICachedQuery).IsAssignableFrom(query.Query)).Select(query => query.Result).Distinct();
    }

    private static string StoredValue<T>(T value, CacheKey key)
    {
        var shared = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        using var rig = new CacheRig(shared: shared);

        rig.Cache.GetOrLoadAsync(key, _ => Task.FromResult(Result.Success(value)), CancellationToken.None).GetAwaiter().GetResult();

        var stored = shared.Get($"v2:catalog:dev:v{CacheContract.Version}:{key.Name}");
        stored.Should().NotBeNull("the entry reached the shared cache under the key of the contract");
        using var document = JsonDocument.Parse(stored!);

        return document.RootElement.GetProperty("value").GetRawText();
    }

    private static async Task<bool> RoundTripAsync<T>(T sample)
        where T : notnull
    {
        var shared = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        using var writer = new CacheRig(shared: shared);
        using var reader = new CacheRig(shared: shared);
        var key = new CacheKey("products:list:roundtrip", ["products:list"]);

        await writer.Cache.GetOrLoadAsync(key, _ => Task.FromResult(Result.Success(sample)), CancellationToken.None);
        var read = await reader.Cache.GetOrLoadAsync<T>(key, _ => throw new InvalidOperationException("The shared cache holds the entry."), CancellationToken.None);

        return read.IsSuccess && JsonSerializer.Serialize(read.Value) == JsonSerializer.Serialize(sample);
    }

    private sealed class QueryRegistry : IFeatureRegistry
    {
        public List<(Type Query, Type Result)> Queries { get; } = [];

        public void AddCommandHandler<TCommand, TResult, THandler>()
            where TCommand : IProtectedRequest
            where THandler : class, ICommandHandler<TCommand, TResult>
        {
        }

        public void AddQueryHandler<TQuery, TResult, THandler>()
            where TQuery : IProtectedRequest
            where THandler : class, IQueryHandler<TQuery, TResult> =>
            Queries.Add((typeof(TQuery), typeof(TResult)));
    }
}
