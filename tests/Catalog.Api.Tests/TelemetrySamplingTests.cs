using AwesomeAssertions;
using Catalog.Api.Tests.Support;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// REQ-OBS-001: a sampling ratio of zero records no trace, and the request still has a trace identifier for the logs and the error body (plan section 8.11, ADR-0043). The listener of an activity source belongs to the
/// process and not to a host, so a host of another test that samples every trace makes the activity of this request recorded and exported by every provider that listens to the source. The test runs alone for that reason.
/// </summary>
[Collection(ExportedMetricsAlone.Name)]
public sealed class TelemetrySamplingTests
{
    private const string IssuerAddress = "http://localhost:1/realms/catalog";

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    public async Task A_ratio_of_zero_records_no_trace_that_the_host_starts_but_the_request_still_has_a_trace_identifier()
    {
        var capture = new TelemetryCapture();
        var settings = new HostSettings(IssuerAddress).With("Catalog:Telemetry:TraceSamplingRatio", "0");
        await using var host = await ApiTestHost.StartAsync(settings, TestEndpoints.Map, telemetry: capture.AddTo);

        using var response = await host.Client.GetAsync("/test/validation", TestContext.Current.CancellationToken);
        var traceId = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.GetProperty("traceId").GetString();

        traceId.Should().MatchRegex("^[0-9a-f]{32}$").And.NotBe(new string('0', 32));
        capture.Spans.Where(span => span.TraceId.ToHexString() == traceId).Should().BeEmpty("a ratio of zero samples nothing, and the trace identifier of the error is still there for the logs");
    }
}
