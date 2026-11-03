namespace Catalog.Infrastructure.Tests.Support;

/// <summary>
/// Waits until a condition about an external system holds, with a bound, and fails the test when it never does. A test uses it only where the thing that it waits for is outside its control, such as a server that pushes
/// a message to a subscriber (plan section 10.3). It is a poll of observable state and never a pause that makes a test pass: a condition that does not hold within the bound fails with the reason that the caller gave.
/// </summary>
internal static class Poll
{
    /// <summary>The longest time that a condition may take. A healthy run takes a few milliseconds.</summary>
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    /// <summary>Polls a condition until it holds.</summary>
    /// <param name="condition">The condition.</param>
    /// <param name="because">What the test waits for, for the message of a failure.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    public static async Task UntilAsync(Func<Task<bool>> condition, string because, CancellationToken cancellationToken = default)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(Bound);

        try
        {
            while (!await condition())
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), limit.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new Xunit.Sdk.XunitException($"The condition did not hold within {Bound.TotalSeconds:0} seconds: {because}.");
        }
    }
}
