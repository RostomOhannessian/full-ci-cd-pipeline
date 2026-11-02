namespace Catalog.Application.Messaging;

/// <summary>
/// Where a feature registers its handlers. The Application defines it so that a feature can name its internal handlers without the Application referencing a container, and the composition root implements
/// it over its container and composes each chain of decorators with <c>CommandPipeline</c> and <c>QueryPipeline</c> (plan section 8.2, ADR-0032). A feature registers each handler once, in a fixed order, so
/// the wiring is the same on every start and a test can read it.
/// </summary>
public interface IFeatureRegistry
{
    /// <summary>Registers the handler of a command.</summary>
    /// <typeparam name="TCommand">The type of the command.</typeparam>
    /// <typeparam name="TResult">The type of the value that a success returns.</typeparam>
    /// <typeparam name="THandler">The type of the handler.</typeparam>
    void AddCommandHandler<TCommand, TResult, THandler>()
        where TCommand : IProtectedRequest
        where THandler : class, ICommandHandler<TCommand, TResult>;

    /// <summary>Registers the handler of a query.</summary>
    /// <typeparam name="TQuery">The type of the query.</typeparam>
    /// <typeparam name="TResult">The type of the value that a success returns.</typeparam>
    /// <typeparam name="THandler">The type of the handler.</typeparam>
    void AddQueryHandler<TQuery, TResult, THandler>()
        where TQuery : IProtectedRequest
        where THandler : class, IQueryHandler<TQuery, TResult>;
}
