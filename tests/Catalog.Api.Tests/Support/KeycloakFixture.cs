using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Catalog.Infrastructure.Tests.Support;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Xunit;

namespace Catalog.Api.Tests.Support;

/// <summary>
/// One Keycloak server in a container, from the image that <c>compose.yaml</c> pins by digest, importing the one realm file of the repository (REQ-IDN-005, REQ-QUA-001, ADR-0026, ADR-0042). The realm file holds
/// <c>${NAME}</c> placeholders for its secrets and passwords, and the server fills them from its environment, so this fixture gives it values that it makes at run time and that exist only for the life of the container.
/// The server advertises one fixed issuer, <c>http://localhost:{port}/realms/catalog</c>, so the port is chosen before the container starts, as the Compose stack does with its fixed port. The test process may reach the container
/// at another name than <c>localhost</c> (the Linux test image reaches the Docker host through <c>host.docker.internal</c>), so the fixture calls the server at <see cref="Address" />, and the server resolves the endpoints that a
/// client calls (the keys and the token endpoint) from that address while the issuer stays fixed. That is the split that the API supports with <c>Catalog:Auth:MetadataAddress</c>.
/// </summary>
public sealed class KeycloakFixture : IAsyncLifetime
{
    private const string RealmFile = "catalog-realm.json";

    private IContainer? _container;

    /// <summary>Gets the secret of the client <c>smoke-tests</c>, which has every scope and the admin role.</summary>
    public string SmokeTestsSecret { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    /// <summary>Gets the secret of the client <c>synthetic-shopper</c>, which has the read scope and the reader role.</summary>
    public string ShopperSecret { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    /// <summary>Gets the port of the server on the host.</summary>
    public int Port { get; private set; }

    /// <summary>Gets the issuer that the server advertises and puts in every token.</summary>
    public string Issuer => $"http://localhost:{Port}/realms/catalog";

    /// <summary>Gets the address at which the test process reaches the server, which is the issuer's host only when the process runs on the Docker host.</summary>
    public string Address { get; private set; } = string.Empty;

    /// <summary>Gets the address of the metadata that the API reads, for a host whose issuer is not reachable from the test process.</summary>
    public string MetadataAddress => $"{Address}/realms/catalog/.well-known/openid-configuration";

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        // The settings are the ones of the SQL Server fixture, because Testcontainers keeps them for the process and a fixture may start first.
        TestcontainersSettings.ResourceReaperImage = new DockerImage(SqlServerFixture.ReaperImage);
        TestcontainersSettings.ResourceReaperPrivilegedModeEnabled = false;

        Port = FreePort();
        var realm = new FileInfo(Path.Combine(ComposeImage.RepositoryRoot(), "identity", "keycloak", "realms", RealmFile));

        _container = new ContainerBuilder(ComposeImage.Keycloak())
            .WithCommand("start-dev", "--import-realm", "--db=dev-mem", "--http-port=8080", $"--hostname=http://localhost:{Port}", "--hostname-backchannel-dynamic=true")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
            .WithEnvironment("CATALOG_SMOKE_TESTS_SECRET", SmokeTestsSecret)
            .WithEnvironment("CATALOG_SYNTHETIC_SHOPPER_SECRET", ShopperSecret)
            .WithEnvironment("CATALOG_READER_PASSWORD", Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
            .WithEnvironment("CATALOG_EDITOR_PASSWORD", Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
            .WithEnvironment("CATALOG_ADMIN_PASSWORD", Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
            .WithResourceMapping(realm, "/opt/keycloak/data/import/")
            .WithPortBinding(Port, 8080)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Listening on"))
            .Build();

        await _container.StartAsync();
        Address = $"http://{_container.Hostname}:{Port}";

        using var client = new HttpClient();
        await Poll.UntilAsync(
            async () =>
            {
                try
                {
                    using var response = await client.GetAsync(MetadataAddress, TestContext.Current.CancellationToken);

                    return response.StatusCode == HttpStatusCode.OK;
                }
                catch (HttpRequestException)
                {
                    return false;
                }
            },
            "the catalog realm answers its discovery document",
            TestContext.Current.CancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>Gets an access token for a client of the realm with the client credentials grant, or the error that the server answered.</summary>
    /// <param name="clientId">The client.</param>
    /// <param name="secret">The secret of the client.</param>
    /// <param name="scope">The scopes to ask for, or null for the default scopes of the client.</param>
    public async Task<(HttpStatusCode Status, JsonElement Body)> RequestTokenAsync(string clientId, string secret, string? scope = null)
    {
        using var client = new HttpClient();
        List<KeyValuePair<string, string>> form = [new("grant_type", "client_credentials"), new("client_id", clientId), new("client_secret", secret)];

        if (scope is not null)
        {
            form.Add(new("scope", scope));
        }

        using var response = await client.PostAsync($"{Address}/realms/catalog/protocol/openid-connect/token", new FormUrlEncodedContent(form), TestContext.Current.CancellationToken);

        return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.Clone());
    }

    /// <summary>Reads the claims of a token without checking it. The tests use it to read what the server put in, and the API checks the token itself.</summary>
    /// <param name="token">The token.</param>
    public static JsonElement ClaimsOf(string token)
    {
        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');

        return JsonDocument.Parse(Convert.FromBase64String(payload)).RootElement.Clone();
    }

    // The port is free when it is chosen, and the container binds it a moment later, which is the approach of the Valkey fixture.
    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
