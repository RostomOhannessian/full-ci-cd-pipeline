using Governance.Auditor.Tests.Support;
using Governance.Auditor.Usage;
using Xunit;

namespace Governance.Auditor.Tests;

/// <summary>
/// Every rule of <c>governance usage validate</c> fails on a ledger that breaks it and stays silent on one that does not. Each case changes
/// one fact of the valid sample ledger in <c>Fixtures/usage</c>.
/// </summary>
public sealed class UsageValidationTests
{
    private const string FirstDelivery = "    phase: 1\n    work-package: \"WP1.1\"\n    activity: delivery";
    private const string SecondDelivery = "    work-package: \"WP1.2\"\n    activity: delivery";

    public static TheoryData<string, string, string> SchemaViolations => new()
    {
        { "    note: \"A session that held this work package alone.\"", "    cost-in-dollars: 5", "is not allowed" },
        { "    note: \"A session that held this work package alone.\"", "    prompt-text: \"fix the bug in the login page\"", "is not allowed" },
        { "aiu: 100.0", "aiu: -1", "at least 0" },
        { "fresh-input-tokens: 1000", "fresh-input-tokens: -5", "at least 0" },
        { "reasoning-effort: xhigh", "reasoning-effort: turbo", "must be one of" },
        { "calls: 10\n", "calls: 0\n", "at least 1" },
        { "session: \"aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa\"", "session: \"not-an-id\"", "does not match the pattern" },
        { "from: \"2026-01-05T09:00:00.000Z\"", "from: \"yesterday\"", "does not match the pattern" },
        { "activity: delivery\n    confidence: exact", "activity: shipping\n    confidence: exact", "must be one of" },
        { "agent: sub-agent", "agent: helper", "must be one of" },
        { "work-package: \"WP1.1\"\n    activity: delivery", "work-package: \"first\"\n    activity: delivery", "does not match the pattern" },
    };

    public static TheoryData<string, string, string> ConsistencyViolations => new()
    {
        { FirstDelivery, FirstDelivery.Replace("WP1.1", "WP9.9", StringComparison.Ordinal), "usage-work-package" },
        { FirstDelivery, FirstDelivery.Replace("phase: 1", "phase: 3", StringComparison.Ordinal), "usage-phase" },
        { FirstDelivery, FirstDelivery.Replace("phase: 1", "phase: 2", StringComparison.Ordinal), "usage-work-package-phase" },
        { SecondDelivery, SecondDelivery.Replace("WP1.2", "WP1.3", StringComparison.Ordinal), "usage-not-started" },
        { "to: \"2026-01-05T11:00:00.000Z\"", "to: \"2026-01-05T08:00:00.000Z\"", "usage-window" },
        { "reasoning-tokens: 1000\n", "reasoning-tokens: 9000\n", "usage-parts" },
        { "peak-context-tokens: 20000", "peak-context-tokens: 100", "usage-parts" },
        { "user-turns: 2\n", "user-turns: 50\n", "usage-parts" },
        { "compactions: 1\n", "compactions: 99\n", "usage-parts" },
        { "      - model: model-b\n        reasoning-effort: medium\n        agent: sub-agent", "      - model: model-a\n        reasoning-effort: xhigh\n        agent: main", "usage-duplicate-row" },
        { "from: \"2026-01-06T13:00:00.000Z\"", "from: \"2026-01-06T11:00:00.000Z\"", "usage-overlap" },
        { "confidence: windowed\n    window:\n      from: \"2026-01-06T09:00:00.000Z\"", "confidence: exact\n    window:\n      from: \"2026-01-06T09:00:00.000Z\"", "usage-exact-shared" },
        { "    activity: planning", "    activity: delivery", "usage-delivery-scope" },
        { "pull-requests: [18]", "pull-requests: [99]", "usage-change" },
        { "  - work-package: \"WP1.1\"\n    pull-requests: [18]", "  - work-package: \"WP9.9\"\n    pull-requests: [18]", "usage-change" },
        { "  - work-package: \"WP1.2\"\n    pull-requests: [19]", "  - work-package: \"WP1.1\"\n    pull-requests: [18]", "usage-change" },
    };

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task The_valid_sample_ledger_passes_every_rule()
    {
        using var repository = UsageSamples.Repository();

        var result = await CliRunner.RunAsync(repository, "usage", "validate");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("usage validate: passed (0 warnings).", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(SchemaViolations))]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task The_schema_rejects_a_ledger_that_breaks_its_shape(string find, string replace, string message)
    {
        using var repository = UsageSamples.Repository(UsageSamples.Edit((find, replace)));

        var result = await CliRunner.RunAsync(repository, "usage", "validate");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("[usage-schema]", result.Output, StringComparison.Ordinal);
        Assert.Contains(message, result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ConsistencyViolations))]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task A_ledger_that_breaks_a_consistency_rule_fails_with_that_rule(string find, string replace, string rule)
    {
        using var repository = UsageSamples.Repository(UsageSamples.Edit((find, replace)));

        var result = await CliRunner.RunAsync(repository, "usage", "validate");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"error [{rule}]", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task The_same_slice_twice_is_a_duplicate()
    {
        var ledger = UsageSamples.Ledger;
        var start = ledger.IndexOf("  - session: \"aaaaaaaa", StringComparison.Ordinal);
        var end = ledger.IndexOf("  - session: \"bbbbbbbb", StringComparison.Ordinal);
        using var repository = UsageSamples.Repository(ledger + ledger[start..end]);

        var result = await CliRunner.RunAsync(repository, "usage", "validate");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("error [usage-duplicate]", result.Output, StringComparison.Ordinal);
        Assert.Contains("2 records are the same slice", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task A_record_for_a_work_package_that_does_not_exist_names_it_and_the_record()
    {
        using var repository = UsageSamples.Repository(UsageSamples.Edit((FirstDelivery, FirstDelivery.Replace("WP1.1", "WP9.9", StringComparison.Ordinal))));

        var result = await CliRunner.RunAsync(repository, "usage", "validate");

        Assert.Contains("Record 1 (session aaaaaaaa, WP9.9 delivery) names the work package WP9.9, which status.yaml does not have.", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task A_window_that_ends_before_it_starts_says_both_times()
    {
        using var repository = UsageSamples.Repository(UsageSamples.Edit(("to: \"2026-01-05T11:00:00.000Z\"", "to: \"2026-01-05T08:00:00.000Z\"")));

        var result = await CliRunner.RunAsync(repository, "usage", "validate");

        Assert.Contains("a window that ends (2026-01-05T08:00:00.000Z) before it starts (2026-01-05T09:00:00.000Z)", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task Delivery_without_a_work_package_is_allowed_only_in_a_phase_that_is_recorded_at_phase_level()
    {
        var withoutPackage = UsageSamples.Edit(("    activity: planning", "    activity: delivery"));
        var waived = UsageSamples.Edit(withoutPackage, ("updated: 2026-01-10\n", "updated: 2026-01-10\nwaivers:\n  - phase: 1\n    reason: \"The phase is recorded at phase level in this sample.\"\n"));

        using var refused = UsageSamples.Repository(withoutPackage);
        using var allowed = UsageSamples.Repository(waived);

        var withRefusal = await CliRunner.RunAsync(refused, "usage", "validate");
        var withWaiver = await CliRunner.RunAsync(allowed, "usage", "validate");

        Assert.Contains("error [usage-delivery-scope]", withRefusal.Output, StringComparison.Ordinal);
        Assert.Equal(0, withWaiver.ExitCode);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task Fixes_and_planning_are_never_counted_as_the_delivery_of_a_work_package()
    {
        using var repository = UsageSamples.Repository();

        var result = await CliRunner.RunAsync(repository, "usage", "estimate", "--json");

        var observed = System.Text.Json.JsonDocument.Parse(result.Output).RootElement.GetProperty("observed");
        var first = observed.EnumerateArray().Single(item => item.GetProperty("id").GetString() == "WP1.1");

        // 100 + 20.5 for delivery, and the 30 of the fix is left out.
        Assert.Equal(120.5, first.GetProperty("aiu").GetDouble());
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task A_completed_work_package_without_usage_is_a_finding_unless_a_waiver_says_why()
    {
        var status = UsageSamples.Edit(UsageSamples.Status, ("        title: \"Third\"\n        size: M\n        state: planned", "        title: \"Third\"\n        size: M\n        state: completed"));
        var waiver = "updated: 2026-01-10\nwaivers:\n  - work-package: \"WP1.3\"\n    reason: \"Its session was deleted, so nothing can be recovered.\"\n";

        using var missing = UsageSamples.Repository(status: status);
        using var waived = UsageSamples.Repository(UsageSamples.Edit(("updated: 2026-01-10\n", waiver)), status);

        var withoutWaiver = await CliRunner.RunAsync(missing, "usage", "validate");
        var withWaiver = await CliRunner.RunAsync(waived, "usage", "validate");

        Assert.Equal(1, withoutWaiver.ExitCode);
        Assert.Contains("error [usage-missing]", withoutWaiver.Output, StringComparison.Ordinal);
        Assert.Contains("WP1.3 is completed and has no usage in the ledger", withoutWaiver.Output, StringComparison.Ordinal);
        Assert.Equal(0, withWaiver.ExitCode);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task A_waiver_of_a_phase_covers_its_completed_work_packages()
    {
        var status = UsageSamples.Edit(UsageSamples.Status, ("        title: \"Third\"\n        size: M\n        state: planned", "        title: \"Third\"\n        size: M\n        state: completed"));
        var ledger = UsageSamples.Edit(("updated: 2026-01-10\n", "updated: 2026-01-10\nwaivers:\n  - phase: 1\n    reason: \"Phase 1 is recorded at phase level in this sample.\"\n"));
        using var repository = UsageSamples.Repository(ledger, status);

        var result = await CliRunner.RunAsync(repository, "usage", "validate");

        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("work-package: \"WP1.1\"", "usage-waiver-stale", "WP1.1 is waived, and the ledger has usage for it")]
    [InlineData("work-package: \"WP9.9\"", "usage-waiver", "A waiver names the work package WP9.9, which status.yaml does not have")]
    [InlineData("phase: 2", "usage-waiver-empty", "Phase 2 is waived because it is recorded at phase level, and the ledger has no record for it")]
    [InlineData("phase: 3", "usage-waiver", "A waiver names phase 3")]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task A_waiver_that_does_not_hold_is_a_finding(string scope, string rule, string message)
    {
        var waiver = $"updated: 2026-01-10\nwaivers:\n  - {scope}\n    reason: \"A reason that is long enough to be a reason.\"\n";
        using var repository = UsageSamples.Repository(UsageSamples.Edit(("updated: 2026-01-10\n", waiver)));

        var result = await CliRunner.RunAsync(repository, "usage", "validate");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"[{rule}]", result.Output, StringComparison.Ordinal);
        Assert.Contains(message, result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task The_same_work_package_waived_twice_is_a_finding()
    {
        var waivers = "updated: 2026-01-10\nwaivers:\n  - work-package: \"WP1.4\"\n    reason: \"A reason that is long enough to be a reason.\"\n  - work-package: \"WP1.4\"\n    reason: \"Another reason that is long enough as well.\"\n";
        using var repository = UsageSamples.Repository(UsageSamples.Edit(("updated: 2026-01-10\n", waivers)));

        var result = await CliRunner.RunAsync(repository, "usage", "validate");

        Assert.Contains("The ledger waives WP1.4 twice.", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task A_completed_work_package_with_usage_but_no_size_of_change_is_a_warning_and_not_an_error()
    {
        var withoutChange = UsageSamples.Edit(("  - work-package: \"WP1.2\"\n    pull-requests: [19]\n    files: 30\n    additions: 1500\n    deletions: 500\n", string.Empty));
        using var repository = UsageSamples.Repository(withoutChange);

        var result = await CliRunner.RunAsync(repository, "usage", "validate");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("warning [usage-change-missing]", result.Output, StringComparison.Ordinal);
        Assert.Contains("usage validate: passed (1 warnings).", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task A_planned_phase_needs_no_usage_and_a_planned_work_package_may_not_have_any()
    {
        using var repository = UsageSamples.Repository();

        var result = await CliRunner.RunAsync(repository, "usage", "validate");

        // WP1.3, WP1.4, and WP2.1 are planned and have no records, and nothing is said about them.
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("WP2.1", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task A_ledger_that_is_not_valid_yaml_fails_with_the_reason_and_not_a_crash()
    {
        using var repository = UsageSamples.Repository("schema-version: 1\nschema-version: 2\n");

        var result = await CliRunner.RunAsync(repository, "usage", "validate");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("[usage-schema]", result.Output, StringComparison.Ordinal);
        Assert.Contains("docs/project/ai-usage.yaml", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task A_repository_without_a_ledger_fails_the_validation_and_says_which_file_is_missing()
    {
        using var repository = new TestRepository()
            .CopyFromRepository("docs/project/status.schema.json")
            .CopyFromRepository(UsageFile.SchemaPath)
            .Add("docs/project/status.yaml", UsageSamples.Status);

        var result = await CliRunner.RunAsync(repository, "usage", "validate");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("[usage-schema]", result.Output, StringComparison.Ordinal);
        Assert.Contains("docs/project/ai-usage.yaml' does not exist", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task The_summary_option_appends_a_report_for_the_job_summary()
    {
        using var repository = UsageSamples.Repository(UsageSamples.Edit(("aiu: 100.0", "aiu: -1")));
        var summary = Path.Combine(repository.Root, "summary.md");

        await CliRunner.RunAsync(repository, "usage", "validate", "--summary", summary);

        Assert.Contains("### usage validate: failed", File.ReadAllText(summary), StringComparison.Ordinal);
    }
}
