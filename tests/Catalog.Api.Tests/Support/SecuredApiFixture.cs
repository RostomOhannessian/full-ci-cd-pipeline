using System.Net.Http.Headers;
using System.Text.Json;
using Xunit;

namespace Catalog.Api.Tests.Support;

/// <summary>An in-process issuer and the real API behind it, with the test endpoints. A class that needs a host of its own settings derives from it, and the others share it.</summary>
public class SecuredApiFixture : IAsyncLifetime
{
    internal TestIssuer Issuer { get; private set; } = null!;

    internal ApiTestHost Host { get; private set; } = null!;

    internal TestKey EcKey { get; private set; } = null!;

    internal virtual HostSettings Customize(HostSettings settings) => settings;

    /// <summary>Gets what the host adds to the telemetry after its own pipeline, such as an in-memory exporter. A class that looks at the telemetry overrides it.</summary>
    internal virtual Action<IServiceCollection>? AttachTelemetry => null;

    public async ValueTask InitializeAsync()
    {
        Issuer = await TestIssuer.StartAsync();
        EcKey = TestKey.Ec("key-ec");
        Issuer.Publish(Issuer.Signing, EcKey);
        Host = await ApiTestHost.StartAsync(Customize(new HostSettings(Issuer)), TestEndpoints.Map, telemetry: AttachTelemetry);
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        await Issuer.DisposeAsync();
        EcKey.Dispose();
        GC.SuppressFinalize(this);
    }

    internal string Token(TokenSpec? spec = null) => Issuer.Token(spec);

    internal Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token = null, Action<HttpRequestMessage>? configure = null)
    {
        var request = new HttpRequestMessage(method, path);

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        configure?.Invoke(request);

        return Host.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}

/// <summary>Reads what a test needs from an answer.</summary>
internal static class ResponseReading
{
    public static Task<string> TextAsync(this HttpResponseMessage response) => response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response)
    {
        var text = await response.TextAsync();

        return JsonDocument.Parse(text).RootElement.Clone();
    }

    public static string ProblemSlug(this JsonElement problem)
    {
        var type = problem.GetProperty("type").GetString()!;

        return type[(type.IndexOf('#', StringComparison.Ordinal) + 1)..];
    }
}
