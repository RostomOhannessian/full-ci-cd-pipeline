using System.Globalization;
using Catalog.Application.Identity;
using Catalog.Application.Messaging;
using Catalog.Application.Paging;
using Catalog.Application.Persistence;
using Catalog.Application.Results;
using Catalog.Domain.Categories;
using Catalog.Domain.Pricing;
using Catalog.Domain.Products;

namespace Catalog.Application.Products;

/// <summary>The values that create and update share, each read through the factory of its value object, so every rule is the model's and none is repeated here.</summary>
internal sealed record ProductValues(ProductName Name, Description? Description, Money Price, CategoryId CategoryId)
{
    public static Result<ProductValues> Parse(string? name, string? description, decimal amount, string? currency, Guid categoryId)
    {
        var parsedName = ProductName.Create(name);
        if (!parsedName.IsSuccess)
        {
            return Handling.Fail<ProductValues>(parsedName.Error);
        }

        var parsedDescription = Description.Create(description);
        if (!parsedDescription.IsSuccess)
        {
            return Handling.Fail<ProductValues>(parsedDescription.Error);
        }

        var parsedCurrency = Currency.Create(currency);
        if (!parsedCurrency.IsSuccess)
        {
            return Handling.Fail<ProductValues>(parsedCurrency.Error.Within("price"));
        }

        var price = Money.Create(amount, parsedCurrency.Value);
        if (!price.IsSuccess)
        {
            return Handling.Fail<ProductValues>(price.Error.Within("price"));
        }

        var parsedCategory = CategoryId.Create(categoryId);
        if (!parsedCategory.IsSuccess)
        {
            return Handling.Fail<ProductValues>(parsedCategory.Error);
        }

        return Result.Success(new ProductValues(parsedName.Value, parsedDescription.Value, price.Value, parsedCategory.Value));
    }

    /// <summary>Loads the category that the product is assigned to. The contract answers a category that does not exist with a validation error on <c>categoryId</c> (ADR-0028).</summary>
    public async Task<Result<Category>> FindCategoryAsync(ICategoryRepository categories, CancellationToken cancellationToken)
    {
        var found = await categories.FindAsync(CategoryId, cancellationToken);

        return found is null ? Handling.Invalid<Category>("categoryId", "The category does not exist.") : Result.Success(found.Entity);
    }
}

internal sealed class CreateProductHandler(IProductRepository products, ICategoryRepository categories, IIdGenerator ids, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<CreateProduct, ProductView>
{
    public async Task<Result<ProductView>> HandleAsync(CreateProduct command, CancellationToken cancellationToken)
    {
        var sku = Sku.Create(command.Sku);
        if (!sku.IsSuccess)
        {
            return Handling.Fail<ProductView>(sku.Error);
        }

        var values = ProductValues.Parse(command.Name, command.Description, command.Amount, command.Currency, command.CategoryId);
        if (!values.IsSuccess)
        {
            return Result.Failure<ProductView>(values.Error);
        }

        var category = await values.Value.FindCategoryAsync(categories, cancellationToken);
        if (!category.IsSuccess)
        {
            return Result.Failure<ProductView>(category.Error);
        }

        if (await products.SkuExistsAsync(sku.Value, cancellationToken))
        {
            return Result.Failure<ProductView>(new Conflict(ConflictReason.SkuAlreadyExists));
        }

        var created = Product.Create(ids.NewProductId(), sku.Value, values.Value.Name, values.Value.Description, values.Value.Price, category.Value, clock);
        if (!created.IsSuccess)
        {
            return Handling.Fail<ProductView>(created.Error);
        }

        products.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ProductView.From(created.Value, products.TagOf(created.Value)));
    }
}

internal sealed class UpdateProductHandler(IProductRepository products, ICategoryRepository categories, IUnitOfWork unitOfWork, TimeProvider clock) : ICommandHandler<UpdateProduct, ProductView>
{
    public async Task<Result<ProductView>> HandleAsync(UpdateProduct command, CancellationToken cancellationToken)
    {
        if (Handling.RequireVersion(command.IfMatch) is { } missing)
        {
            return Result.Failure<ProductView>(missing);
        }

        var id = ProductId.Create(command.ProductId);
        if (!id.IsSuccess)
        {
            return Handling.Fail<ProductView>(id.Error);
        }

        var values = ProductValues.Parse(command.Name, command.Description, command.Amount, command.Currency, command.CategoryId);
        if (!values.IsSuccess)
        {
            return Result.Failure<ProductView>(values.Error);
        }

        var found = await products.FindAsync(id.Value, cancellationToken);
        if (found is null)
        {
            return Result.Failure<ProductView>(new NotFound("product"));
        }

        if (Handling.MatchVersion(command.IfMatch!, found.Tag) is { } stale)
        {
            return Result.Failure<ProductView>(stale);
        }

        var category = await values.Value.FindCategoryAsync(categories, cancellationToken);
        if (!category.IsSuccess)
        {
            return Result.Failure<ProductView>(category.Error);
        }

        var updated = found.Entity.Update(values.Value.Name, values.Value.Description, values.Value.Price, category.Value, clock);
        if (!updated.IsSuccess)
        {
            return Handling.Fail<ProductView>(updated.Error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ProductView.From(found.Entity, products.TagOf(found.Entity)));
    }
}

internal sealed class DiscontinueProductHandler(IProductRepository products, IUnitOfWork unitOfWork, TimeProvider clock) : ICommandHandler<DiscontinueProduct, ProductView>
{
    public async Task<Result<ProductView>> HandleAsync(DiscontinueProduct command, CancellationToken cancellationToken)
    {
        if (Handling.RequireVersion(command.IfMatch) is { } missing)
        {
            return Result.Failure<ProductView>(missing);
        }

        var id = ProductId.Create(command.ProductId);
        if (!id.IsSuccess)
        {
            return Handling.Fail<ProductView>(id.Error);
        }

        var found = await products.FindAsync(id.Value, cancellationToken);
        if (found is null)
        {
            return Result.Failure<ProductView>(new NotFound("product"));
        }

        if (Handling.MatchVersion(command.IfMatch!, found.Tag) is { } stale)
        {
            return Result.Failure<ProductView>(stale);
        }

        found.Entity.Discontinue(clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ProductView.From(found.Entity, products.TagOf(found.Entity)));
    }
}

internal sealed class GetProductHandler(IProductReadStore store) : IQueryHandler<GetProduct, ProductView>
{
    public async Task<Result<ProductView>> HandleAsync(GetProduct query, CancellationToken cancellationToken)
    {
        var id = ProductId.Create(query.ProductId);
        if (!id.IsSuccess)
        {
            return Handling.Fail<ProductView>(id.Error);
        }

        var view = await store.GetAsync(id.Value, cancellationToken);

        return view is null ? Result.Failure<ProductView>(new NotFound("product")) : Result.Success(view);
    }
}

internal sealed class ListProductsHandler(IProductReadStore store) : IQueryHandler<ListProducts, Page<ProductView>>
{
    public async Task<Result<Page<ProductView>>> HandleAsync(ListProducts query, CancellationToken cancellationToken)
    {
        List<FieldError> errors = [];

        var sort = ListProducts.ParseSort(query.Sort);
        if (sort is null)
        {
            errors.Add(new FieldError("sort", "The sort must be createdAt or name."));
        }

        CategoryId? category = null;
        if (query.CategoryId is { } categoryId)
        {
            var parsed = CategoryId.Create(categoryId);
            if (parsed.IsSuccess)
            {
                category = parsed.Value;
            }
            else
            {
                errors.Add(new FieldError(parsed.Error.Field, parsed.Error.Message));
            }
        }

        if (query.Status is { } status && !Enum.IsDefined(status))
        {
            errors.Add(new FieldError("status", "The status must be active or discontinued."));
        }

        if (query.Q is { } search && (search.Length is < 1 or > 100))
        {
            errors.Add(new FieldError("q", "The search text must have 1 to 100 characters."));
        }

        var request = PageRequest.Parse(query.PageSize, query.Cursor, query.Sort);
        if (!request.IsSuccess && request.Error is ValidationFailed paging)
        {
            errors.AddRange(paging.Errors);
        }

        if (errors.Count > 0 || sort is null)
        {
            return Result.Failure<Page<ProductView>>(new ValidationFailed(errors));
        }

        var filter = new ProductFilter(category, query.Status, query.Q, sort.Value);
        var rows = await store.ListAsync(filter, request.Value.After, request.Value.Size + 1, cancellationToken);

        return Result.Success(Pages.Build(rows, request.Value.Size, query.Sort, row => PositionOf(row, sort.Value)));
    }

    private static KeysetPosition PositionOf(ProductView row, ProductSort sort) =>
        new(sort == ProductSort.Name ? row.Name : row.CreatedAt.ToString("O", CultureInfo.InvariantCulture), row.Id);
}
