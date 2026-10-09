using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Governance.Auditor.Common;

namespace Governance.Auditor.TestSummary;

internal sealed record TestFailure(string Test, string Message);

/// <param name="File">The TRX file name.</param>
/// <param name="Total">All results in the file.</param>
/// <param name="Passed">Results with the outcome Passed.</param>
/// <param name="Failed">Results that failed, errored, timed out, or aborted.</param>
/// <param name="Skipped">Everything else, such as results that were not executed.</param>
/// <param name="Duration">The wall-clock time of the run.</param>
/// <param name="Failures">The failed tests, with the first line of each message.</param>
internal sealed record TrxSummary(string File, int Total, int Passed, int Failed, int Skipped, TimeSpan Duration, IReadOnlyList<TestFailure> Failures);

/// <summary>
/// Turns TRX files into a Markdown job summary. A TRX file is an artifact of a test run, so it is read as untrusted data: DTDs are
/// refused, and the text of a failure is flattened and shortened before it reaches the summary.
/// </summary>
internal static class TrxParser
{
    private const int MaxFailuresShown = 25;
    private static readonly string[] FailedOutcomes = ["Failed", "Error", "Timeout", "Aborted"];

    public static TrxSummary Parse(string fileName, string xml)
    {
        XDocument document;

        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException exception)
        {
            throw new GovernanceException($"{fileName}: the file is not valid XML. {exception.Message}", exception);
        }

        var results = document.Descendants().Where(element => element.Name.LocalName == "UnitTestResult").ToList();
        List<TestFailure> failures = [];
        var passed = 0;
        var failed = 0;

        foreach (var result in results)
        {
            var outcome = (string?)result.Attribute("outcome") ?? string.Empty;

            if (string.Equals(outcome, "Passed", StringComparison.Ordinal))
            {
                passed++;
            }
            else if (FailedOutcomes.Contains(outcome, StringComparer.Ordinal))
            {
                failed++;
                var message = result.Descendants().FirstOrDefault(element => element.Name.LocalName == "Message")?.Value ?? string.Empty;
                failures.Add(new TestFailure((string?)result.Attribute("testName") ?? "(unnamed test)", message.Split('\n')[0].Trim()));
            }
        }

        return new TrxSummary(fileName, results.Count, passed, failed, results.Count - passed - failed, RunDuration(document), failures);
    }

    public static string Render(IReadOnlyList<TrxSummary> summaries, int maxFailures = MaxFailuresShown)
    {
        var failed = summaries.Sum(summary => summary.Failed);
        var heading = $"### Test results: {(failed == 0 ? "passed" : "failed")}\n\n";

        var rows = summaries.Select<TrxSummary, IReadOnlyList<string>>(summary =>
        [
            MarkdownText.Code(summary.File),
            summary.Total.ToString(CultureInfo.InvariantCulture),
            summary.Passed.ToString(CultureInfo.InvariantCulture),
            summary.Failed.ToString(CultureInfo.InvariantCulture),
            summary.Skipped.ToString(CultureInfo.InvariantCulture),
            summary.Duration.ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture),
        ]);

        var builder = new System.Text.StringBuilder(heading);
        builder.Append(MarkdownText.Table(["Result file", "Total", "Passed", "Failed", "Skipped", "Duration"], rows)).Append('\n');

        var failures = summaries.SelectMany(summary => summary.Failures).ToList();

        if (failures.Count > 0)
        {
            builder.Append("Failed tests:\n\n");
            builder.Append(MarkdownText.Table(
                ["Test", "Message"],
                failures.Take(maxFailures).Select<TestFailure, IReadOnlyList<string>>(failure => [MarkdownText.Cell(failure.Test, 200), MarkdownText.Cell(failure.Message)])));

            if (failures.Count > maxFailures)
            {
                builder.Append($"\nThe first {maxFailures} of {failures.Count} failures are shown.\n");
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static TimeSpan RunDuration(XDocument document)
    {
        var times = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Times");

        return times is not null
            && DateTimeOffset.TryParse((string?)times.Attribute("start"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            && DateTimeOffset.TryParse((string?)times.Attribute("finish"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var finish)
            && finish >= start
                ? finish - start
                : TimeSpan.Zero;
    }
}
