using Catalog.Api.Configuration;
using Catalog.Api.Http;
using Catalog.Application.Categories;
using Catalog.Application.Paging;

namespace Catalog.Api.Mapping;

/// <summary>Reads a request to a category operation into the command or the query of its use case. A request that the transport cannot read becomes the response that refuses it.</summary>
internal static class CategoryInput
{
    /// <summary>Reads <c>createCategory</c>: the idempotency key and the body.</summary>
    /// <param name="http">The context of the request.</param>
    /// <param name="limits">The limits of the API.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    public static async Task<Outcome<CreateCategory>> CreateAsync(HttpContext http, LimitsOptions limits, CancellationToken cancellationToken)
    {
        var key = ConditionalHeaders.ReadIdempotencyKey(http.Request);

        if (!key.IsSuccess)
        {
            return Outcome.Fail<CreateCategory>(key.Failure);
        }

        var body = await JsonBody.ReadAsync(http.Request, ApiJsonContext.Default.CategoryCreate, limits, cancellationToken);

        return body.IsSuccess ? Outcome.Ok(new CreateCategory(body.Value.Name, key.Value.Key)) : Outcome.Fail<CreateCategory>(body.Failure);
    }

    /// <summary>Reads <c>updateCategory</c>: the identifier, the version, and the body.</summary>
    /// <param name="http">The context of the request.</param>
    /// <param name="categoryId">The identifier in the path.</param>
    /// <param name="limits">The limits of the API.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    public static async Task<Outcome<RenameCategory>> RenameAsync(HttpContext http, string? categoryId, LimitsOptions limits, CancellationToken cancellationToken)
    {
        if (!TransportInput.TryReadId("categoryId", categoryId, out var id, out var invalidId))
        {
            return Outcome.Fail<RenameCategory>(invalidId);
        }

        var tag = ConditionalHeaders.ReadIfMatch(http.Request);

        if (!tag.IsSuccess)
        {
            return Outcome.Fail<RenameCategory>(tag.Failure);
        }

        var body = await JsonBody.ReadAsync(http.Request, ApiJsonContext.Default.CategoryUpdate, limits, cancellationToken);

        return body.IsSuccess ? Outcome.Ok(new RenameCategory(id, body.Value.Name, tag.Value.Tag)) : Outcome.Fail<RenameCategory>(body.Failure);
    }

    /// <summary>Reads <c>discontinueCategory</c>: the identifier and the version.</summary>
    /// <param name="http">The context of the request.</param>
    /// <param name="categoryId">The identifier in the path.</param>
    public static Outcome<DiscontinueCategory> Discontinue(HttpContext http, string? categoryId)
    {
        if (!TransportInput.TryReadId("categoryId", categoryId, out var id, out var invalidId))
        {
            return Outcome.Fail<DiscontinueCategory>(invalidId);
        }

        var tag = ConditionalHeaders.ReadIfMatch(http.Request);

        return tag.IsSuccess ? Outcome.Ok(new DiscontinueCategory(id, tag.Value.Tag)) : Outcome.Fail<DiscontinueCategory>(tag.Failure);
    }

    /// <summary>Reads <c>getCategory</c>: the identifier.</summary>
    /// <param name="categoryId">The identifier in the path.</param>
    public static Outcome<GetCategory> Get(string? categoryId) =>
        TransportInput.TryReadId("categoryId", categoryId, out var id, out var invalidId) ? Outcome.Ok(new GetCategory(id)) : Outcome.Fail<GetCategory>(invalidId);

    /// <summary>Reads <c>listCategories</c>: the status, the page size, and the cursor.</summary>
    /// <param name="request">The request.</param>
    public static Outcome<ListCategories> List(HttpRequest request)
    {
        var parameters = new QueryParameters(request.Query);
        var size = parameters.Whole("pageSize", PageRequest.DefaultSize);
        var cursor = parameters.Text("cursor");
        var status = parameters.Choice("status", ViewMapping.StatusesByName);

        return parameters.Errors.Count > 0
            ? Outcome.Fail<ListCategories>(Problems.Validation([.. parameters.Errors]))
            : Outcome.Ok(new ListCategories(size, cursor, status));
    }
}
