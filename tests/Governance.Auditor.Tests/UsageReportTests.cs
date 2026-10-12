using System.Text.Json;
using Governance.Auditor.Tests.Support;
using Governance.Auditor.Usage;
using Xunit;

namespace Governance.Auditor.Tests;

public sealed class UsageRenderTests
{
    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task The_rendered_report_matches_the_reviewed_example()
    {
        using var repository = UsageSamples.Repository();

        var result = await CliRunner.RunAsync(repository, "usage", "render");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("usage render: wrote docs/project/AI-USAGE.md.", result.Output, StringComparison.Ordinal);
        Golden.AssertMatches(repository.Read(UsageFile.PagePath), "usage/AI-USAGE.expected.txt");
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task Rendering_twice_gives_the_same_bytes_with_one_trailing_newline_and_no_carriage_return()
    {
        using var repository = UsageSamples.Repository();

        await CliRunner.RunAsync(repository, "usage", "render");
        var first = repository.Read(UsageFile.PagePath);
        await CliRunner.RunAsync(repository, "usage", "render");
        var second = repository.Read(UsageFile.PagePath);

        Assert.Equal(first, second);
        Assert.EndsWith("\n", first, StringComparison.Ordinal);
        Assert.False(first.EndsWith("\n\n", StringComparison.Ordinal));
        Assert.DoesNotContain('\r', first);
        Assert.DoesNotContain("\n\n\n", first, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task The_report_names_a_session_by_its_first_eight_characters_only()
    {
        using var repository = UsageSamples.Repository();

        await CliRunner.RunAsync(repository, "usage", "render");
        var page = repository.Read(UsageFile.PagePath);

        Assert.Contains("`aaaaaaaa`", page, StringComparison.Ordinal);
        Assert.DoesNotContain("aaaaaaaa-aaaa", page, StringComparison.Ordinal);
        Assert.DoesNotContain("A session that held this work package alone", page, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task The_report_shows_the_totals_by_phase_work_package_model_and_effort()
    {
        using var repository = UsageSamples.Repository();

        await CliRunner.RunAsync(repository, "usage", "render");
        var page = repository.Read(UsageFile.PagePath);

        foreach (var heading in new[] { "## Totals", "## By phase", "## By work package", "## Observed AIU by size class", "## By model and effort", "## By session" })
        {
            Assert.Contains(heading, page, StringComparison.Ordinal);
        }

        // 100 + 20.5 + 400 + 50 + 30 AIU, of which 520.5 is delivery and 50 is planning.
        Assert.Contains("| 1 | Sample phase | 521 | 50 | 0 | 30 | 601 | 52 |", page, StringComparison.Ordinal);
        Assert.Contains("| WP1.1 | S | First | 121 | 14 |", page, StringComparison.Ordinal);
        Assert.Contains("| `model-a` | xhigh | main | 43 |", page, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task Check_passes_on_a_current_page_and_fails_on_a_page_that_differs_from_the_ledger()
    {
        using var repository = UsageSamples.Repository();
        await CliRunner.RunAsync(repository, "usage", "render");

        var current = await CliRunner.RunAsync(repository, "usage", "render", "--check");

        var page = repository.Read(UsageFile.PagePath);
        repository.Add(UsageFile.PagePath, page.Replace("| Sessions | 2 |", "| Sessions | 3 |", StringComparison.Ordinal));
        var drifted = await CliRunner.RunAsync(repository, "usage", "render", "--check");

        Assert.Equal(0, current.ExitCode);
        Assert.Equal(1, drifted.ExitCode);
        Assert.Contains("error [usage-drift]", drifted.Output, StringComparison.Ordinal);
        Assert.Contains("docs/project/AI-USAGE.md:", drifted.Output, StringComparison.Ordinal);
        Assert.Contains("Run 'governance usage render' and commit the result.", drifted.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task Check_fails_when_the_page_is_missing_and_never_writes_it()
    {
        using var repository = UsageSamples.Repository();

        var result = await CliRunner.RunAsync(repository, "usage", "render", "--check");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("error [usage-drift]", result.Output, StringComparison.Ordinal);
        Assert.False(repository.Exists(UsageFile.PagePath));
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task A_ledger_that_is_not_valid_is_not_rendered()
    {
        using var repository = UsageSamples.Repository(UsageSamples.Edit(("to: \"2026-01-05T11:00:00.000Z\"", "to: \"2026-01-05T08:00:00.000Z\"")));

        var result = await CliRunner.RunAsync(repository, "usage", "render");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("usage render: failed", result.Output, StringComparison.Ordinal);
        Assert.False(repository.Exists(UsageFile.PagePath));
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task The_output_option_writes_to_another_page()
    {
        using var repository = UsageSamples.Repository();

        var result = await CliRunner.RunAsync(repository, "usage", "render", "--output", "docs/project/other.md");

        Assert.Equal(0, result.ExitCode);
        Assert.True(repository.Exists("docs/project/other.md"));
        Assert.False(repository.Exists(UsageFile.PagePath));
    }
}

public sealed class UsageEstimateTests
{
    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task The_estimate_matches_the_reviewed_example_and_prints_its_method_and_caveats()
    {
        using var repository = UsageSamples.Repository();

        var result = await CliRunner.RunAsync(repository, "usage", "estimate");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("  method:", result.Output, StringComparison.Ordinal);
        Assert.Contains("  caveats:", result.Output, StringComparison.Ordinal);
        Golden.AssertMatches(result.Output, "usage/estimate.expected.txt");
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task A_size_with_no_completed_work_package_says_so_and_is_scaled_from_the_others()
    {
        using var repository = UsageSamples.Repository();

        var result = await CliRunner.RunAsync(repository, "usage", "estimate");

        Assert.Contains("    L: no completed work package of this size", result.Output, StringComparison.Ordinal);
        Assert.Contains("WP2.1 (L, planned): 867 to 1,567, median 1,217 (scaled by weight, no work package of this size is completed)", result.Output, StringComparison.Ordinal);
        Assert.Contains("No completed work package has the size L", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task A_size_with_completed_work_packages_is_estimated_from_their_range()
    {
        using var repository = UsageSamples.Repository();

        var result = await CliRunner.RunAsync(repository, "usage", "estimate", "--json");

        var phases = JsonDocument.Parse(result.Output).RootElement.GetProperty("phases").EnumerateArray().ToList();
        var items = phases.SelectMany(phase => phase.GetProperty("workPackages").EnumerateArray()).ToDictionary(item => item.GetProperty("id").GetString()!);

        // One completed S (120.5 AIU) and one completed M (400 AIU), so a single observation is a single point.
        Assert.Equal("by-size", items["WP1.3"].GetProperty("method").GetString());
        Assert.Equal(400, items["WP1.3"].GetProperty("low").GetDouble());
        Assert.Equal(400, items["WP1.3"].GetProperty("high").GetDouble());
        Assert.Equal(120.5, items["WP1.4"].GetProperty("median").GetDouble());
        Assert.Equal("scaled-by-weight", items["WP2.1"].GetProperty("method").GetString());
        Assert.Equal(0, items["WP2.1"].GetProperty("observations").GetInt32());
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task A_size_with_one_observation_says_that_the_range_is_a_single_point()
    {
        using var repository = UsageSamples.Repository();

        var result = await CliRunner.RunAsync(repository, "usage", "estimate");

        Assert.Contains("WP1.3 (M, planned): 400 to 400, median 400 (by size, 1 observed, so the range is a single point)", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task A_phase_is_the_sum_of_its_work_packages()
    {
        using var repository = UsageSamples.Repository();

        var result = await CliRunner.RunAsync(repository, "usage", "estimate", "--json");

        var phase = JsonDocument.Parse(result.Output).RootElement.GetProperty("phases").EnumerateArray().First();

        // WP1.3 is an M (400) and WP1.4 is an S (120.5).
        Assert.Equal(520.5, phase.GetProperty("low").GetDouble());
        Assert.Equal(520.5, phase.GetProperty("median").GetDouble());
        Assert.Equal(520.5, phase.GetProperty("high").GetDouble());
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task The_phase_option_limits_the_estimate_and_an_unknown_phase_is_refused()
    {
        using var repository = UsageSamples.Repository();

        var limited = await CliRunner.RunAsync(repository, "usage", "estimate", "--phase", "2");
        var unknown = await CliRunner.RunAsync(repository, "usage", "estimate", "--phase", "4");

        Assert.Equal(0, limited.ExitCode);
        Assert.Contains("  phase 2: Later phase", limited.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("  phase 1:", limited.Output, StringComparison.Ordinal);
        Assert.Equal(1, unknown.ExitCode);
        Assert.Contains("status.yaml has no phase '4'", unknown.Error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task With_no_completed_work_package_the_estimate_says_that_there_is_nothing_to_estimate_from()
    {
        const string ledger = """
            schema-version: 1
            updated: 2026-01-10
            waivers:
              - phase: 1
                reason: "Phase 1 is recorded at phase level in this sample."
            records:
              - session: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"
                phase: 1
                activity: planning
                confidence: windowed
                window:
                  from: "2026-01-06T13:00:00.000Z"
                  to: "2026-01-06T14:00:00.000Z"
                user-turns: 1
                compactions: 0
                usage:
                  - model: model-a
                    reasoning-effort: high
                    agent: main
                    calls: 5
                    aiu: 50.0
                    fresh-input-tokens: 500
                    cache-read-tokens: 100000
                    cache-write-tokens: 10000
                    output-tokens: 3000
                    reasoning-tokens: 500
                    peak-context-tokens: 30000
                    model-seconds: 60
            """;
        using var repository = UsageSamples.Repository(ledger);

        var result = await CliRunner.RunAsync(repository, "usage", "estimate");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("observed: 0 completed work packages with delivery usage", result.Output, StringComparison.Ordinal);
        Assert.Contains("there is nothing to estimate from", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("  phase 1:", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task The_estimate_has_no_randomness_so_two_runs_agree()
    {
        using var repository = UsageSamples.Repository();

        var first = await CliRunner.RunAsync(repository, "usage", "estimate");
        var second = await CliRunner.RunAsync(repository, "usage", "estimate");
        var firstJson = await CliRunner.RunAsync(repository, "usage", "estimate", "--json");
        var secondJson = await CliRunner.RunAsync(repository, "usage", "estimate", "--json");

        Assert.Equal(first.Output, second.Output);
        Assert.Equal(firstJson.Output, secondJson.Output);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task The_json_names_the_unit_the_weights_and_the_caveats()
    {
        using var repository = UsageSamples.Repository();

        var result = await CliRunner.RunAsync(repository, "usage", "estimate", "--json");

        var root = JsonDocument.Parse(result.Output).RootElement;
        Assert.Equal("AIU", root.GetProperty("unit").GetString());
        Assert.Equal(0.5, root.GetProperty("weights").GetProperty("S").GetDouble());
        Assert.Equal(3, root.GetProperty("weights").GetProperty("M").GetDouble());
        Assert.Equal(6.5, root.GetProperty("weights").GetProperty("L").GetDouble());
        Assert.True(root.GetProperty("caveats").GetArrayLength() >= 4);
        Assert.DoesNotContain('\n', result.Output.TrimEnd('\n'));
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task An_estimate_of_a_ledger_that_is_not_valid_is_refused()
    {
        using var repository = UsageSamples.Repository(UsageSamples.Edit(("aiu: 100.0", "aiu: -1")));

        var result = await CliRunner.RunAsync(repository, "usage", "estimate");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("usage estimate: failed", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new[] { 5 }, 5)]
    [InlineData(new[] { 1, 9, 5 }, 5)]
    [InlineData(new[] { 1, 2, 3, 10 }, 2.5)]
    [Trait("Requirement", "REQ-AI-005")]
    public void The_median_is_the_middle_value_or_the_mean_of_the_two_in_the_middle(int[] values, double expected)
    {
        Assert.Equal((decimal)expected, UsageAnalysis.Median(values.Select(value => (decimal)value)));
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public void The_median_of_nothing_is_not_defined()
    {
        Assert.Throws<InvalidOperationException>(() => UsageAnalysis.Median([]));
    }

    [Theory]
    [InlineData("S", 0.5)]
    [InlineData("M", 3)]
    [InlineData("L", 6.5)]
    [Trait("Requirement", "REQ-AI-005")]
    public void The_weights_are_the_midpoints_of_the_size_classes_of_plan_section_5_7(string size, double expected)
    {
        Assert.Equal((decimal)expected, UsageVocabulary.Weight(size));
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public void A_size_that_is_not_a_class_has_no_weight()
    {
        Assert.Throws<Governance.Auditor.Common.GovernanceException>(() => UsageVocabulary.Weight("XL"));
    }
}
