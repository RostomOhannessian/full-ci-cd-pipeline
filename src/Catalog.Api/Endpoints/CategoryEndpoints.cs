using Catalog.Api.Configuration;
using Catalog.Api.Http;
using Catalog.Api.Mapping;
using Catalog.Api.Security;
using Catalog.Application.Categories;
using Catalog.Application.Messaging;
using Catalog.Application.Paging;
using Microsoft.Extensions.Options;

namespace Catalog.Api.Endpoints;

/// <summary>
/// The operations on categories (<c>listCategories</c>, <c>createCategory</c>, <c>getCategory</c>, <c>updateCategory</c>, and <c>discontinueCategory</c>). An endpoint maps the request to the command or the query of
/// its use case, runs the handler, and maps the result to the response. It holds no rule of the catalog, and it depends on the contract types and the handler interfaces and never on the model or an adapter
/// (REQ-ARC-004).
/// </summary>
internal static class CategoryEndpoints
{
    /// <summary>Maps the operations under <c>/categories</c> of the version group.</summary>
    /// <param name="api">The group of the version.</param>
    public static void MapCategoryEndpoints(this RouteGroupBuilder api)
    {
        var categories = api.MapGroup("/categories").WithTags("Categories");

        categories.MapGet(string.Empty, ListAsync).WithName("listCategories").RequireAccessOf<ListCategories>();
        categories.MapPost(string.Empty, CreateAsync).WithName("createCategory").RequireAccessOf<CreateCategory>();
        categories.MapGet("/{categoryId}", GetAsync).WithName("getCategory").RequireAccessOf<GetCategory>();
        categories.MapPut("/{categoryId}", RenameAsync).WithName("updateCategory").RequireAccessOf<RenameCategory>();
        categories.MapPost("/{categoryId}/discontinue", DiscontinueAsync).WithName("discontinueCategory").RequireAccessOf<DiscontinueCategory>();
    }

    private static async Task<IResult> ListAsync(HttpContext http, IQueryHandler<ListCategories, Page<CategoryView>> handler, CancellationToken cancellationToken)
    {
        var query = CategoryInput.List(http.Request);

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

        return Responses.Json(http, StatusCodes.Status200OK, result.Value.ToContract(), ApiJsonContext.Default.CategoryPage);
    }

    private static async Task<IResult> CreateAsync(HttpContext http, IOptions<LimitsOptions> limits, ICommandHandler<CreateCategory, CategoryView> handler, CancellationToken cancellationToken)
    {
        var command = await CategoryInput.CreateAsync(http, limits.Value, cancellationToken);

        if (!command.IsSuccess)
        {
            return command.Failure;
        }

        var result = await handler.HandleAsync(command.Value, cancellationToken);

        return result.IsSuccess
            ? Responses.Json(http, StatusCodes.Status201Created, result.Value.ToContract(), ApiJsonContext.Default.Category, result.Value.ETag, Responses.LocationOf(http.Request, "categories", result.Value.Id))
            : Problems.For(result.Error);
    }

    private static async Task<IResult> GetAsync(HttpContext http, string categoryId, IQueryHandler<GetCategory, CategoryView> handler, CancellationToken cancellationToken)
    {
        var query = CategoryInput.Get(categoryId);

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
            : Responses.Json(http, StatusCodes.Status200OK, result.Value.ToContract(), ApiJsonContext.Default.Category, result.Value.ETag);
    }

    private static async Task<IResult> RenameAsync(HttpContext http, string categoryId, IOptions<LimitsOptions> limits, ICommandHandler<RenameCategory, CategoryView> handler, CancellationToken cancellationToken)
    {
        var command = await CategoryInput.RenameAsync(http, categoryId, limits.Value, cancellationToken);

        if (!command.IsSuccess)
        {
            return command.Failure;
        }

        var result = await handler.HandleAsync(command.Value, cancellationToken);

        return result.IsSuccess
            ? Responses.Json(http, StatusCodes.Status200OK, result.Value.ToContract(), ApiJsonContext.Default.Category, result.Value.ETag)
            : Problems.For(result.Error);
    }

    private static async Task<IResult> DiscontinueAsync(HttpContext http, string categoryId, ICommandHandler<DiscontinueCategory, CategoryView> handler, CancellationToken cancellationToken)
    {
        var command = CategoryInput.Discontinue(http, categoryId);

        if (!command.IsSuccess)
        {
            return command.Failure;
        }

        var result = await handler.HandleAsync(command.Value, cancellationToken);

        return result.IsSuccess
            ? Responses.Json(http, StatusCodes.Status200OK, result.Value.ToContract(), ApiJsonContext.Default.Category, result.Value.ETag)
            : Problems.For(result.Error);
    }
}
