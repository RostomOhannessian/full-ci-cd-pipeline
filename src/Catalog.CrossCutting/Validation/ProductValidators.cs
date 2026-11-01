using Catalog.Application.Paging;
using Catalog.Application.Products;
using Catalog.Application.Results;
using Catalog.Domain.Categories;
using Catalog.Domain.Pricing;
using Catalog.Domain.Products;
using FluentValidation;

namespace Catalog.CrossCutting.Validation;

/// <summary>Validates <see cref="CreateProduct"/>: every value object of the request, so all invalid fields come back at once.</summary>
public sealed class CreateProductValidator : AbstractValidator<CreateProduct>
{
    /// <summary>Creates the validator.</summary>
    public CreateProductValidator() => RuleFor(command => command).Custom((command, context) =>
    {
        DomainRules.Report(context, Sku.Create(command.Sku));
        DomainRules.Report(context, ProductName.Create(command.Name));
        DomainRules.Report(context, Description.Create(command.Description));
        ProductRules.ReportPrice(context, command.Amount, command.Currency);
        DomainRules.Report(context, CategoryId.Create(command.CategoryId));
    });
}

/// <summary>Validates <see cref="UpdateProduct"/>: the identifier and every value of the replacement.</summary>
public sealed class UpdateProductValidator : AbstractValidator<UpdateProduct>
{
    /// <summary>Creates the validator.</summary>
    public UpdateProductValidator() => RuleFor(command => command).Custom((command, context) =>
    {
        DomainRules.Report(context, ProductId.Create(command.ProductId));
        DomainRules.Report(context, ProductName.Create(command.Name));
        DomainRules.Report(context, Description.Create(command.Description));
        ProductRules.ReportPrice(context, command.Amount, command.Currency);
        DomainRules.Report(context, CategoryId.Create(command.CategoryId));
    });
}

/// <summary>Validates <see cref="DiscontinueProduct"/>.</summary>
public sealed class DiscontinueProductValidator : AbstractValidator<DiscontinueProduct>
{
    /// <summary>Creates the validator.</summary>
    public DiscontinueProductValidator() => RuleFor(command => command).Custom((command, context) => DomainRules.Report(context, ProductId.Create(command.ProductId)));
}

/// <summary>Validates <see cref="GetProduct"/>.</summary>
public sealed class GetProductValidator : AbstractValidator<GetProduct>
{
    /// <summary>Creates the validator.</summary>
    public GetProductValidator() => RuleFor(query => query).Custom((query, context) => DomainRules.Report(context, ProductId.Create(query.ProductId)));
}

/// <summary>Validates <see cref="ListProducts"/>: the filters, the sort, the page size, and the cursor, named as the query parameters of the contract.</summary>
public sealed class ListProductsValidator : AbstractValidator<ListProducts>
{
    /// <summary>Creates the validator.</summary>
    public ListProductsValidator() => RuleFor(query => query).Custom((query, context) =>
    {
        if (query.CategoryId is { } categoryId)
        {
            DomainRules.Report(context, CategoryId.Create(categoryId));
        }

        if (query.Status is { } status && !Enum.IsDefined(status))
        {
            context.AddFailure("status", "The status must be active or discontinued.");
        }

        if (query.Q is { } search && (search.Length is < 1 or > 100))
        {
            context.AddFailure("q", "The search text must have 1 to 100 characters.");
        }

        if (ListProducts.ParseSort(query.Sort) is null)
        {
            context.AddFailure("sort", "The sort must be createdAt or name.");
        }

        if (PageRequest.Parse(query.PageSize, query.Cursor, query.Sort).Error is ValidationFailed paging)
        {
            foreach (var error in paging.Errors)
            {
                context.AddFailure(error.Field, error.Message);
            }
        }
    });
}

internal static class ProductRules
{
    // The price holds two values, and the amount is only checked against the currency that came with it.
    public static void ReportPrice<TRequest>(ValidationContext<TRequest> context, decimal amount, string? currency)
    {
        var parsedCurrency = Currency.Create(currency);

        if (DomainRules.Report(context, parsedCurrency, "price"))
        {
            DomainRules.Report(context, Money.Create(amount, parsedCurrency.Value), "price");
        }
    }
}
