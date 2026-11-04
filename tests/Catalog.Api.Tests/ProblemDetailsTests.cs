using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Catalog.Api.Tests.Support;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// Every error of the API is an RFC 9457 document with the members of the contract (plan section 8.6, ADR-0028, REQ-API-005): the address of the entry of the error catalog, the title, the status, a detail that is
/// safe to show, the path of the request, and the trace identifier. The snapshots of the shapes arrive with the Verify tests of WP1.10, and these tests hold the behavior.
/// </summary>
public sealed partial class ProblemDetailsTests(SecuredApiFixture api) : IClassFixture<SecuredApiFixture>
{
    private const string CatalogAddress = "https://rostomohannessian.github.io/full-ci-cd-pipeline/reference/error-catalog.html";

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TraceIdPattern();

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    public async Task A_validation_problem_lists_the_messages_for_each_field_in_the_order_that_they_were_found()
    {
        using var response = await api.SendAsync(HttpMethod.Get, "/test/validation");
        var problem = await response.JsonAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        problem.GetProperty("type").GetString().Should().Be($"{CatalogAddress}#validation-failed");
        problem.GetProperty("title").GetString().Should().Be("One or more validation errors occurred.");
        problem.GetProperty("status").GetInt32().Should().Be(400);
        problem.GetProperty("instance").GetString().Should().Be("/test/validation");
        problem.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();

        var errors = problem.GetProperty("errors");
        errors.EnumerateObject().Select(field => field.Name).Should().Equal("price.amount", "name");
        errors.GetProperty("price.amount").EnumerateArray().Select(message => message.GetString()).Should().Equal("The amount must have at most 2 decimal places for USD.");
        errors.GetProperty("name").EnumerateArray().Select(message => message.GetString()).Should().Equal("The name is required.", "The name must have between 1 and 200 characters.");
    }

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    public async Task Every_error_carries_a_trace_identifier_of_32_hexadecimal_characters_and_two_requests_get_two()
    {
        using var first = await api.SendAsync(HttpMethod.Get, "/test/validation");
        using var second = await api.SendAsync(HttpMethod.Get, "/test/validation");
        var firstId = (await first.JsonAsync()).GetProperty("traceId").GetString()!;
        var secondId = (await second.JsonAsync()).GetProperty("traceId").GetString()!;

        TraceIdPattern().IsMatch(firstId).Should().BeTrue();
        TraceIdPattern().IsMatch(secondId).Should().BeTrue();
        firstId.Should().NotBe(new string('0', 32)).And.NotBe(secondId);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    public async Task A_route_that_does_not_exist_is_a_not_found_problem_with_the_path()
    {
        using var response = await api.SendAsync(HttpMethod.Get, "/api/v1/nothing-here", api.Token());
        var problem = await response.JsonAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        problem.ProblemSlug().Should().Be("not-found");
        problem.GetProperty("instance").GetString().Should().Be("/api/v1/nothing-here");
    }

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    public async Task A_method_that_a_route_does_not_serve_is_a_problem_of_the_type_about_blank()
    {
        using var response = await api.SendAsync(HttpMethod.Delete, "/test/read", api.Token());
        var problem = await response.JsonAsync();

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        problem.GetProperty("type").GetString().Should().Be("about:blank");
        problem.GetProperty("title").GetString().Should().Be("Method Not Allowed");
        problem.GetProperty("status").GetInt32().Should().Be(405);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    public async Task An_unauthenticated_request_gets_an_unauthorized_problem_with_the_title_of_the_catalog()
    {
        using var response = await api.SendAsync(HttpMethod.Get, "/test/read");
        var problem = await response.JsonAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        problem.GetProperty("type").GetString().Should().Be($"{CatalogAddress}#unauthorized");
        problem.GetProperty("title").GetString().Should().Be("Authentication is required.");
        problem.GetProperty("instance").GetString().Should().Be("/test/read");
    }

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    [Trait("Requirement", "REQ-SEC-006")]
    public async Task An_unhandled_exception_is_an_internal_error_that_never_shows_its_text_and_the_log_keeps_it_with_the_trace_identifier()
    {
        using var response = await api.SendAsync(HttpMethod.Get, "/test/boom", api.Token());
        var text = await response.TextAsync();
        var traceId = System.Text.Json.JsonDocument.Parse(text).RootElement.GetProperty("traceId").GetString()!;

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        text.Should().Contain("#internal-error").And.NotContain(TestEndpoints.ThrownSecret).And.NotContain("InvalidOperationException");
        api.Host.Log.Lines.Should().Contain(line => line.Contains(traceId, StringComparison.Ordinal) && line.Contains("unhandled exception", StringComparison.OrdinalIgnoreCase));
        api.Host.Log.Lines.Should().Contain(line => line.Contains(TestEndpoints.ThrownSecret, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    [Trait("Requirement", "REQ-CACHE-003")]
    public async Task A_database_failure_anywhere_in_the_chain_is_a_service_unavailable_problem_with_retry_after_and_no_driver_text()
    {
        using var response = await api.SendAsync(HttpMethod.Get, "/test/database-down", api.Token());
        var text = await response.TextAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(5));
        text.Should().Contain("#service-unavailable").And.NotContain("Pw-Secret").And.NotContain("Login failed");
    }
}
