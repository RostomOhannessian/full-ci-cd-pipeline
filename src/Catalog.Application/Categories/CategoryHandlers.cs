using Catalog.Application.Identity;
using Catalog.Application.Messaging;
using Catalog.Application.Paging;
using Catalog.Application.Persistence;
using Catalog.Application.Results;
using Catalog.Domain.Categories;

namespace Catalog.Application.Categories;

internal sealed class CreateCategoryHandler(ICategoryRepository categories, IIdGenerator ids, IUnitOfWork unitOfWork, TimeProvider clock) : ICommandHandler<CreateCategory, CategoryView>
{
    public async Task<Result<CategoryView>> HandleAsync(CreateCategory command, CancellationToken cancellationToken)
    {
        var name = CategoryName.Create(command.Name);

        if (!name.IsSuccess)
        {
            return Handling.Fail<CategoryView>(name.Error);
        }

        var category = Category.Create(ids.NewCategoryId(), name.Value, clock);
        categories.Add(category);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(CategoryView.From(category, categories.TagOf(category)));
    }
}

internal sealed class RenameCategoryHandler(ICategoryRepository categories, IUnitOfWork unitOfWork, TimeProvider clock) : ICommandHandler<RenameCategory, CategoryView>
{
    public async Task<Result<CategoryView>> HandleAsync(RenameCategory command, CancellationToken cancellationToken)
    {
        if (Handling.RequireVersion(command.IfMatch) is { } missing)
        {
            return Result.Failure<CategoryView>(missing);
        }

        var id = CategoryId.Create(command.CategoryId);
        var name = CategoryName.Create(command.Name);

        if (!id.IsSuccess)
        {
            return Handling.Fail<CategoryView>(id.Error);
        }

        if (!name.IsSuccess)
        {
            return Handling.Fail<CategoryView>(name.Error);
        }

        var found = await categories.FindAsync(id.Value, cancellationToken);

        if (found is null)
        {
            return Result.Failure<CategoryView>(new NotFound("category"));
        }

        if (Handling.MatchVersion(command.IfMatch!, found.Tag) is { } stale)
        {
            return Result.Failure<CategoryView>(stale);
        }

        found.Entity.Rename(name.Value, clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(CategoryView.From(found.Entity, categories.TagOf(found.Entity)));
    }
}

internal sealed class DiscontinueCategoryHandler(ICategoryRepository categories, IUnitOfWork unitOfWork, TimeProvider clock) : ICommandHandler<DiscontinueCategory, CategoryView>
{
    public async Task<Result<CategoryView>> HandleAsync(DiscontinueCategory command, CancellationToken cancellationToken)
    {
        if (Handling.RequireVersion(command.IfMatch) is { } missing)
        {
            return Result.Failure<CategoryView>(missing);
        }

        var id = CategoryId.Create(command.CategoryId);

        if (!id.IsSuccess)
        {
            return Handling.Fail<CategoryView>(id.Error);
        }

        var found = await categories.FindAsync(id.Value, cancellationToken);

        if (found is null)
        {
            return Result.Failure<CategoryView>(new NotFound("category"));
        }

        if (Handling.MatchVersion(command.IfMatch!, found.Tag) is { } stale)
        {
            return Result.Failure<CategoryView>(stale);
        }

        found.Entity.Discontinue(clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(CategoryView.From(found.Entity, categories.TagOf(found.Entity)));
    }
}

internal sealed class GetCategoryHandler(ICategoryReadStore store) : IQueryHandler<GetCategory, CategoryView>
{
    public async Task<Result<CategoryView>> HandleAsync(GetCategory query, CancellationToken cancellationToken)
    {
        var id = CategoryId.Create(query.CategoryId);

        if (!id.IsSuccess)
        {
            return Handling.Fail<CategoryView>(id.Error);
        }

        var view = await store.GetAsync(id.Value, cancellationToken);

        return view is null ? Result.Failure<CategoryView>(new NotFound("category")) : Result.Success(view);
    }
}

internal sealed class ListCategoriesHandler(ICategoryReadStore store) : IQueryHandler<ListCategories, Page<CategoryView>>
{
    public async Task<Result<Page<CategoryView>>> HandleAsync(ListCategories query, CancellationToken cancellationToken)
    {
        var request = PageRequest.Parse(query.PageSize, query.Cursor, ListCategories.SortName);

        if (!request.IsSuccess)
        {
            return Result.Failure<Page<CategoryView>>(request.Error);
        }

        var rows = await store.ListAsync(request.Value.After, request.Value.Size + 1, cancellationToken);

        return Result.Success(Pages.Build(rows, request.Value.Size, ListCategories.SortName, row => new KeysetPosition(row.Name, row.Id)));
    }
}
