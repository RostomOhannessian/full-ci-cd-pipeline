using Catalog.Application.Idempotency;
using Catalog.Application.Security;

namespace Catalog.Application.Messaging;

/// <summary>
/// A command or a query that the caller must be allowed to run. Every operation of the version 1 contract names the scope that it needs, so every request type
/// implements this interface, and the authorization decorator reads the access level before the handler runs (REQ-APP-005, ADR-0032).
/// </summary>
public interface IProtectedRequest
{
    /// <summary>Gets what the caller must be allowed to do. It belongs to the type of the request and not to an instance, so a handler cannot change it.</summary>
    static abstract AccessLevel RequiredAccess { get; }
}

/// <summary>
/// A command that runs at most once for each key. The idempotency decorator reserves the key before the handler runs, replays the first result when the same
/// request comes again, and refuses a different request that uses the same key. A command whose key is null runs without that protection.
/// </summary>
public interface IIdempotentCommand
{
    /// <summary>Gets the key that the caller sent, or null when it sent none.</summary>
    IdempotencyKey? IdempotencyKey { get; }
}
