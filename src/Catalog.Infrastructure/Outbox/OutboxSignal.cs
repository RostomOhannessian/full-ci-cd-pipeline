namespace Catalog.Infrastructure.Outbox;

/// <summary>
/// The signal between the dispatcher that a commit wakes and the background service that waits (plan section 8.4). A notification that arrives while nobody waits is kept, so the next wait returns at once, and many
/// notifications count as one, because one pass leases a whole batch.
/// </summary>
public sealed class OutboxSignal : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(0, 1);

    /// <summary>Wakes the waiting service, or makes the next wait return at once.</summary>
    public void Notify()
    {
        try
        {
            _semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // A notification is already waiting, and one is enough.
        }
    }

    /// <summary>Waits for a notification or for the timeout, and returns true when a notification ended the wait.</summary>
    /// <param name="timeout">The longest wait.</param>
    /// <param name="clock">The clock that measures the timeout, so a test can move it.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    public async Task<bool> WaitAsync(TimeSpan timeout, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clock);

        using var stopWaiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var notified = _semaphore.WaitAsync(stopWaiting.Token);
        var timedOut = false;

        try
        {
            await notified.WaitAsync(timeout, clock, cancellationToken);

            return true;
        }
        catch (TimeoutException)
        {
            timedOut = true;

            return false;
        }
        finally
        {
            // The wait that timed out must not stay in the queue of the semaphore, because it would take the next notification.
            await stopWaiting.CancelAsync();
            try
            {
                await notified;

                // A notification that arrived while the wait timed out was taken by the wait that was cancelled, so it goes back.
                if (timedOut)
                {
                    Notify();
                }
            }
            catch (OperationCanceledException)
            {
                // The wait was cancelled on purpose.
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _semaphore.Dispose();
}
