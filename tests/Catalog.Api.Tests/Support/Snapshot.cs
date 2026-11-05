using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace Catalog.Api.Tests.Support;

/// <summary>
/// Compares a JSON value with the snapshot that is committed next to the tests (REQ-API-005, REQ-CON-002, ADR-0042). The snapshot is <c>Snapshots/{file}.{method}.verified.json</c>, written with two-space indentation and
/// LF line endings, and the trace identifier of an error and the UUIDs in its instance path are replaced, so a snapshot holds no value that changes between runs, no token, and no secret. A snapshot that does not match writes
/// <c>.received.json</c> beside it and fails the test with the first difference, and a snapshot that does not exist fails the same way, so a reviewer reads every new one. A developer accepts a change by setting
/// <c>SNAPSHOT_ACCEPT=1</c>, which copies the received file over the verified one, and CI never accepts. The helper replaces Verify, whose releases from 33.0.0 fail the build unless the owner declares a sponsorship or an
/// exemption (ADR-0042).
/// </summary>
internal static class Snapshot
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly Regex Uuid = new("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Compares the value with the snapshot of the calling test.</summary>
    /// <param name="json">The JSON text of the value.</param>
    /// <param name="sourceFile">The file of the test, whose name (and not its path, which a CI build rewrites) names the snapshot.</param>
    /// <param name="method">The name of the test, which names the snapshot.</param>
    public static async Task MatchAsync(string json, [CallerFilePath] string sourceFile = "", [CallerMemberName] string method = "")
    {
        var folder = Path.Combine(Repository.Root, "tests", "Catalog.Api.Tests", "Snapshots");
        var name = $"{Path.GetFileNameWithoutExtension(sourceFile)}.{method}";
        var verified = Path.Combine(folder, $"{name}.verified.json");
        var received = Path.Combine(folder, $"{name}.received.json");
        var actual = Canonical(json);

        var expected = File.Exists(verified) ? (await File.ReadAllTextAsync(verified, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n") : null;

        if (expected == actual)
        {
            File.Delete(received);

            return;
        }

        Directory.CreateDirectory(folder);

        if (Environment.GetEnvironmentVariable("SNAPSHOT_ACCEPT") == "1" && Environment.GetEnvironmentVariable("CI") is null)
        {
            await File.WriteAllTextAsync(verified, actual, TestContext.Current.CancellationToken);
            File.Delete(received);

            return;
        }

        await File.WriteAllTextAsync(received, actual, TestContext.Current.CancellationToken);
        Assert.Fail(expected is null
            ? $"The snapshot {name} does not exist. Review {Path.GetFileName(received)} and accept it with SNAPSHOT_ACCEPT=1."
            : $"The snapshot {name} differs at {FirstDifference(expected, actual)}. Review {Path.GetFileName(received)} and accept the change with SNAPSHOT_ACCEPT=1.");
    }

    // The trace identifier and the identifiers in the instance path of an error are the members that change between runs, and the rest keeps the order in which the test wrote it.
    private static string Canonical(string json)
    {
        var node = JsonNode.Parse(json)!;
        Scrub(node);

        return node.ToJsonString(Options).ReplaceLineEndings("\n") + "\n";
    }

    private static void Scrub(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject value:
                foreach (var member in value.ToList())
                {
                    if (member.Value is JsonValue && member.Key == "traceId")
                    {
                        value[member.Key] = "Scrubbed";
                    }
                    else if (member.Value is JsonValue address && member.Key == "instance" && address.TryGetValue<string>(out var path))
                    {
                        value[member.Key] = Uuid.Replace(path, "{uuid}");
                    }
                    else
                    {
                        Scrub(member.Value);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    Scrub(item);
                }

                break;
        }
    }

    private static string FirstDifference(string expected, string actual)
    {
        var left = expected.Split('\n');
        var right = actual.Split('\n');

        for (var line = 0; line < Math.Max(left.Length, right.Length); line++)
        {
            var was = line < left.Length ? left[line] : "(end)";
            var now = line < right.Length ? right[line] : "(end)";

            if (was != now)
            {
                return $"line {line + 1}: expected {was.Trim()} but found {now.Trim()}";
            }
        }

        return "the end of the text";
    }
}
