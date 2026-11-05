using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Catalog.Api.Tests.Support;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// REQ-OBS-003 and REQ-SEC-006: the logs that leave the host are structured records whose attributes hold the values of the message template, and no secret, no credential, no connection string, no token, and no
/// personal data is in them (plan sections 8.11 and 9, ADR-0043). The exporter of these tests is the in-memory exporter, attached after the redaction of the real host, so it receives what an exporter of the real host
/// receives. The sentinels are low-entropy on purpose, so the secret scan of the repository does not flag them, and a test searches the whole output for them.
/// </summary>
public sealed partial class TelemetryLogTests(TelemetryApiFixture api) : IClassFixture<TelemetryApiFixture>
{
    private static readonly string TokenSentinel = Sentinels.Token;

    private const string EmailSentinel = "shopper.sentinel@example.test";

    private const string ConnectionSentinel = "Server=db.sentinel.test,1433;Database=catalog;User Id=sentinel;Password=Pw-Secret-connection";

    private string Everything => api.Capture.Everything(api.Host.Services);

    private ILogger<TelemetryLogTests> Logger => api.Host.Services.GetRequiredService<ILogger<TelemetryLogTests>>();

    // The category is the class of this test, so the records of the framework with the same event identifier are not mistaken for the ones of the test.
    private LoggedRecord Mine(int eventId) => api.Capture.Logs.Single(record => record.EventId == eventId && record.Category == typeof(TelemetryLogTests).FullName);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "Product {ProductCount} was counted in {Phase}.")]
    private static partial void Counted(ILogger logger, int productCount, string phase);

    [LoggerMessage(EventId = 11, Level = LogLevel.Warning, Message = "The call carried {Password}, {AccessToken}, {ConnectionString}, and {Authorization}.")]
    private static partial void Carried(ILogger logger, string password, string accessToken, string connectionString, string authorization);

    [LoggerMessage(EventId = 12, Level = LogLevel.Warning, Message = "The call failed with {Detail}, {Reason}, and {Note}.")]
    private static partial void CallFailed(ILogger logger, string detail, string reason, string note);

    [LoggerMessage(EventId = 13, Level = LogLevel.Error, Message = "The save failed.")]
    private static partial void SaveFailed(ILogger logger, Exception exception);

    [Fact]
    [Trait("Requirement", "REQ-OBS-003")]
    public void A_log_record_is_exported_as_a_template_and_a_dictionary_of_the_values_that_filled_it()
    {
        Counted(Logger, 3, "warm-up");

        var record = Mine(7);
        record.Category.Should().Be(typeof(TelemetryLogTests).FullName);
        record.Level.Should().Be(LogLevel.Information);
        record.Body.Should().Be("Product {ProductCount} was counted in {Phase}.");
        record.FormattedMessage.Should().BeNull("the attributes hold every value, and the formatted text would repeat them unchecked");
        record.Attributes.Should().Contain("ProductCount", "3").And.Contain("Phase", "warm-up");
        record.HasException.Should().BeFalse();
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-003")]
    [Trait("Requirement", "REQ-SEC-006")]
    public void An_attribute_with_the_name_of_a_secret_is_replaced_whatever_its_value_is()
    {
        Carried(Logger, "Pw-Secret-word", "plain", "plain", "plain");

        var record = Mine(11);
        record.Attributes.Should().Contain("Password", "[redacted]").And.Contain("AccessToken", "[redacted]").And.Contain("ConnectionString", "[redacted]").And.Contain("Authorization", "[redacted]");
        Everything.Should().NotContain("Pw-Secret-word");
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-003")]
    [Trait("Requirement", "REQ-SEC-006")]
    public void A_value_with_the_shape_of_a_token_a_connection_string_or_an_address_is_replaced_whatever_its_name_is()
    {
        CallFailed(Logger, $"expired {TokenSentinel}", ConnectionSentinel, $"ask {EmailSentinel}");

        var record = Mine(12);
        record.Attributes.Should().Contain("Detail", "[redacted]").And.Contain("Reason", "[redacted]").And.Contain("Note", "[redacted]");
        Everything.Should().NotContain(TokenSentinel).And.NotContain("Pw-Secret-connection").And.NotContain("sentinel.test").And.NotContain(EmailSentinel);
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-003")]
    [Trait("Requirement", "REQ-SEC-006")]
    public void The_text_of_an_exception_never_leaves_the_host_but_its_type_and_frames_do()
    {
        try
        {
            throw new InvalidOperationException($"{ConnectionSentinel} {TokenSentinel}");
        }
        catch (InvalidOperationException exception)
        {
            SaveFailed(Logger, exception);
        }

        var record = Mine(13);
        record.HasException.Should().BeFalse("the exception object is removed, because its text can hold a value");
        record.Attributes["exception.type"].Should().Be("System.InvalidOperationException");
        record.Attributes["exception.stacktrace"].Should().Contain(nameof(The_text_of_an_exception_never_leaves_the_host_but_its_type_and_frames_do));
        Everything.Should().NotContain("Pw-Secret-connection").And.NotContain(TokenSentinel).And.NotContain("sentinel.test");
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-003")]
    [Trait("Requirement", "REQ-SEC-006")]
    public async Task A_fault_of_a_request_is_logged_with_the_trace_of_the_request_and_without_the_text_of_the_exception()
    {
        var trace = ApiCalls.NewTraceId();

        using var response = await api.SendAsync(HttpMethod.Get, "/test/boom", null, ApiCalls.WithTrace(trace));
        var problem = await response.JsonAsync();

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        problem.GetProperty("traceId").GetString().Should().Be(trace, "the identifier in the answer is the trace of the request");
        var fault = api.Capture.Logs.Single(record => record.TraceId == trace && record.Category == "Catalog.Api.Http.ApiErrorHandler");
        fault.Level.Should().Be(LogLevel.Error);
        fault.Attributes["exception.type"].Should().Be("System.InvalidOperationException");
        fault.Attributes["TraceId"].Should().Be(trace);
        api.Capture.Everything(api.Host.Services).Should().NotContain(TestEndpoints.ThrownSecret);
    }

    [Fact]
    [Trait("Requirement", "REQ-SEC-006")]
    public async Task A_database_fault_is_logged_by_its_type_and_the_text_of_the_driver_stays_out_of_every_signal()
    {
        var trace = ApiCalls.NewTraceId();

        using var response = await api.SendAsync(HttpMethod.Get, "/test/database-down", null, ApiCalls.WithTrace(trace));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var warning = api.Capture.Logs.Single(record => record.TraceId == trace && record.Level == LogLevel.Warning);
        warning.Attributes["ExceptionType"].Should().Be(nameof(FakeDbException));
        api.Capture.Everything(api.Host.Services).Should().NotContain(TestEndpoints.DriverSecret).And.NotContain(HostSettings.DatabasePassword);
    }

    [Fact]
    [Trait("Requirement", "REQ-SEC-006")]
    public async Task No_token_no_credential_and_no_personal_value_reaches_a_log_a_span_or_a_metric_after_real_requests()
    {
        var token = api.Token(new TokenSpec { Scopes = ["catalog.read"], Roles = ["catalog-reader"] });

        foreach (var path in new[] { "/test/read", "/test/whoami", "/api/v1/nothing-here", "/test/boom" })
        {
            using var response = await api.SendAsync(HttpMethod.Get, path, token, request => request.Headers.TryAddWithoutValidation(RemoteAddressStartupFilter.Header, "203.0.113.77"));
        }

        using var anonymous = await api.SendAsync(HttpMethod.Get, "/test/read", "not.a.token");

        var everything = api.Capture.Everything(api.Host.Services);

        everything.Should().NotContain(token).And.NotContain("not.a.token").And.NotContain(HostSettings.DatabasePassword).And.NotContain(HostSettings.ValkeyPassword).And.NotContain("203.0.113.77");
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-003")]
    public async Task The_redacted_log_of_a_fault_has_the_shape_that_the_snapshot_holds()
    {
        var trace = ApiCalls.NewTraceId();
        using var response = await api.SendAsync(HttpMethod.Get, "/test/boom", null, ApiCalls.WithTrace(trace));

        var records = api.Capture.Logs.Where(record => record.TraceId == trace && record.Level >= LogLevel.Warning).OrderBy(record => record.Category, StringComparer.Ordinal).ThenBy(record => record.EventId).ToList();
        var shape = new JsonArray([.. records.Select(record => (JsonNode?)new JsonObject
        {
            ["category"] = record.Category,
            ["level"] = record.Level.ToString(),
            ["eventId"] = record.EventId,
            ["body"] = record.Body,
            ["formattedMessage"] = record.FormattedMessage,
            ["traceIdIsTheRequest"] = record.TraceId == trace,
            ["attributes"] = new JsonObject([.. record.Attributes.OrderBy(attribute => attribute.Key, StringComparer.Ordinal).Select(attribute => KeyValuePair.Create(
                attribute.Key,
                (JsonNode?)JsonValue.Create(attribute.Key switch
                {
                    "TraceId" => "<trace>",
                    "exception.stacktrace" => "<frames>",
                    _ => attribute.Value,
                })))]),
        })]);

        await Snapshot.MatchAsync(shape.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
