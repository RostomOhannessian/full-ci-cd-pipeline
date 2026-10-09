using Governance.Auditor.Tests.Support;
using Governance.Auditor.Trace;
using Xunit;

namespace Governance.Auditor.Tests;

public sealed class TraceCommandTests
{
    private static readonly (string Id, string[] DeliveredBy, string Status)[] Requirements =
    [
        ("REQ-GOV-001", ["WP1.1"], "planned"),
        ("REQ-GOV-002", ["WP1.1", "WP1.2"], "planned"),
        ("REQ-QUA-003", ["WP1.1"], "planned"),
    ];

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public async Task The_report_matches_the_reviewed_example()
    {
        using var repository = Repository(
            TraceSamples.EvidenceYaml(("REQ-GOV-001", "docs", "docs/evidence/first.md")),
            ("tests/Sample.Tests/Quality.cs", "[Trait(\"Requirement\", \"REQ-QUA-003\")]\n[Trait(\"Requirement\", \"REQ-GOV-002\")]\n"));

        var result = await CliRunner.RunAsync(repository, "trace");

        Assert.Equal(0, result.ExitCode);
        Golden.AssertMatches(repository.Read(TraceFiles.ReportPath), "TraceReport.expected.txt");
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public async Task The_command_fails_with_exit_code_1_and_names_the_requirement_when_proof_is_missing_and_still_writes_the_report()
    {
        using var repository = Repository(TraceSamples.EvidenceYaml(), ("tests/Sample.Tests/Quality.cs", "[Trait(\"Requirement\", \"REQ-QUA-003\")]\n"));

        var result = await CliRunner.RunAsync(repository, "trace");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("trace-missing-proof", result.Output, StringComparison.Ordinal);
        Assert.Contains("REQ-GOV-001", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("REQ-QUA-003 (", result.Output, StringComparison.Ordinal);
        Assert.Contains("Missing proof", repository.Read(TraceFiles.ReportPath), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public async Task The_drift_check_passes_after_the_report_is_written_and_fails_when_a_new_test_changes_the_proof()
    {
        using var repository = Repository(
            TraceSamples.EvidenceYaml(("REQ-GOV-001", "docs", "docs/evidence/first.md")),
            ("tests/Sample.Tests/Quality.cs", "[Trait(\"Requirement\", \"REQ-QUA-003\")]\n"));
        await CliRunner.RunAsync(repository, "trace");

        var unchanged = await CliRunner.RunAsync(repository, "trace", "--check");
        repository.Add("tests/Sample.Tests/Governance.cs", "[Trait(\"Requirement\", \"REQ-GOV-002\")]\n");
        var changed = await CliRunner.RunAsync(repository, "trace", "--check");

        Assert.Equal(0, unchanged.ExitCode);
        Assert.Equal(1, changed.ExitCode);
        Assert.Contains("trace-drift", changed.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public async Task The_all_option_lists_what_is_missing_across_every_requirement()
    {
        using var repository = Repository(
            TraceSamples.EvidenceYaml(("REQ-GOV-001", "docs", "docs/evidence/first.md")),
            ("tests/Sample.Tests/Quality.cs", "[Trait(\"Requirement\", \"REQ-QUA-003\")]\n"));

        var result = await CliRunner.RunAsync(repository, "trace", "--all");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("REQ-GOV-002", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public async Task An_unknown_phase_is_a_usage_error_with_exit_code_2()
    {
        using var repository = Repository(TraceSamples.EvidenceYaml());

        var result = await CliRunner.RunAsync(repository, "trace", "--phase", "9");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--phase must be 0 to 4 or launch", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public async Task A_requirements_file_that_breaks_its_schema_is_reported_and_nothing_is_written()
    {
        using var repository = Repository(TraceSamples.EvidenceYaml());
        repository.Add(TraceFiles.RequirementsPath, "schema-version: 1\n");

        var result = await CliRunner.RunAsync(repository, "trace");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("trace-schema", result.Output, StringComparison.Ordinal);
        Assert.False(repository.Exists(TraceFiles.ReportPath));
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public async Task An_evidence_register_that_breaks_its_schema_is_reported()
    {
        using var repository = Repository(TraceSamples.EvidenceYaml());
        repository.Add(TraceFiles.EvidencePath, "schema-version: 1\nlast-verified: '2026-10-08'\nentries:\n  - requirements: []\n    kind: guess\n    path: x\n    summary: short\n    recorded: yesterday\n");

        var result = await CliRunner.RunAsync(repository, "trace");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("must be one of", result.Output, StringComparison.Ordinal);
        Assert.Contains("at least 1 items", result.Output, StringComparison.Ordinal);
    }

    private static TestRepository Repository(string evidenceYaml, params (string Path, string Content)[] tests)
    {
        var repository = TraceSamples.Repository(TraceSamples.RequirementsYaml(Requirements), evidenceYaml);

        foreach (var (path, content) in tests)
        {
            repository.Add(path, content);
        }

        return repository;
    }
}
