using System.Net;
using AwesomeAssertions;
using Catalog.Api.Tests.Support;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// The version segment of the path and the headers that report the versions (REQ-API-002 and REQ-API-001): every response of a versioned route lists the supported versions, a deprecated version is not claimed
/// before it exists, and a path without a version or with an unknown one is not routed to a default. These cases end before any use case runs, so they need no database.
/// </summary>
public sealed class VersioningTests(SecuredApiFixture api) : IClassFixture<SecuredApiFixture>
{
    private async Task<ApiResponse> GetAsync(string path, string? token)
    {
        using var response = await api.SendAsync(HttpMethod.Get, path, token);

        return new ApiResponse(response.StatusCode, response.Headers, response.Content.Headers.ContentType?.MediaType, await response.TextAsync());
    }

    [Fact]
    [Trait("Requirement", "REQ-API-002")]
    public async Task Every_kind_of_answer_of_a_versioned_route_lists_the_supported_versions()
    {
        var unauthenticated = await GetAsync("/api/v1/categories", null);
        var forbidden = await GetAsync("/api/v1/categories", api.Token(new TokenSpec { Scopes = [], Roles = [] }));
        var invalid = await GetAsync("/api/v1/categories/not-a-uuid", api.Token());
        var invalidQuery = await GetAsync("/api/v1/products?pageSize=0", api.Token());

        unauthenticated.Status.Should().Be(HttpStatusCode.Unauthorized);
        forbidden.Status.Should().Be(HttpStatusCode.Forbidden);
        invalid.Status.Should().Be(HttpStatusCode.BadRequest);
        invalidQuery.Status.Should().Be(HttpStatusCode.BadRequest);

        foreach (var answer in new[] { unauthenticated, forbidden, invalid, invalidQuery })
        {
            answer.Header("api-supported-versions").Should().Be("1.0", $"the {answer.Status} answer must list the versions: {answer.Body}");
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-API-002")]
    public async Task No_version_is_deprecated_so_no_deprecation_or_sunset_is_announced()
    {
        var answer = await GetAsync("/api/v1/categories/not-a-uuid", api.Token());

        answer.Header("api-deprecated-versions").Should().BeNull();
        answer.Header("Deprecation").Should().BeNull();
        answer.Header("Sunset").Should().BeNull();
    }

    [Fact]
    [Trait("Requirement", "REQ-API-002")]
    public async Task A_path_without_a_version_is_not_found_and_an_unknown_version_is_refused()
    {
        var withoutVersion = await GetAsync("/api/categories", api.Token());
        var unknownVersion = await GetAsync("/api/v2/categories", api.Token());
        var badVersion = await GetAsync("/api/vnext/categories", api.Token());

        withoutVersion.Status.Should().Be(HttpStatusCode.NotFound);
        withoutVersion.ContentType.Should().Be("application/problem+json");
        unknownVersion.Status.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
        unknownVersion.ContentType.Should().Be("application/problem+json");
        badVersion.Status.Should().Be(HttpStatusCode.NotFound);
    }
}
