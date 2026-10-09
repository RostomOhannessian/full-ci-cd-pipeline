using Governance.Auditor.Common;
using Governance.Auditor.Tests.Support;
using Governance.Auditor.TestSummary;
using Xunit;

namespace Governance.Auditor.Tests;

public sealed class TestSummaryTests
{
    private const string Trx = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Times creation="2026-10-08T10:00:00.0000000+00:00" start="2026-10-08T10:00:01.0000000+00:00" finish="2026-10-08T10:00:03.5000000+00:00" />
          <Results>
            <UnitTestResult testName="Passes_one" outcome="Passed" duration="00:00:00.0010000" />
            <UnitTestResult testName="Passes_two" outcome="Passed" duration="00:00:00.0020000" />
            <UnitTestResult testName="Fails_with_a_message | with a pipe" outcome="Failed" duration="00:00:00.0030000">
              <Output>
                <ErrorInfo>
                  <Message>Expected 1 but found 2
        second line that must not appear
                  </Message>
                  <StackTrace>at Something.Hidden()</StackTrace>
                </ErrorInfo>
              </Output>
            </UnitTestResult>
            <UnitTestResult testName="Is_skipped" outcome="NotExecuted" />
          </Results>
        </TestRun>
        """;

    [Fact]
    [Trait("Requirement", "REQ-QUA-002")]
    public void A_trx_file_is_counted_by_outcome_and_the_failure_keeps_only_the_first_line_of_its_message()
    {
        var summary = TrxParser.Parse("sample.trx", Trx);

        Assert.Equal(4, summary.Total);
        Assert.Equal(2, summary.Passed);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(1, summary.Skipped);
        Assert.Equal(TimeSpan.FromSeconds(2.5), summary.Duration);

        var failure = Assert.Single(summary.Failures);
        Assert.Equal("Expected 1 but found 2", failure.Message);
        Assert.DoesNotContain("Hidden", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Requirement", "REQ-QUA-002")]
    [InlineData("Error")]
    [InlineData("Timeout")]
    [InlineData("Aborted")]
    public void An_error_a_timeout_and_an_abort_count_as_failures(string outcome)
    {
        var xml = $"<TestRun><Results><UnitTestResult testName=\"T\" outcome=\"{outcome}\" /></Results></TestRun>";

        var summary = TrxParser.Parse("sample.trx", xml);

        Assert.Equal(1, summary.Failed);
        Assert.Equal(0, summary.Passed);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-002")]
    public void The_markdown_summary_escapes_text_from_the_results_so_a_test_name_cannot_inject_markup()
    {
        var markdown = TrxParser.Render([TrxParser.Parse("sample.trx", Trx)]);

        Assert.Contains("### Test results: failed", markdown, StringComparison.Ordinal);
        Assert.Contains("| `sample.trx` | 4 | 2 | 1 | 1 | 0:02.500 |", markdown, StringComparison.Ordinal);
        Assert.Contains("Fails_with_a_message \\| with a pipe", markdown, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-002")]
    public void A_long_list_of_failures_is_cut_off_with_a_count()
    {
        var results = string.Join(string.Empty, Enumerable.Range(1, 30).Select(index => $"<UnitTestResult testName=\"T{index}\" outcome=\"Failed\" />"));

        var markdown = TrxParser.Render([TrxParser.Parse("many.trx", $"<TestRun><Results>{results}</Results></TestRun>")], maxFailures: 5);

        Assert.Contains("The first 5 of 30 failures are shown.", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("T6 ", markdown, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-002")]
    public void A_file_with_a_doctype_is_refused_because_test_results_are_untrusted_input()
    {
        const string hostile = """
            <?xml version="1.0"?>
            <!DOCTYPE TestRun [<!ENTITY secret SYSTEM "file:///etc/passwd">]>
            <TestRun><Results><UnitTestResult testName="&secret;" outcome="Passed" /></Results></TestRun>
            """;

        var exception = Assert.Throws<GovernanceException>(() => TrxParser.Parse("hostile.trx", hostile));

        Assert.Contains("hostile.trx", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-002")]
    public async Task The_command_passes_when_every_test_passed_and_writes_the_job_summary()
    {
        using var repository = new TestRepository().Add("TestResults/ok.trx", "<TestRun><Results><UnitTestResult testName=\"A\" outcome=\"Passed\" /></Results></TestRun>");
        var summary = Path.Combine(repository.Root, "summary.md");

        var result = await CliRunner.RunAsync(repository, "test-summary", "--summary", summary);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("1 results in 1 files, 0 failed", result.Output, StringComparison.Ordinal);
        Assert.Contains("### Test results: passed", await File.ReadAllTextAsync(summary, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-002")]
    public async Task The_command_fails_when_a_test_failed()
    {
        using var repository = new TestRepository().Add("TestResults/nested/bad.trx", Trx);

        var result = await CliRunner.RunAsync(repository, "test-summary");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("test-failed", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-002")]
    public async Task No_results_at_all_is_a_failure_because_not_measured_must_never_read_as_passed()
    {
        using var repository = new TestRepository();

        var result = await CliRunner.RunAsync(repository, "test-summary");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("test-summary-empty", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-002")]
    public async Task A_single_trx_file_can_be_named_directly()
    {
        using var repository = new TestRepository().Add("out/run.trx", "<TestRun><Results><UnitTestResult testName=\"A\" outcome=\"Passed\" /></Results></TestRun>");

        var result = await CliRunner.RunAsync(repository, "test-summary", "--results", "out/run.trx");

        Assert.Equal(0, result.ExitCode);
    }
}
