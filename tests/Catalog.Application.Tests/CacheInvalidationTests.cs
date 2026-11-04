using AwesomeAssertions;
using Catalog.Application.Caching;
using Catalog.Application.Events;
using Catalog.Application.Products;
using Catalog.Application.Results;
using Catalog.Domain.Common;
using Xunit;

namespace Catalog.Application.Tests;

/// <summary>
/// REQ-CACHE-002: a write removes the entries that it could have changed. The policy maps each domain event to the tags to remove, the subscriber removes them through the port, and both paths of the plan (the best-effort
/// dispatch after the commit and the outbox) use the same code, so they cannot disagree (plan section 8.5, ADR-0039). The tests need no cache, no database, and no clock.
/// </summary>
public sealed class CacheInvalidationTests
{
    private static readonly Guid Product = Guid.Parse("018f0000-0000-7000-8000-000000000001");
    private static readonly Guid Category = Guid.Parse("018f0000-0000-7000-8000-0000000000c1");
    private static readonly Guid OtherCategory = Guid.Parse("018f0000-0000-7000-8000-0000000000c2");
    private static readonly Guid MessageId = Guid.Parse("018f0000-0000-7000-8000-00000000aaaa");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static OutboxMessageEnvelope Message(string eventType, string payload, int schemaVersion = 1) =>
        new(MessageId, eventType, schemaVersion, new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero), payload);

    private const string Money = "{\"amount\":1,\"currency\":\"USD\"}";

    private static string Field(string name, Guid id) => $"\"{name}\":\"{id}\"";

    private static string Json(string eventType, params string[] fields) =>
        "{\"eventType\":\"" + eventType + "\",\"schemaVersion\":1,\"occurredAt\":\"2026-11-03T10:00:00.0000000+00:00\"," + string.Join(',', fields) + "}";

    public static TheoryData<string, string, string[]> Events() => new()
    {
        { "ProductCreated", Json("ProductCreated", Field("productId", Product), "\"sku\":\"KB-100\"", "\"name\":\"Keyboard\"", "\"description\":null", "\"price\":" + Money, Field("categoryId", Category)), [CacheTags.Product(Product), CacheTags.ProductList] },
        { "ProductDetailsChanged", Json("ProductDetailsChanged", Field("productId", Product), "\"name\":\"Keyboard\"", "\"description\":null"), [CacheTags.Product(Product), CacheTags.ProductList] },
        { "ProductPriceChanged", Json("ProductPriceChanged", Field("productId", Product), "\"oldPrice\":" + Money, "\"newPrice\":" + Money), [CacheTags.Product(Product), CacheTags.ProductList] },
        { "ProductCategoryChanged", Json("ProductCategoryChanged", Field("productId", Product), Field("oldCategoryId", Category), Field("newCategoryId", OtherCategory)), [CacheTags.Product(Product), CacheTags.ProductList] },
        { "ProductDiscontinued", Json("ProductDiscontinued", Field("productId", Product)), [CacheTags.Product(Product), CacheTags.ProductList] },
        { "CategoryCreated", Json("CategoryCreated", Field("categoryId", Category), "\"name\":\"Keyboards\""), [CacheTags.Category(Category)] },
        { "CategoryRenamed", Json("CategoryRenamed", Field("categoryId", Category), "\"oldName\":\"Keyboards\"", "\"newName\":\"Keys\""), [CacheTags.Category(Category)] },
        { "CategoryDiscontinued", Json("CategoryDiscontinued", Field("categoryId", Category)), [CacheTags.Category(Category)] },
    };

    [Theory]
    [MemberData(nameof(Events))]
    [Trait("Requirement", "REQ-CACHE-002")]
    public void Each_event_removes_the_entry_of_the_product_and_the_lists_or_the_lists_of_the_category(string eventType, string payload, string[] expected)
    {
        var tags = CacheInvalidationPolicy.TagsFor(Message(eventType, payload));

        tags.Should().Equal(expected);
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-002")]
    public void The_policy_knows_every_event_of_the_domain_and_nothing_else()
    {
        var domainEvents = typeof(IDomainEvent).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IDomainEvent).IsAssignableFrom(type))
            .Select(type => type.Name)
            .ToList();

        domainEvents.Should().NotBeEmpty();
        CacheInvalidationPolicy.EventTypes.Should().BeEquivalentTo(domainEvents, "a new event of the domain must say which entries it makes stale, or the cache serves them until they expire");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-002")]
    public void Every_tag_that_a_cached_query_attaches_is_one_that_a_write_removes()
    {
        var productTags = new GetProduct(Product).CacheKey.Tags;
        var listTags = new ListProducts().CacheKey.Tags;
        var categoryListTags = new ListProducts(CategoryId: Category).CacheKey.Tags;

        var removedByProductEvents = CacheInvalidationPolicy.TagsFor(Message("ProductPriceChanged", Json("ProductPriceChanged", Field("productId", Product), "\"oldPrice\":" + Money, "\"newPrice\":" + Money)));
        var removedByCategoryEvents = CacheInvalidationPolicy.TagsFor(Message("CategoryRenamed", Json("CategoryRenamed", Field("categoryId", Category), "\"oldName\":\"a\"", "\"newName\":\"b\"")));

        productTags.Should().OnlyContain(tag => removedByProductEvents.Contains(tag), "the entry of a product is removed by the events of that product");
        listTags.Should().OnlyContain(tag => removedByProductEvents.Contains(tag), "an unfiltered list is removed by every product event");
        categoryListTags.Should().Contain(removedByCategoryEvents, "a list that is narrowed to a category carries the tag that the events of the category remove");
        categoryListTags.Should().Contain(CacheTags.ProductList, "and it is removed by every product event too");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-002")]
    public void An_event_that_the_policy_does_not_know_is_refused_so_the_outbox_dead_letters_it_and_never_hides_a_stale_entry()
    {
        var read = () => CacheInvalidationPolicy.TagsFor(Message("ProductTeleported", """{"productId":"x"}"""));

        read.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("ProductTeleported").And.Contain(MessageId.ToString());
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"eventType":"ProductDiscontinued","schemaVersion":1}""")]
    [InlineData("""{"eventType":"ProductDiscontinued","schemaVersion":1,"productId":"not-a-guid"}""")]
    [InlineData("""{"eventType":"ProductDiscontinued","schemaVersion":1,"productId":42}""")]
    [InlineData("[]")]
    [Trait("Requirement", "REQ-CACHE-002")]
    public void A_payload_that_is_not_in_the_shape_of_its_event_is_refused_and_the_message_never_repeats_it(string payload)
    {
        var read = () => CacheInvalidationPolicy.TagsFor(Message("ProductDiscontinued", payload));

        var failure = read.Should().Throw<InvalidOperationException>().Which;
        failure.Message.Should().Contain("ProductDiscontinued").And.Contain(MessageId.ToString()).And.NotContain(payload);
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-002")]
    public void A_shape_of_a_newer_version_is_refused_because_the_policy_cannot_tell_what_it_changes()
    {
        var read = () => CacheInvalidationPolicy.TagsFor(Message("ProductDiscontinued", Json("ProductDiscontinued", Field("productId", Product)), schemaVersion: 2));

        read.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("version 2");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-002")]
    public async Task The_subscriber_removes_each_tag_once_through_the_port_in_the_order_of_the_policy()
    {
        var cache = new RecordingCache();
        var subscriber = new CacheInvalidationSubscriber(cache);

        await subscriber.HandleAsync(Message("ProductDiscontinued", Json("ProductDiscontinued", Field("productId", Product))), Token);

        cache.Removed.Should().Equal(CacheTags.Product(Product), CacheTags.ProductList);
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-002")]
    public async Task A_tag_that_is_not_confirmed_does_not_stop_the_others_and_the_failure_reaches_the_outbox_so_it_tries_again()
    {
        var cache = new RecordingCache { Failing = { CacheTags.Product(Product) } };
        var subscriber = new CacheInvalidationSubscriber(cache);

        var handle = () => subscriber.HandleAsync(Message("ProductDiscontinued", Json("ProductDiscontinued", Field("productId", Product))), Token);

        (await handle.Should().ThrowAsync<CacheInvalidationIncompleteException>()).Which.Message.Should().Contain("1 of 2").And.Contain(MessageId.ToString());
        cache.Removed.Should().Equal(CacheTags.Product(Product), CacheTags.ProductList);
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-002")]
    public async Task Any_other_fault_and_a_cancellation_reach_the_caller_unchanged()
    {
        var cache = new RecordingCache { Fault = new InvalidOperationException("boom") };
        var subscriber = new CacheInvalidationSubscriber(cache);
        var message = Message("ProductDiscontinued", Json("ProductDiscontinued", Field("productId", Product)));

        var fault = () => subscriber.HandleAsync(message, Token);
        (await fault.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("boom");

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        cache.Fault = new OperationCanceledException(cancelled.Token);
        var cancel = () => subscriber.HandleAsync(message, cancelled.Token);
        await cancel.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-002")]
    public void The_name_of_the_subscriber_is_stable_and_fits_the_receipt()
    {
        var subscriber = new CacheInvalidationSubscriber(new RecordingCache());

        subscriber.Name.Should().Be("catalog-cache-invalidation", "the name is the key of the receipt, so renaming the class never changes it");
        subscriber.Name.Length.Should().BeLessThanOrEqualTo(200);
        subscriber.Name.Should().MatchRegex("^[\\x20-\\x7E]+$");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-002")]
    public async Task A_missing_message_or_cache_is_refused()
    {
        var noCache = () => new CacheInvalidationSubscriber(null!);
        var noMessage = () => new CacheInvalidationSubscriber(new RecordingCache()).HandleAsync(null!, Token);
        var noPolicyMessage = () => CacheInvalidationPolicy.TagsFor(null!);

        noCache.Should().Throw<ArgumentNullException>();
        await noMessage.Should().ThrowAsync<ArgumentNullException>();
        noPolicyMessage.Should().Throw<ArgumentNullException>();
    }

    /// <summary>A cache that records the tags it is asked to remove, and fails for the ones that a test names.</summary>
    private sealed class RecordingCache : ICatalogCache
    {
        public List<string> Removed { get; } = [];

        public HashSet<string> Failing { get; } = [];

        public Exception? Fault { get; set; }

        public Task<Result<T>> GetOrLoadAsync<T>(CacheKey key, Func<CancellationToken, Task<Result<T>>> load, CancellationToken cancellationToken) => load(cancellationToken);

        public Task RemoveByTagAsync(string tag, CancellationToken cancellationToken)
        {
            Removed.Add(tag);

            if (Fault is not null)
            {
                return Task.FromException(Fault);
            }

            return Failing.Contains(tag) ? Task.FromException(new CacheInvalidationIncompleteException("not confirmed")) : Task.CompletedTask;
        }
    }
}
