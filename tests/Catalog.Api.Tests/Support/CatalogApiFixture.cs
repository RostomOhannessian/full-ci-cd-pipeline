using System.Globalization;
using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Catalog.Infrastructure.Tests.Support;
using Xunit;

namespace Catalog.Api.Tests.Support;

/// <summary>
/// The real API over a real SQL Server 2022 database and a real Valkey 8.1 server, for the tests of the endpoints (REQ-QUA-001). Both servers are containers that assembly fixtures start from the digests of
/// <c>compose.yaml</c>, and this fixture gives its host a database and a cache environment of its own, so test classes that run in parallel never read each other's rows or cached pages. The identity provider is the
/// in-process issuer, and the clock is the fake clock of the host.
/// </summary>
/// <param name="sql">The SQL Server fixture of the assembly.</param>
/// <param name="valkey">The Valkey fixture of the assembly.</param>
public sealed class CatalogApiFixture(SqlServerFixture sql, ValkeyFixture valkey) : IAsyncLifetime
{
    private CatalogDatabaseFixture _database = null!;

    internal TestIssuer Issuer { get; private set; } = null!;

    internal ApiTestHost Host { get; private set; } = null!;

    internal HttpClient Client => Host.Client;

    public async ValueTask InitializeAsync()
    {
        _database = new CatalogDatabaseFixture(sql);
        await _database.InitializeAsync();
        Issuer = await TestIssuer.StartAsync();

        var settings = new HostSettings(Issuer)
            .WithDatabase(_database.ConnectionString)
            .With("Catalog:Valkey:Endpoint", valkey.Server.Endpoint)
            .With("Catalog:Valkey:UserName", "default")
            .With("Catalog:Valkey:Password", valkey.Server.AdminPassword)
            .With("Catalog:Valkey:ConnectTimeout", "00:00:05")
            .With("Catalog:Valkey:CommandTimeout", "00:00:05")
            .With("Catalog:Cache:SharedTimeout", "00:00:02")
            .With("Catalog:Cache:Environment", $"api{Guid.NewGuid():N}"[..11]);

        Host = await ApiTestHost.StartAsync(settings);
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        await Issuer.DisposeAsync();
        await _database.DisposeAsync();
    }

    /// <summary>Empties every table, so a test starts from an empty catalog.</summary>
    public Task ResetAsync() => _database.ResetAsync();

    /// <summary>Sends a request as a caller.</summary>
    internal Task<ApiResponse> SendAsync(HttpMethod method, string path, TokenSpec? caller, string? json = null, Action<HttpRequestMessage>? configure = null) =>
        Client.SendAsync(Issuer, method, path, caller, json, configure);

    /// <summary>Creates a category as an administrator and returns its answer. The clock moves on first, so a creation time is never the one of the category or the product before it.</summary>
    internal async Task<ApiResponse> CreateCategoryAsync(string name)
    {
        Host.Clock.Advance(TimeSpan.FromSeconds(1));
        var response = await SendAsync(HttpMethod.Post, "/api/v1/categories", ApiCalls.Admin, $$"""{"name":"{{name}}"}""", ApiCalls.WithKey(ApiCalls.NewKey()));
        response.Status.Should().Be(HttpStatusCode.Created, response.Body);

        return response;
    }

    /// <summary>Creates a product as an editor and returns its answer. The clock moves on first, so the newest product is the one made last.</summary>
    internal async Task<ApiResponse> CreateProductAsync(Guid categoryId, string sku, string name = "Mechanical keyboard", string amount = "89.99", string currency = "USD", string? description = null)
    {
        Host.Clock.Advance(TimeSpan.FromSeconds(1));
        var response = await SendAsync(HttpMethod.Post, "/api/v1/products", ApiCalls.Editor, ProductJson(categoryId, sku, name, amount, currency, description), ApiCalls.WithKey(ApiCalls.NewKey()));
        response.Status.Should().Be(HttpStatusCode.Created, response.Body);

        return response;
    }

    /// <summary>Writes the body of a product as the contract does.</summary>
    internal static string ProductJson(Guid categoryId, string? sku, string name, string amount, string currency, string? description = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            if (sku is not null)
            {
                writer.WriteString("sku", sku);
            }

            writer.WriteString("name", name);

            if (description is not null)
            {
                writer.WriteString("description", description);
            }

            writer.WriteStartObject("price");
            writer.WriteString("amount", amount);
            writer.WriteString("currency", currency);
            writer.WriteEndObject();
            writer.WriteString("categoryId", categoryId.ToString("D", CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
