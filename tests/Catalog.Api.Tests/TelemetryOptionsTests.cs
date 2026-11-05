using System.Text;
using AwesomeAssertions;
using Catalog.Api.Configuration;
using Catalog.Api.Tests.Support;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// REQ-OBS-001, REQ-OBS-002, and REQ-OBS-003: the telemetry settings are checked when the host starts and never repeat a value, because an OTLP address can hold a credential and the headers hold one, the exporter sends
/// the three signals over OTLP to the address of the standard variables and sends nothing when it is off, and the redaction has run before the export (plan section 8.11, ADR-0043). The collector of these tests is a listener
/// on the loopback address, so the proof is what a host puts on the network and not what a library says that it does.
/// </summary>
public sealed class TelemetryOptionsTests
{
    private const string IssuerAddress = "http://localhost:1/realms/catalog";

    private static ValidateOptionsResult Validate(TelemetryOptions options, params (string Key, string? Value)[] variables) =>
        new TelemetryOptionsValidator(new ConfigurationBuilder().AddInMemoryCollection(variables.ToDictionary(variable => variable.Key, variable => variable.Value)).Build()).Validate(null, options);

    [Fact]
    [Trait("Requirement", "REQ-OBS-002")]
    public void The_defaults_send_over_otlp_with_the_service_name_of_the_api_and_record_every_trace_and_are_valid()
    {
        var options = new TelemetryOptions();

        options.Exporter.Should().Be(TelemetryExporter.Otlp);
        options.ServiceName.Should().Be("catalog-api");
        options.TraceSamplingRatio.Should().Be(1);
        TelemetryOptions.SectionName.Should().Be("Catalog:Telemetry");
        Validate(options).Succeeded.Should().BeTrue();
        options.ToString().Should().Be(nameof(TelemetryOptions));
    }

    [Theory]
    [Trait("Requirement", "REQ-OBS-002")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("catalog api")]
    [InlineData("catalog/api")]
    [InlineData("service-with-a-name-that-is-far-too-long-service-with-a-name-that-is-far-too-long-service-with-a-name-that-is-far-too-long")]
    public void The_service_name_must_be_one_short_word_of_letters_digits_dots_hyphens_and_underscores(string name) =>
        Validate(new TelemetryOptions { ServiceName = name }).FailureMessage.Should().Contain("ServiceName");

    [Theory]
    [Trait("Requirement", "REQ-OBS-002")]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void The_sampling_ratio_is_between_zero_and_one(double ratio) =>
        Validate(new TelemetryOptions { TraceSamplingRatio = ratio }).FailureMessage.Should().Contain("TraceSamplingRatio");

    [Fact]
    [Trait("Requirement", "REQ-OBS-002")]
    public void An_exporter_that_is_not_defined_is_refused()
    {
        Validate(new TelemetryOptions { Exporter = (TelemetryExporter)7 }).FailureMessage.Should().Contain("Exporter");
        Validate(new TelemetryOptions { Exporter = TelemetryExporter.None, ServiceName = "catalog-api", TraceSamplingRatio = 0 }).Succeeded.Should().BeTrue();
    }

    [Theory]
    [Trait("Requirement", "REQ-OBS-002")]
    [Trait("Requirement", "REQ-SEC-006")]
    [InlineData("OTEL_EXPORTER_OTLP_ENDPOINT")]
    [InlineData("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT")]
    [InlineData("OTEL_EXPORTER_OTLP_METRICS_ENDPOINT")]
    [InlineData("OTEL_EXPORTER_OTLP_LOGS_ENDPOINT")]
    public void An_endpoint_variable_must_be_an_absolute_http_address_and_the_problem_never_repeats_it(string variable)
    {
        var result = Validate(new TelemetryOptions(), (variable, Sentinels.AddressWithCredentials));

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(variable).And.NotContain("Pw-Secret-endpoint").And.NotContain("collector.example.test");
        Validate(new TelemetryOptions(), (variable, "http://collector.example.test:4318")).Succeeded.Should().BeTrue();
        Validate(new TelemetryOptions(), (variable, "https://collector.example.test")).Succeeded.Should().BeTrue();
    }

    [Theory]
    [Trait("Requirement", "REQ-OBS-002")]
    [InlineData("OTEL_EXPORTER_OTLP_PROTOCOL")]
    [InlineData("OTEL_EXPORTER_OTLP_TRACES_PROTOCOL")]
    [InlineData("OTEL_EXPORTER_OTLP_METRICS_PROTOCOL")]
    [InlineData("OTEL_EXPORTER_OTLP_LOGS_PROTOCOL")]
    public void A_protocol_variable_must_be_grpc_or_http_protobuf(string variable)
    {
        Validate(new TelemetryOptions(), (variable, "http/json")).FailureMessage.Should().Contain(variable);
        Validate(new TelemetryOptions(), (variable, "grpc")).Succeeded.Should().BeTrue();
        Validate(new TelemetryOptions(), (variable, "http/protobuf")).Succeeded.Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-002")]
    public void When_the_exporter_is_off_the_variables_are_not_checked_because_nothing_reads_them() =>
        Validate(new TelemetryOptions { Exporter = TelemetryExporter.None }, ("OTEL_EXPORTER_OTLP_ENDPOINT", "not an address")).Succeeded.Should().BeTrue();

    [Fact]
    [Trait("Requirement", "REQ-OBS-002")]
    public async Task A_host_with_a_wrong_endpoint_does_not_start_and_the_error_names_the_variable_and_not_the_value()
    {
        var settings = new HostSettings(IssuerAddress).With("Catalog:Telemetry:Exporter", "Otlp").With("OTEL_EXPORTER_OTLP_ENDPOINT", Sentinels.AddressWithCredentials);

        var act = () => ApiTestHost.StartAsync(settings);

        var failure = (await act.Should().ThrowAsync<OptionsValidationException>()).Which;
        failure.Message.Should().Contain("OTEL_EXPORTER_OTLP_ENDPOINT").And.NotContain("Pw-Secret-endpoint");
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    [Trait("Requirement", "REQ-OBS-002")]
    [Trait("Requirement", "REQ-OBS-003")]
    public async Task The_host_sends_traces_metrics_and_logs_over_otlp_to_the_address_of_the_standard_variable()
    {
        await using var collector = await OtlpListener.StartAsync();
        var settings = new HostSettings(IssuerAddress)
            .With("Catalog:Telemetry:Exporter", "Otlp")
            .With("Catalog:Telemetry:ServiceName", "catalog-api-sentinel")
            .With("OTEL_EXPORTER_OTLP_ENDPOINT", collector.Address.ToString().TrimEnd('/'))
            .With("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf");
        await using var host = await ApiTestHost.StartAsync(settings, TestEndpoints.Map);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/read");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {Sentinels.Token}");
        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        host.Services.GetRequiredService<TracerProvider>().ForceFlush(10_000).Should().BeTrue();
        host.Services.GetRequiredService<MeterProvider>().ForceFlush(10_000).Should().BeTrue();
        host.Services.GetRequiredService<LoggerProvider>().ForceFlush(10_000).Should().BeTrue();

        var received = collector.Received;
        received.Select(export => export.Path).Distinct().Should().Contain(["/v1/traces", "/v1/metrics", "/v1/logs"]);
        received.Should().OnlyContain(export => export.ContentType == "application/x-protobuf");
        var text = string.Concat(received.Select(export => Encoding.Latin1.GetString(export.Body)));
        text.Should().Contain("catalog-api-sentinel").And.Contain("service.name");
        text.Should().NotContain(Sentinels.Token).And.NotContain(HostSettings.DatabasePassword).And.NotContain(HostSettings.ValkeyPassword);
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-002")]
    public async Task A_host_whose_exporter_is_off_sends_nothing_even_when_the_variable_names_a_collector()
    {
        await using var collector = await OtlpListener.StartAsync();
        var settings = new HostSettings(IssuerAddress)
            .With("Catalog:Telemetry:Exporter", "None")
            .With("OTEL_EXPORTER_OTLP_ENDPOINT", collector.Address.ToString().TrimEnd('/'))
            .With("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf");
        await using var host = await ApiTestHost.StartAsync(settings, TestEndpoints.Map);

        using var response = await host.Client.GetAsync("/test/read", TestContext.Current.CancellationToken);
        host.Services.GetRequiredService<TracerProvider>().ForceFlush(10_000);
        host.Services.GetRequiredService<MeterProvider>().ForceFlush(10_000);
        host.Services.GetRequiredService<LoggerProvider>().ForceFlush(10_000);

        collector.Received.Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-002")]
    public async Task A_host_whose_collector_is_not_there_answers_every_request_as_before()
    {
        // Nobody listens on port 1. The exporters batch off the request path, so no request waits for the collector, and a failed export is dropped.
        var settings = new HostSettings(IssuerAddress)
            .With("Catalog:Telemetry:Exporter", "Otlp")
            .With("OTEL_EXPORTER_OTLP_ENDPOINT", "http://127.0.0.1:1")
            .With("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf")
            .With("OTEL_EXPORTER_OTLP_TIMEOUT", "500");
        await using var host = await ApiTestHost.StartAsync(settings, TestEndpoints.Map);

        for (var attempt = 0; attempt < 20; attempt++)
        {
            using var response = await host.Client.GetAsync("/test/validation", TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        }
    }
}
