using Catalog.Api.Configuration;
using Catalog.Api.Http;
using Catalog.Application.Paging;
using Catalog.Application.Products;

namespace Catalog.Api.Mapping;

/// <summary>Reads a request to a product operation into the command or the query of its use case. A request that the transport cannot read becomes the response that refuses it.</summary>
internal static class ProductInput
{
    /// <summary>Reads <c>createProduct</c>: the idempotency key, the body, and the price.</summary>
    /// <param name="http">The context of the request.</param>
    /// <param name="limits">The limits of the API.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    public static async Task<Outcome<CreateProduct>> CreateAsync(HttpContext http, LimitsOptions limits, CancellationToken cancellationToken)
    {
        var key = ConditionalHeaders.ReadIdempotencyKey(http.Request);

        if (!key.IsSuccess)
        {
            return Outcome.Fail<CreateProduct>(key.Failure);
        }

        var body = await JsonBody.ReadAsync(http.Request, ApiJsonContext.Default.ProductCreate, limits, cancellationToken);

        if (!body.IsSuccess)
        {
            return Outcome.Fail<CreateProduct>(body.Failure);
        }

        if (!TransportInput.TryReadPrice(body.Value.Price, out var amount, out var currency, out var invalidPrice))
        {
            return Outcome.Fail<CreateProduct>(invalidPrice);
        }

        return Outcome.Ok(new CreateProduct(body.Value.Sku, body.Value.Name, body.Value.Description, amount, currency, body.Value.CategoryId ?? Guid.Empty, key.Value.Key));
    }

    /// <summary>Reads <c>updateProduct</c>: the identifier, the version, the body, and the price.</summary>
    /// <param name="http">The context of the request.</param>
    /// <param name="productId">The identifier in the path.</param>
    /// <param name="limits">The limits of the API.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    public static async Task<Outcome<UpdateProduct>> UpdateAsync(HttpContext http, string? productId, LimitsOptions limits, CancellationToken cancellationToken)
    {
        if (!TransportInput.TryReadId("productId", productId, out var id, out var invalidId))
        {
            return Outcome.Fail<UpdateProduct>(invalidId);
        }

        var tag = ConditionalHeaders.ReadIfMatch(http.Request);

        if (!tag.IsSuccess)
        {
            return Outcome.Fail<UpdateProduct>(tag.Failure);
        }

        var body = await JsonBody.ReadAsync(http.Request, ApiJsonContext.Default.ProductUpdate, limits, cancellationToken);

        if (!body.IsSuccess)
        {
            return Outcome.Fail<UpdateProduct>(body.Failure);
        }

        if (!TransportInput.TryReadPrice(body.Value.Price, out var amount, out var currency, out var invalidPrice))
        {
            return Outcome.Fail<UpdateProduct>(invalidPrice);
        }

        return Outcome.Ok(new UpdateProduct(id, body.Value.Name, body.Value.Description, amount, currency, body.Value.CategoryId ?? Guid.Empty, tag.Value.Tag));
    }

    /// <summary>Reads <c>discontinueProduct</c>: the identifier and the version.</summary>
    /// <param name="http">The context of the request.</param>
    /// <param name="productId">The identifier in the path.</param>
    public static Outcome<DiscontinueProduct> Discontinue(HttpContext http, string? productId)
    {
        if (!TransportInput.TryReadId("productId", productId, out var id, out var invalidId))
        {
            return Outcome.Fail<DiscontinueProduct>(invalidId);
        }

        var tag = ConditionalHeaders.ReadIfMatch(http.Request);

        return tag.IsSuccess ? Outcome.Ok(new DiscontinueProduct(id, tag.Value.Tag)) : Outcome.Fail<DiscontinueProduct>(tag.Failure);
    }

    /// <summary>Reads <c>getProduct</c>: the identifier.</summary>
    /// <param name="productId">The identifier in the path.</param>
    public static Outcome<GetProduct> Get(string? productId) =>
        TransportInput.TryReadId("productId", productId, out var id, out var invalidId) ? Outcome.Ok(new GetProduct(id)) : Outcome.Fail<GetProduct>(invalidId);

    /// <summary>Reads <c>listProducts</c>: the filters, the sort, the page size, and the cursor.</summary>
    /// <param name="request">The request.</param>
    public static Outcome<ListProducts> List(HttpRequest request)
    {
        var parameters = new QueryParameters(request.Query);
        var categoryId = parameters.Id("categoryId");
        var status = parameters.Choice("status", ViewMapping.StatusesByName);
        var search = parameters.Text("q");
        var sort = parameters.Text("sort") ?? ListProducts.DefaultSort;
        var size = parameters.Whole("pageSize", PageRequest.DefaultSize);
        var cursor = parameters.Text("cursor");

        return parameters.Errors.Count > 0
            ? Outcome.Fail<ListProducts>(Problems.Validation([.. parameters.Errors]))
            : Outcome.Ok(new ListProducts(categoryId, status, search, sort, size, cursor));
    }
}
