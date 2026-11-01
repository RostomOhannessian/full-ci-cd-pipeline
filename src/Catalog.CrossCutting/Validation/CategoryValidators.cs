using Catalog.Application.Categories;
using Catalog.Application.Paging;
using Catalog.Domain.Categories;
using Catalog.Domain.Common;
using FluentValidation;

namespace Catalog.CrossCutting.Validation;

/// <summary>
/// The shared step of the validators of the use cases. A validator runs the factory of each value object of a request and reports the error of every one that fails, so a caller sees all the
/// invalid fields at once, and each rule is the one of the domain model and is never written a second time. The model names a field as the contract does, so the field goes into the report as it is.
/// </summary>
public static class DomainRules
{
    /// <summary>Reports the failure of a domain factory, if it failed.</summary>
    /// <typeparam name="TRequest">The type of the request that is validated.</typeparam>
    /// <typeparam name="TValue">The type of the value that the factory makes.</typeparam>
    /// <param name="context">The context of the validation.</param>
    /// <param name="result">The result of the factory.</param>
    /// <param name="parent">The path of the value that holds the checked value, such as <c>price</c>, or null.</param>
    /// <returns>True when the factory succeeded.</returns>
    public static bool Report<TRequest, TValue>(ValidationContext<TRequest> context, DomainResult<TValue> result, string? parent = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess)
        {
            return true;
        }

        var error = parent is null ? result.Error : result.Error.Within(parent);
        context.AddFailure(error.Field, error.Message);

        return false;
    }
}

/// <summary>Validates <see cref="CreateCategory"/>.</summary>
public sealed class CreateCategoryValidator : AbstractValidator<CreateCategory>
{
    /// <summary>Creates the validator.</summary>
    public CreateCategoryValidator() => RuleFor(command => command).Custom((command, context) => DomainRules.Report(context, CategoryName.Create(command.Name)));
}

/// <summary>Validates <see cref="RenameCategory"/>.</summary>
public sealed class RenameCategoryValidator : AbstractValidator<RenameCategory>
{
    /// <summary>Creates the validator.</summary>
    public RenameCategoryValidator() => RuleFor(command => command).Custom((command, context) =>
    {
        DomainRules.Report(context, CategoryId.Create(command.CategoryId));
        DomainRules.Report(context, CategoryName.Create(command.Name));
    });
}

/// <summary>Validates <see cref="DiscontinueCategory"/>.</summary>
public sealed class DiscontinueCategoryValidator : AbstractValidator<DiscontinueCategory>
{
    /// <summary>Creates the validator.</summary>
    public DiscontinueCategoryValidator() => RuleFor(command => command).Custom((command, context) => DomainRules.Report(context, CategoryId.Create(command.CategoryId)));
}

/// <summary>Validates <see cref="GetCategory"/>.</summary>
public sealed class GetCategoryValidator : AbstractValidator<GetCategory>
{
    /// <summary>Creates the validator.</summary>
    public GetCategoryValidator() => RuleFor(query => query).Custom((query, context) => DomainRules.Report(context, CategoryId.Create(query.CategoryId)));
}

/// <summary>Validates <see cref="ListCategories"/>: the page size and the cursor.</summary>
public sealed class ListCategoriesValidator : AbstractValidator<ListCategories>
{
    /// <summary>Creates the validator.</summary>
    public ListCategoriesValidator() => RuleFor(query => query).Custom((query, context) =>
    {
        var request = PageRequest.Parse(query.PageSize, query.Cursor, ListCategories.SortName);

        if (request.Error is Application.Results.ValidationFailed failed)
        {
            foreach (var error in failed.Errors)
            {
                context.AddFailure(error.Field, error.Message);
            }
        }
    });
}
