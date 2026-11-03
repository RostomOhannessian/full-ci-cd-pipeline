using Microsoft.Extensions.Options;

namespace Catalog.CrossCutting.Caching;

/// <summary>
/// The settings of the catalog cache (plan section 8.5, ADR-0038). The defaults are the ones that the plan names: an L1 time to live of 30 seconds, which is the bound of the staleness while the backplane is down, a
/// short time to live for lists, and a circuit breaker on the shared cache and on the backplane. The environment is the only part of a key that comes from outside, so the validator holds it to a few characters
/// that a key and an access control rule can carry. The type prints nothing but its name, like the other settings of the catalog.
/// </summary>
public sealed class CatalogCacheOptions
{
    /// <summary>The name of the configuration section that the composition root binds to this type.</summary>
    public const string SectionName = "Catalog:Cache";

    /// <summary>The largest length of the environment name.</summary>
    public const int MaxEnvironmentLength = 32;

    /// <summary>Gets or sets the name of the environment, such as <c>dev</c>. It is part of every key and of the channel of the backplane, so two environments never read each other's entries. Default: <c>dev</c>.</summary>
    public string Environment { get; set; } = "dev";

    /// <summary>Gets or sets how long an entry stays in the memory of a node. This is the bound of the staleness while the backplane is down (plan section 8.5). Default: 30 seconds.</summary>
    public TimeSpan MemoryDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets how long an entry stays in the shared cache. Default: 5 minutes.</summary>
    public TimeSpan SharedDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets or sets how long a list stays in the memory of a node and in the shared cache. A list changes with every write to a product, so it lives for a shorter time. Default: 10 seconds.</summary>
    public TimeSpan ListDuration { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets or sets how many entries the memory of a node keeps before it evicts the least recently used. Every entry counts as one. Default: 10,000.</summary>
    public int MemoryMaxEntries { get; set; } = 10_000;

    /// <summary>Gets or sets the longest time that one call to the shared cache may take. A call that takes longer counts as a failure, which opens the circuit breaker. Default: 500 milliseconds.</summary>
    public TimeSpan SharedTimeout { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Gets or sets how long the circuit breaker of the shared cache stays open after a failure. Zero would turn it off, and every call would then wait for the timeout, so it must be positive. Default: 5 seconds.</summary>
    public TimeSpan SharedCircuitBreakerDuration { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets how long the circuit breaker of the backplane stays open after a failure. It must be positive. Default: 5 seconds.</summary>
    public TimeSpan BackplaneCircuitBreakerDuration { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets the prefix of every key of this environment, such as <c>catalog:dev:</c>.</summary>
    public string KeyPrefix => $"catalog:{Environment}:";

    /// <summary>Gets the prefix of the channel of the backplane of this environment, such as <c>catalog:dev</c>. FusionCache adds <c>.Backplane:v2</c>.</summary>
    public string ChannelPrefix => $"catalog:{Environment}";

    /// <summary>Checks the settings and returns the problems. A problem names a setting and never repeats a value.</summary>
    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (!IsEnvironmentName(Environment))
        {
            problems.Add($"Environment must have between 1 and {MaxEnvironmentLength} lower-case letters, digits, or hyphens, and start with a letter.");
        }

        RequirePositive(problems, MemoryDuration, nameof(MemoryDuration));
        RequirePositive(problems, SharedDuration, nameof(SharedDuration));
        RequirePositive(problems, ListDuration, nameof(ListDuration));
        RequirePositive(problems, SharedTimeout, nameof(SharedTimeout));
        RequirePositive(problems, SharedCircuitBreakerDuration, nameof(SharedCircuitBreakerDuration));
        RequirePositive(problems, BackplaneCircuitBreakerDuration, nameof(BackplaneCircuitBreakerDuration));

        if (MemoryMaxEntries < 1)
        {
            problems.Add("MemoryMaxEntries must be at least 1.");
        }

        if (MemoryDuration > SharedDuration)
        {
            problems.Add("MemoryDuration must not be longer than SharedDuration, because a node would keep an entry that the shared cache already dropped.");
        }

        if (ListDuration > MemoryDuration)
        {
            problems.Add("ListDuration must not be longer than MemoryDuration, because a list changes more often than a product.");
        }

        return problems;
    }

    /// <inheritdoc />
    public override string ToString() => nameof(CatalogCacheOptions);

    // A name that a key, a channel, and an access control rule can carry. A loop avoids a regular expression, whose generated code would sit in a namespace of its own, outside the layer.
    private static bool IsEnvironmentName(string? name) =>
        name is { Length: > 0 and <= MaxEnvironmentLength }
        && name[0] is >= 'a' and <= 'z'
        && name.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    private static void RequirePositive(List<string> problems, TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero)
        {
            problems.Add($"{name} must be positive.");
        }
    }
}

/// <summary>Validates <see cref="CatalogCacheOptions"/> when the host starts, so a wrong setting stops the host before it serves a request.</summary>
public sealed class CatalogCacheOptionsValidator : IValidateOptions<CatalogCacheOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, CatalogCacheOptions options)
    {
        var problems = options?.Validate() ?? ["The cache settings are missing."];

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
