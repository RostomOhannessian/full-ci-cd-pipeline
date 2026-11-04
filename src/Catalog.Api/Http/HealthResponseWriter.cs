using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Catalog.Api.Http;

/// <summary>
/// Writes the report of a health endpoint as JSON (plan section 8.9, ADR-0040). The endpoints are on the management port, which the gateway never routes to, so the report lists each check with its status and its text.
/// The text is the one that each check wrote on purpose. A check that threw has an exception, and the description that the framework took from it is replaced, so no message of an exception, no connection string, and no
/// credential reaches a response.
/// </summary>
internal static class HealthResponseWriter
{
    /// <summary>Writes the report.</summary>
    /// <param name="context">The context of the request.</param>
    /// <param name="report">The report.</param>
    public static async Task WriteAsync(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = "application/json";

        await using var writer = new Utf8JsonWriter(context.Response.BodyWriter);
        writer.WriteStartObject();
        writer.WriteString("status", report.Status.ToString());
        writer.WriteNumber("durationMs", (long)report.TotalDuration.TotalMilliseconds);
        writer.WriteStartArray("checks");

        foreach (var (name, entry) in report.Entries)
        {
            writer.WriteStartObject();
            writer.WriteString("name", name);
            writer.WriteString("status", entry.Status.ToString());
            writer.WriteString("description", entry.Exception is null ? entry.Description : "The check failed.");
            writer.WriteNumber("durationMs", (long)entry.Duration.TotalMilliseconds);

            if (entry.Data.Count > 0)
            {
                writer.WriteStartObject("data");

                foreach (var (key, value) in entry.Data)
                {
                    WriteValue(writer, key, value);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    // The checks of this API put numbers and flags in their data. Any other value is written as nothing, so a check cannot leak an object by accident.
    private static void WriteValue(Utf8JsonWriter writer, string key, object value)
    {
        switch (value)
        {
            case int number:
                writer.WriteNumber(key, number);
                break;
            case long number:
                writer.WriteNumber(key, number);
                break;
            case bool flag:
                writer.WriteBoolean(key, flag);
                break;
        }
    }
}
