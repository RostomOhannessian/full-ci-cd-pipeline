namespace Catalog.Domain.Common;

/// <summary>The lifecycle of a category or a product. The only transition is from <see cref="Active"/> to <see cref="Discontinued"/>.</summary>
public enum LifecycleStatus
{
    /// <summary>The item is in use. A product can be assigned to an active category.</summary>
    Active = 0,

    /// <summary>The item is retired. It stays in the catalog, and it can be read.</summary>
    Discontinued = 1,
}
