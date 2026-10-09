using Governance.Auditor.Status;
using Governance.Auditor.Tests.Support;
using Governance.Auditor.Trace;
using Xunit;

namespace Governance.Auditor.Tests;

public sealed class ProofScannerTests
{
    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public void A_dotnet_test_names_its_requirement_with_a_trait_and_the_line_is_recorded()
    {
        using var repository = new TestRepository()
            .Add("tests/Sample.Tests/OneTests.cs", "public sealed class OneTests\n{\n    [Fact]\n    [Trait(\"Requirement\", \"REQ-GOV-002\")]\n    public void A() { }\n}\n");

        var references = ProofScanner.Scan(repository.Files);

        var reference = Assert.Single(references);
        Assert.Equal("REQ-GOV-002", reference.Requirement);
        Assert.Equal("tests/Sample.Tests/OneTests.cs", reference.Path);
        Assert.Equal(4, reference.Line);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public void A_trait_on_a_theory_and_a_trait_with_extra_spaces_are_both_found()
    {
        using var repository = new TestRepository()
            .Add("tests/Sample.Tests/Spaced.cs", "[Trait( \"Requirement\" ,  \"REQ-QUA-003\" )]\n[Trait(\"Requirement\", \"REQ-CI-004\")]\n[Trait(\"Category\", \"Slow\")]\n");

        var found = ProofScanner.Scan(repository.Files).Select(reference => reference.Requirement).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(["REQ-CI-004", "REQ-QUA-003"], found);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public void A_trait_in_production_code_or_documentation_is_not_a_test_and_proves_nothing()
    {
        using var repository = new TestRepository()
            .Add("src/Catalog.Domain/Thing.cs", "[Trait(\"Requirement\", \"REQ-GOV-002\")]\n")
            .Add("docs/page.md", "[Trait(\"Requirement\", \"REQ-GOV-002\")]\n");

        Assert.Empty(ProofScanner.Scan(repository.Files));
    }

    [Theory]
    [Trait("Requirement", "REQ-QUA-003")]
    [InlineData("tools/ci/Thing.Tests.ps1", "# requirements: REQ-QUA-005")]
    [InlineData("policies/kyverno/tests/test.yaml", "# requirements: REQ-POL-001")]
    [InlineData("tests/k6/smoke.js", "// requirement: REQ-OBS-002")]
    [InlineData("infra/terraform/modules/x/tests/x.tftest.hcl", "# Requirements: REQ-INF-003")]
    public void A_test_in_another_language_declares_its_requirements_in_a_comment(string path, string declaration)
    {
        using var repository = new TestRepository().Add(path, $"{declaration}\nsomething else\n");

        var reference = Assert.Single(ProofScanner.Scan(repository.Files));

        Assert.StartsWith("REQ-", reference.Requirement, StringComparison.Ordinal);
        Assert.Equal(path, reference.Path);
        Assert.Equal(1, reference.Line);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public void One_comment_can_name_several_requirements_separated_by_commas_or_spaces()
    {
        using var repository = new TestRepository().Add("tools/ci/Many.Tests.ps1", "# requirements: REQ-QUA-005, REQ-QUA-004 REQ-CI-001\n");

        var found = ProofScanner.Scan(repository.Files).Select(reference => reference.Requirement).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(["REQ-CI-001", "REQ-QUA-004", "REQ-QUA-005"], found);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public void A_comment_that_is_not_a_requirement_declaration_is_ignored()
    {
        using var repository = new TestRepository().Add("tools/ci/Other.Tests.ps1", "# This test covers REQ-QUA-005 only in prose\n# requirements are listed above\n");

        Assert.Empty(ProofScanner.Scan(repository.Files));
    }
}

public sealed class TraceAnalyzerTests
{
    private static readonly StatusDocument Status = StatusSamples.Parse(StatusSamples.Valid);

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public void A_requirement_is_in_scope_when_every_work_package_that_delivers_it_is_completed()
    {
        var result = Analyze(
            [TraceSamples.Requirement("REQ-GOV-001", ["WP1.1"]), TraceSamples.Requirement("REQ-GOV-002", ["WP1.1", "WP1.2"])],
            tests: [],
            evidence: []);

        var done = result.Coverage.Single(item => item.Requirement.Id == "REQ-GOV-001");
        var partial = result.Coverage.Single(item => item.Requirement.Id == "REQ-GOV-002");

        Assert.True(done.InScope);
        Assert.Equal(CoverageState.Missing, done.State);
        Assert.False(partial.InScope);
        Assert.Equal(CoverageState.NotDue, partial.State);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public void Traceability_fails_when_an_in_scope_requirement_has_no_test_and_no_evidence()
    {
        var result = Analyze([TraceSamples.Requirement("REQ-GOV-001", ["WP1.1"])], tests: [], evidence: []);

        var finding = Assert.Single(result.Findings, finding => finding.Rule == TraceAnalyzer.MissingProof);
        Assert.Equal(Common.Severity.Error, finding.Severity);
        Assert.Contains("REQ-GOV-001", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public void A_test_that_carries_the_requirement_id_is_proof()
    {
        var result = Analyze(
            [TraceSamples.Requirement("REQ-GOV-001", ["WP1.1"])],
            tests: [new TestReference("REQ-GOV-001", "tests/A.cs", 3)],
            evidence: []);

        Assert.DoesNotContain(result.Findings, finding => finding.Rule == TraceAnalyzer.MissingProof);
        Assert.Equal(CoverageState.Covered, result.Coverage[0].State);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public void An_evidence_entry_whose_file_exists_is_proof()
    {
        var result = Analyze(
            [TraceSamples.Requirement("REQ-GOV-001", ["WP1.1"])],
            tests: [],
            evidence: [TraceSamples.Entry("REQ-GOV-001", "docs/evidence/first.md")]);

        Assert.DoesNotContain(result.Findings, finding => finding.Severity == Common.Severity.Error);
        Assert.Equal(CoverageState.Covered, result.Coverage[0].State);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public void An_evidence_entry_that_points_at_a_missing_file_is_an_error_and_is_not_trusted_as_proof()
    {
        var result = Analyze(
            [TraceSamples.Requirement("REQ-GOV-001", ["WP1.1"])],
            tests: [],
            evidence: [TraceSamples.Entry("REQ-GOV-001", "docs/evidence/nowhere.md")]);

        Assert.Contains(result.Findings, finding => finding.Rule == "trace-evidence-path" && finding.Message.Contains("nowhere.md", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public void A_test_or_evidence_entry_that_names_an_unknown_requirement_is_an_error()
    {
        var result = Analyze(
            [TraceSamples.Requirement("REQ-GOV-001", ["WP1.1"])],
            tests: [new TestReference("REQ-GOV-999", "tests/A.cs", 7), new TestReference("REQ-bad", "tests/B.cs", 2)],
            evidence: [TraceSamples.Entry("REQ-GOV-998", "docs/evidence/first.md")]);

        var unknown = result.Findings.Where(finding => finding.Rule == "trace-unknown-requirement").ToList();

        Assert.Equal(3, unknown.Count);
        Assert.Contains(unknown, finding => finding.Path == "tests/A.cs" && finding.Line == 7);
        Assert.Contains(unknown, finding => finding.Message.Contains("not a valid requirement ID", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public void A_requirement_that_names_an_unknown_work_package_is_an_error_but_launch_is_known()
    {
        var result = Analyze([TraceSamples.Requirement("REQ-GOV-001", ["WP9.9"]), TraceSamples.Requirement("REQ-GOV-002", ["LAUNCH"])], tests: [], evidence: []);

        Assert.Single(result.Findings, finding => finding.Rule == "trace-unknown-work-package");
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public void A_verified_requirement_is_always_in_scope_even_when_its_work_is_not_finished()
    {
        var result = Analyze([TraceSamples.Requirement("REQ-GOV-002", ["WP2.1"], status: "verified")], tests: [], evidence: []);

        Assert.True(result.Coverage[0].InScope);
        Assert.Contains(result.Findings, finding => finding.Rule == TraceAnalyzer.MissingProof);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public void A_deferred_or_withdrawn_requirement_is_never_in_scope()
    {
        var result = Analyze(
            [TraceSamples.Requirement("REQ-GOV-001", ["WP1.1"], status: "deferred"), TraceSamples.Requirement("REQ-GOV-002", ["WP1.1"], status: "withdrawn")],
            tests: [],
            evidence: [],
            new TraceOptions(All: true));

        Assert.Empty(result.Findings);
        Assert.Equal([CoverageState.Deferred, CoverageState.Withdrawn], result.Coverage.Select(item => item.State));
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public void The_all_option_puts_every_active_requirement_in_scope_to_show_what_is_still_missing()
    {
        var result = Analyze([TraceSamples.Requirement("REQ-GOV-002", ["WP2.1"])], tests: [], evidence: [], new TraceOptions(All: true));

        Assert.Single(result.Findings, finding => finding.Rule == TraceAnalyzer.MissingProof);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public void The_phase_option_puts_the_requirements_of_that_phase_and_the_earlier_ones_in_scope()
    {
        var requirements = new[] { TraceSamples.Requirement("REQ-GOV-001", ["WP1.2"]), TraceSamples.Requirement("REQ-GOV-002", ["WP2.1"]), TraceSamples.Requirement("REQ-GOV-003", ["WP1.1", "WP2.1"]) };

        var throughOne = Analyze(requirements, tests: [], evidence: [], new TraceOptions(ThroughPhase: new PhaseId("1")));
        var throughTwo = Analyze(requirements, tests: [], evidence: [], new TraceOptions(ThroughPhase: new PhaseId("2")));

        Assert.Equal(["REQ-GOV-001"], throughOne.Coverage.Where(item => item.InScope).Select(item => item.Requirement.Id));
        Assert.Equal(3, throughTwo.Coverage.Count(item => item.InScope));
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public void Proof_that_arrives_before_the_work_is_complete_is_reported_as_covered_and_not_as_a_failure()
    {
        var result = Analyze(
            [TraceSamples.Requirement("REQ-GOV-002", ["WP1.1", "WP1.2"])],
            tests: [new TestReference("REQ-GOV-002", "tests/A.cs", 1)],
            evidence: []);

        Assert.False(result.Coverage[0].InScope);
        Assert.Equal(CoverageState.Covered, result.Coverage[0].State);
        Assert.Empty(result.Findings);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public void A_proven_and_fully_delivered_requirement_that_is_still_planned_gets_a_status_warning()
    {
        var result = Analyze(
            [
                TraceSamples.Requirement("REQ-GOV-001", ["WP1.1"], status: "planned"),
                TraceSamples.Requirement("REQ-GOV-003", ["WP1.1"], status: "verified"),
                TraceSamples.Requirement("REQ-QUA-003", ["WP1.1"], status: "in_progress"),
            ],
            tests: [new TestReference("REQ-GOV-001", "tests/A.cs", 1), new TestReference("REQ-GOV-003", "tests/A.cs", 2), new TestReference("REQ-QUA-003", "tests/A.cs", 3)],
            evidence: []);

        var warning = Assert.Single(result.Findings);
        Assert.Equal(Common.Severity.Warning, warning.Severity);
        Assert.Contains("REQ-GOV-001", warning.Message, StringComparison.Ordinal);
        Assert.Contains("'in_progress' or 'verified'", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public void A_requirement_listed_twice_is_an_error()
    {
        var result = Analyze([TraceSamples.Requirement("REQ-GOV-001", ["WP1.1"]), TraceSamples.Requirement("REQ-GOV-001", ["WP1.1"])], tests: [], evidence: []);

        Assert.Contains(result.Findings, finding => finding.Rule == "trace-duplicate-requirement");
    }

    private static TraceResult Analyze(Requirement[] requirements, TestReference[] tests, EvidenceEntry[] evidence, TraceOptions? options = null)
    {
        using var repository = new TestRepository().Add("docs/evidence/first.md", "Evidence.");
        return TraceAnalyzer.Analyze(TraceSamples.Register(requirements), Status, tests, TraceSamples.Evidence(evidence), repository.Files, options ?? new TraceOptions());
    }
}
