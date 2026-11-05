using Catalog.Api.Configuration;
using Catalog.Api.Documentation;
using Catalog.Api.Http;
using Catalog.Api.Mapping;
using Catalog.Api.Security;
using Catalog.Application.Messaging;
using Catalog.Application.Paging;
using Catalog.Application.Products;
using Microsoft.Extensions.Options;

namespace Catalog.Api.Endpoints;

/// <summary>
/// The operations on products (<c>listProducts</c>, <c>createProduct</c>, <c>getProduct</c>, <c>updateProduct</c>, and <c>discontinueProduct</c>). Like the category endpoints, each one maps the request to the command or the
/// query of its use case, runs the handler, and maps the result to the response, with no rule of the catalog (REQ-ARC-004).
/// </summary>
internal static class ProductEndpoints
{
    private const string Tag = "Products";

    /// <summary>Maps the operations under <c>/products</c> of the version group.</summary>
    /// <param name="api">The group of the version.</param>
    public static void MapProductEndpoints(this RouteGroupBuilder api)
    {
        var products = api.MapGroup("/products").WithTags(Tag);

        products.MapGet(string.Empty, ListAsync).WithName("listProducts").RequireAccessOf<ListProducts>()
            .WithContract(new ContractOperation(Tag, "ProductPage", Parameters: ["CategoryFilter", "ProductStatusFilter", "SearchText", "Sort", "PageSize", "Cursor"]));
        products.MapPost(string.Empty, CreateAsync).WithName("createProduct").RequireAccessOf<CreateProduct>()
            .WithContract(new ContractOperation(Tag, "Product", StatusCodes.Status201Created, Parameters: ["IdempotencyKey"], RequestSchema: "ProductCreate", ConflictResponse: "ProductCreateConflict"));
        products.MapGet("/{productId}", GetAsync).WithName("getProduct").RequireAccessOf<GetProduct>()
            .WithContract(new ContractOperation(Tag, "Product", PathParameter: "ProductId", Parameters: ["IfNoneMatch"]));
        products.MapPut("/{productId}", UpdateAsync).WithName("updateProduct").RequireAccessOf<UpdateProduct>()
            .WithContract(new ContractOperation(Tag, "Product", PathParameter: "ProductId", Parameters: ["IfMatch"], RequestSchema: "ProductUpdate", ConflictResponse: "ProductDiscontinuedConflict"));
        products.MapPost("/{productId}/discontinue", DiscontinueAsync).WithName("discontinueProduct").RequireAccessOf<DiscontinueProduct>()
            .WithContract(new ContractOperation(Tag, "Product", PathParameter: "ProductId", Parameters: ["IfMatch"]));
    }

    private static async Task<IResult> ListAsync(HttpContext http, IQueryHandler<ListProducts, Page<ProductView>> handler, CancellationToken cancellationToken)
    {
        var query = ProductInput.List(http.Request);

        if (!query.IsSuccess)
        {
            return query.Failure;
        }

        var result = await handler.HandleAsync(query.Value, cancellationToken);

        if (!result.IsSuccess)
        {
            return Problems.For(result.Error);
        }

        Responses.AddNextLink(http, result.Value.NextCursor);

        return Responses.Json(http, StatusCodes.Status200OK, result.Value.ToContract(), ApiJsonContext.Default.ProductPage);
    }

    private static async Task<IResult> CreateAsync(HttpContext http, IOptions<LimitsOptions> limits, ICommandHandler<CreateProduct, ProductView> handler, CancellationToken cancellationToken)
    {
        var command = await ProductInput.CreateAsync(http, limits.Value, cancellationToken);

        if (!command.IsSuccess)
        {
            return command.Failure;
        }

        var result = await handler.HandleAsync(command.Value, cancellationToken);

        return result.IsSuccess
            ? Responses.Json(http, StatusCodes.Status201Created, result.Value.ToContract(), ApiJsonContext.Default.Product, result.Value.ETag, Responses.LocationOf(http.Request, "products", result.Value.Id))
            : Problems.For(result.Error);
    }

    private static async Task<IResult> GetAsync(HttpContext http, string productId, IQueryHandler<GetProduct, ProductView> handler, CancellationToken cancellationToken)
    {
        var query = ProductInput.Get(productId);

        if (!query.IsSuccess)
        {
            return query.Failure;
        }

        var condition = ConditionalHeaders.ReadIfNoneMatch(http.Request);

        if (!condition.IsSuccess)
        {
            return condition.Failure;
        }

        var result = await handler.HandleAsync(query.Value, cancellationToken);

        if (!result.IsSuccess)
        {
            return Problems.For(result.Error);
        }

        return condition.Value.Tag?.Value == result.Value.ETag
            ? Responses.NotModified(http, result.Value.ETag)
            : Responses.Json(http, StatusCodes.Status200OK, result.Value.ToContract(), ApiJsonContext.Default.Product, result.Value.ETag);
    }

    private static async Task<IResult> UpdateAsync(HttpContext http, string productId, IOptions<LimitsOptions> limits, ICommandHandler<UpdateProduct, ProductView> handler, CancellationToken cancellationToken)
    {
        var command = await ProductInput.UpdateAsync(http, productId, limits.Value, cancellationToken);

        if (!command.IsSuccess)
        {
            return command.Failure;
        }

        var result = await handler.HandleAsync(command.Value, cancellationToken);

        return result.IsSuccess
            ? Responses.Json(http, StatusCodes.Status200OK, result.Value.ToContract(), ApiJsonContext.Default.Product, result.Value.ETag)
            : Problems.For(result.Error);
    }

    private static async Task<IResult> DiscontinueAsync(HttpContext http, string productId, ICommandHandler<DiscontinueProduct, ProductView> handler, CancellationToken cancellationToken)
    {
        var command = ProductInput.Discontinue(http, productId);

        if (!command.IsSuccess)
        {
            return command.Failure;
        }

        var result = await handler.HandleAsync(command.Value, cancellationToken);

        return result.IsSuccess
            ? Responses.Json(http, StatusCodes.Status200OK, result.Value.ToContract(), ApiJsonContext.Default.Product, result.Value.ETag)
            : Problems.For(result.Error);
    }
}
