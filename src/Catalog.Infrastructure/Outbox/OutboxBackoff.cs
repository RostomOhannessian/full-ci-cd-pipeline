namespace Catalog.Infrastructure.Outbox;

/// <summary>The delay before the next delivery of a message that failed: exponential backoff with jitter (plan section 8.4). It is a pure function, so a test checks its bounds without a clock or a random source.</summary>
public static class OutboxBackoff
{
    /// <summary>Returns the delay after a failed attempt.</summary>
    /// <param name="attempt">The number of the attempt that failed, counted from 1.</param>
    /// <param name="options">The settings.</param>
    /// <param name="unitRandom">A random number in the range from 0 up to but not including 1. The delay is changed by up to the jitter ratio in both directions, and 0.5 changes nothing.</param>
    public static TimeSpan Delay(int attempt, OutboxOptions options, double unitRandom)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);

        // Doubling stops long before the shift could overflow, because the cap is reached first.
        var steps = Math.Min(attempt - 1, 30);
        var raw = options.BaseBackoff.TotalMilliseconds * Math.Pow(2, steps);
        var capped = Math.Min(raw, options.MaxBackoff.TotalMilliseconds);
        var jitter = 1 + (((Math.Clamp(unitRandom, 0, 1) * 2) - 1) * options.JitterRatio);

        return TimeSpan.FromMilliseconds(Math.Min(capped * jitter, options.MaxBackoff.TotalMilliseconds));
    }
}
