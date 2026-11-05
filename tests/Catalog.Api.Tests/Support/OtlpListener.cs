using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Catalog.Api.Tests.Support;

/// <summary>One export that the listener received: the path, the media type, and the body, which is OTLP in protobuf form.</summary>
/// <param name="Path">The path, such as <c>/v1/traces</c>.</param>
/// <param name="ContentType">The media type of the body.</param>
/// <param name="Body">The body.</param>
internal sealed record OtlpExport(string Path, string? ContentType, byte[] Body);

/// <summary>
/// A collector that is only a listener: it accepts every export over OTLP/HTTP on a port of the loopback address and keeps what it received, so a test proves what the host sends over the network and to whom. The port is
/// chosen by the system, so tests that run at the same time never meet.
/// </summary>
internal sealed class OtlpListener : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<OtlpExport> _received = new();

    private OtlpListener(WebApplication app) => _app = app;

    /// <summary>Gets the address that a host names in <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>.</summary>
    public Uri Address { get; private set; } = new("http://127.0.0.1:1");

    /// <summary>Gets what the listener received so far.</summary>
    public IReadOnlyList<OtlpExport> Received => [.. _received];

    public static async Task<OtlpListener> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        var listener = new OtlpListener(app);
        app.MapPost("/v1/{signal}", async (HttpContext context) =>
        {
            using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body, context.RequestAborted);
            listener._received.Enqueue(new OtlpExport(context.Request.Path.Value ?? string.Empty, context.Request.ContentType, body.ToArray()));

            return Results.Bytes([], "application/x-protobuf");
        });
        await app.StartAsync();

        listener.Address = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());

        return listener;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
