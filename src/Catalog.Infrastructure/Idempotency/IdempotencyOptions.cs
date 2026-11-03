namespace Catalog.Infrastructure.Idempotency;

/// <summary>The settings of the idempotency store (plan section 8.4).</summary>
public sealed class IdempotencyOptions
{
    /// <summary>Gets or sets how long a record counts, from the moment that the key was reserved. The plan says 24 hours.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Gets or sets how long a reservation that never finished blocks the key. A request that dies after it reserved the key leaves an in-progress record, and after this time another request with the same key may take the
    /// reservation over, so a crash does not lock a key for the whole retention. Default 2 minutes.
    /// </summary>
    public TimeSpan InProgressTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Checks the bounds and returns the problems, or an empty list when the settings are valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (Retention <= TimeSpan.Zero)
        {
            problems.Add("Retention must be positive.");
        }

        if (InProgressTimeout <= TimeSpan.Zero || InProgressTimeout >= Retention)
        {
            problems.Add("InProgressTimeout must be positive and shorter than Retention.");
        }

        return problems;
    }
}
