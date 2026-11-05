namespace Catalog.Infrastructure.Outbox;

/// <summary>The settings of the outbox processor, the dispatcher, and the cleanup (plan section 8.4). The defaults suit the Compose stack, and every value has a bound that the validator checks.</summary>
public sealed class OutboxOptions
{
    /// <summary>Gets or sets the largest number of messages that one pass leases. Default 20.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>Gets or sets how long a lease holds a message, after which another processor may take it. It must be longer than the slowest handler. Default 2 minutes.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Gets or sets the number of deliveries after which a failing message moves to the dead-letter state. Default 8.</summary>
    public int MaxAttempts { get; set; } = 8;

    /// <summary>Gets or sets the delay after the first failure. The delay doubles with each attempt. Default 2 seconds.</summary>
    public TimeSpan BaseBackoff { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Gets or sets the longest delay between two attempts. Default 5 minutes.</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets or sets the share of a delay that jitter may add or remove, between 0 and 1. Jitter keeps two processors from retrying in step. Default 0.2.</summary>
    public double JitterRatio { get; set; } = 0.2;

    /// <summary>Gets or sets how long the background service waits for a wake-up before it looks for due messages. Default 5 seconds.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets how long a finished message stays in the table before the cleanup deletes it. Dead-letter messages are never deleted by the cleanup. Default 7 days.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Gets or sets how often the background service runs the cleanup. Default 1 hour.</summary>
    public TimeSpan MaintenanceInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Gets or sets how often the processor reads the backlog of the outbox for the gauges. The reading counts the dead-letter messages, so it is not made on every pass. Default 30 seconds.</summary>
    public TimeSpan ObservationInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Checks the bounds and returns the problems, or an empty list when the settings are valid. The registration calls it at startup, so a wrong setting stops the host.</summary>
    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (BatchSize is < 1 or > 1000)
        {
            problems.Add("BatchSize must be between 1 and 1000.");
        }

        if (LeaseDuration <= TimeSpan.Zero)
        {
            problems.Add("LeaseDuration must be positive.");
        }

        if (MaxAttempts < 1)
        {
            problems.Add("MaxAttempts must be at least 1.");
        }

        if (BaseBackoff <= TimeSpan.Zero || MaxBackoff < BaseBackoff)
        {
            problems.Add("BaseBackoff must be positive and MaxBackoff must not be smaller.");
        }

        if (JitterRatio is < 0 or > 1)
        {
            problems.Add("JitterRatio must be between 0 and 1.");
        }

        if (PollInterval <= TimeSpan.Zero || MaintenanceInterval <= TimeSpan.Zero || Retention <= TimeSpan.Zero || ObservationInterval <= TimeSpan.Zero)
        {
            problems.Add("PollInterval, MaintenanceInterval, Retention, and ObservationInterval must be positive.");
        }

        return problems;
    }
}
