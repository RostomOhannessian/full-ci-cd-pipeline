using System.Diagnostics;
using AwesomeAssertions;
using Catalog.Api.Telemetry;
using Catalog.Api.Tests.Support;
using Microsoft.Extensions.Compliance.Classification;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// REQ-OBS-003 and REQ-SEC-006: the classifier decides by the name of an attribute and by the shape of its value, the redactor replaces a classified value with a marker and leaves every other value as it is, and the
/// processor of spans applies both to every tag before an exporter sees it (plan section 8.11, ADR-0043). The lists lean toward redacting too much, so each test of a sensitive case names what it protects, and each test
/// of a harmless case names the attribute that must stay readable, because a trace that redacts the route is useless.
/// </summary>
public sealed class TelemetryRedactionTests(TelemetryApiFixture api) : IClassFixture<TelemetryApiFixture>
{
    public static TheoryData<string> SecretNames =>
    [
        "password",
        "Password",
        "db.connection_string",
        "ConnectionString",
        "http.request.header.authorization",
        "http.request.header.cookie",
        "set-cookie",
        "x-api-key",
        "apiKey",
        "client_secret",
        "AccessToken",
        "refresh_token",
        "bearer",
        "private_key",
    ];

    public static TheoryData<string> PersonalNames =>
    [
        "email",
        "user.email",
        "enduser.id",
        "user_id",
        "client.address",
        "network.peer.address",
        "preferred_username",
        "PhoneNumber",
        "ip_address",
    ];

    public static TheoryData<string> HarmlessNames =>
    [
        "url.path",
        "http.route",
        "http.request.method",
        "http.response.status_code",
        "server.address",
        "db.system.name",
        "db.namespace",
        "db.query.summary",
        "catalog.use_case",
        "catalog.outcome",
        "error.type",
        "exception.type",
        "ProductCount",
        "TraceId",
        "fusioncache.cache.name",
        "catalog.outbox.handler",
    ];

    public static TheoryData<string> SecretValues =>
    [
        Sentinels.Token,
        $"expired {Sentinels.Token[..^8]}",
        "Bearer abcdefghijklmnop",
        "Basic dXNlcjpwYXNzd29yZA==",
        "Server=db.example.test,1433;Database=catalog;User Id=sa;Password=Pw-Secret-test",
        "Data Source=db.example.test;Initial Catalog=catalog;Integrated Security=true",
        "Host=db;Username=app;password=Pw-Secret-test",
        "uid=app;pwd=Pw-Secret-test",
    ];

    public static TheoryData<string> PersonalValues =>
    [
        "shopper@example.test",
        "ask first.last+tag@mail.example.test about it",
        @"C:\Users\someone\AppData\Local\ASP.NET\DataProtection-Keys",
        "/home/someone/.aspnet/DataProtection-Keys",
        "/Users/someone/Library/Application Support",
    ];

    public static TheoryData<string> HarmlessValues =>
    [
        "GET",
        "/api/v1/products/019bc119-04e8-7823-8f57-11f991fa134e",
        "/api/v{version:apiVersion}/products/{productId}",
        "Catalog.Application.Products.CreateProduct",
        "catalog_1",
        "SELECT [p].[Id] FROM [catalog].[Products] AS [p] WHERE [p].[Sku] = @sku",
        "microsoft.sql_server",
        "127.0.0.1",
        "0123456789abcdef",
    ];

    [Theory]
    [MemberData(nameof(SecretNames))]
    [Trait("Requirement", "REQ-SEC-006")]
    [Trait("Requirement", "REQ-OBS-003")]
    public void An_attribute_named_like_a_secret_is_a_secret_whatever_its_value_is(string name) =>
        TelemetryClassifier.Classify(name, "harmless").Should().Be(CatalogTaxonomy.Secret);

    [Theory]
    [MemberData(nameof(PersonalNames))]
    [Trait("Requirement", "REQ-SEC-006")]
    [Trait("Requirement", "REQ-OBS-003")]
    public void An_attribute_named_like_personal_data_is_personal_whatever_its_value_is(string name) =>
        TelemetryClassifier.Classify(name, "harmless").Should().Be(CatalogTaxonomy.Personal);

    [Theory]
    [MemberData(nameof(HarmlessNames))]
    [Trait("Requirement", "REQ-OBS-003")]
    public void An_attribute_that_a_trace_needs_stays_readable(string name) =>
        TelemetryClassifier.Classify(name, "value").Should().Be(DataClassification.None);

    [Theory]
    [MemberData(nameof(SecretValues))]
    [Trait("Requirement", "REQ-SEC-006")]
    [Trait("Requirement", "REQ-OBS-003")]
    public void A_text_with_the_shape_of_a_token_or_a_connection_string_is_a_secret_whatever_its_name_is(string value) =>
        TelemetryClassifier.Classify("detail", value).Should().Be(CatalogTaxonomy.Secret);

    [Theory]
    [MemberData(nameof(PersonalValues))]
    [Trait("Requirement", "REQ-SEC-006")]
    [Trait("Requirement", "REQ-OBS-003")]
    public void A_text_with_an_address_of_a_person_or_the_folder_of_a_person_is_personal_whatever_its_name_is(string value) =>
        TelemetryClassifier.Classify("detail", value).Should().Be(CatalogTaxonomy.Personal);

    [Theory]
    [MemberData(nameof(HarmlessValues))]
    [Trait("Requirement", "REQ-OBS-003")]
    public void A_text_that_is_a_route_a_name_or_a_statement_stays_readable(string value) =>
        TelemetryClassifier.Classify("detail", value).Should().Be(DataClassification.None);

    [Fact]
    [Trait("Requirement", "REQ-OBS-003")]
    public void A_value_that_is_not_text_is_classified_by_its_name_only()
    {
        TelemetryClassifier.Classify("count", 42).Should().Be(DataClassification.None);
        TelemetryClassifier.Classify("flag", true).Should().Be(DataClassification.None);
        TelemetryClassifier.Classify("password", 42).Should().Be(CatalogTaxonomy.Secret);
        TelemetryClassifier.Classify("detail", null).Should().Be(DataClassification.None);
        ((Action)(() => TelemetryClassifier.Classify(null!, "x"))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-003")]
    public void The_redactor_replaces_a_classified_value_with_the_marker_and_returns_every_other_value_unchanged()
    {
        var redactor = api.Host.Services.GetRequiredService<TelemetryRedactor>();
        var harmless = "GET";
        object boxed = 42;

        redactor.Apply("password", "Pw-Secret-word").Should().Be(MarkerRedactor.Marker);
        redactor.Apply("email", "shopper@example.test").Should().Be(MarkerRedactor.Marker);
        redactor.Apply("detail", "Bearer abcdefghijklmnop").Should().Be(MarkerRedactor.Marker);
        redactor.Apply("http.request.method", harmless).Should().BeSameAs(harmless);
        redactor.Apply("count", boxed).Should().BeSameAs(boxed);
        MarkerRedactor.Marker.Should().Be("[redacted]");
    }

    [Fact]
    [Trait("Requirement", "REQ-SEC-006")]
    [Trait("Requirement", "REQ-OBS-001")]
    public void The_processor_of_spans_redacts_a_sensitive_tag_before_an_exporter_sees_it_and_keeps_the_others()
    {
        var processor = new RedactingActivityProcessor(api.Host.Services.GetRequiredService<TelemetryRedactor>());
        using var source = new ActivitySource("Test.Redaction");
        using var listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate.Name == "Test.Redaction",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        using var span = source.StartActivity("request")!;
        span.SetTag("http.route", "/api/v1/products/{productId}");
        span.SetTag("http.request.header.authorization", "Bearer abcdefghijklmnop");
        span.SetTag("client.address", "203.0.113.77");
        span.SetTag("detail", "Server=db;Database=catalog;Password=Pw-Secret-test");
        span.SetTag("http.response.status_code", 200);

        processor.OnEnd(span);

        span.GetTagItem("http.route").Should().Be("/api/v1/products/{productId}");
        span.GetTagItem("http.response.status_code").Should().Be(200);
        span.GetTagItem("http.request.header.authorization").Should().Be(MarkerRedactor.Marker);
        span.GetTagItem("client.address").Should().Be(MarkerRedactor.Marker);
        span.GetTagItem("detail").Should().Be(MarkerRedactor.Marker);
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-003")]
    public void The_processors_and_the_redactor_need_their_collaborators()
    {
        ((Action)(() => _ = new TelemetryRedactor(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new RedactingActivityProcessor(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new RedactingLogProcessor(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new RedactingActivityProcessor(api.Host.Services.GetRequiredService<TelemetryRedactor>()).OnEnd(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new RedactingLogProcessor(api.Host.Services.GetRequiredService<TelemetryRedactor>()).OnEnd(null!))).Should().Throw<ArgumentNullException>();
    }
}
