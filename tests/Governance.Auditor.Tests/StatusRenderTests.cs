using Governance.Auditor.Status;
using Governance.Auditor.Tests.Support;
using Xunit;

namespace Governance.Auditor.Tests;

public sealed class StatusRenderTests
{
    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void The_rendered_page_matches_the_reviewed_example()
    {
        var page = StatusRenderer.Render(StatusSamples.Parse(StatusSamples.Valid));

        Golden.AssertMatches(page, "StatusPage.expected.txt");
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void Rendering_the_same_status_twice_gives_the_same_bytes_and_ends_with_one_newline()
    {
        var status = StatusSamples.Parse(StatusSamples.Valid);

        var first = StatusRenderer.Render(status);
        var second = StatusRenderer.Render(status);

        Assert.Equal(first, second);
        Assert.EndsWith("\n", first, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', first);
        Assert.False(first.EndsWith("\n\n", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-003")]
    public void The_resume_table_names_the_branch_the_active_work_package_and_the_next_action()
    {
        var page = StatusRenderer.Render(StatusSamples.Parse(StatusSamples.Valid));

        Assert.Contains("| Branch | `wp/1.2-second` |", page, StringComparison.Ordinal);
        Assert.Contains("| Active work package | WP1.2: Second ([Issue 4](https://github.com/owner/name/issues/4)), in progress |", page, StringComparison.Ordinal);
        Assert.Contains("| Next action | Finish WP1.2 \\| then open a pull request. |", page, StringComparison.Ordinal);
        Assert.Contains("| Open pull requests | None |", page, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-003")]
    public void With_no_active_work_package_the_table_says_none_and_open_pull_requests_are_listed_with_their_links()
    {
        var yaml = StatusSamples.Build(
            ("currentWorkPackage", "null"),
            ("currentBranch", "phase/1-sample"),
            ("openPullRequests", "[{ number: 19, title: \"feat(x): add it\", branch: \"wp/1.2-second\" }]"));

        var page = StatusRenderer.Render(StatusSamples.Parse(yaml));

        Assert.Contains("| Active work package | None |", page, StringComparison.Ordinal);
        Assert.Contains("| Branch | `phase/1-sample` |", page, StringComparison.Ordinal);
        Assert.Contains("[Pull request 19](https://github.com/owner/name/pull/19): feat(x): add it (`wp/1.2-second`)", page, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void Only_phases_that_have_started_get_a_work_package_table_and_the_rest_are_counted()
    {
        var page = StatusRenderer.Render(StatusSamples.Parse(StatusSamples.Valid));

        Assert.Contains("## Phase 1 work packages", page, StringComparison.Ordinal);
        Assert.DoesNotContain("## Phase 2 work packages", page, StringComparison.Ordinal);
        Assert.Contains("phase 2 has 1 work package.", page, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void A_directory_in_the_evidence_is_shown_as_a_path_and_a_file_as_a_link()
    {
        var page = StatusRenderer.Render(StatusSamples.Parse(StatusSamples.Valid));

        Assert.Contains("[docs/evidence/first.md](../evidence/first.md)", page, StringComparison.Ordinal);
        Assert.Contains("`docs/evidence/`", page, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void Every_placeholder_in_the_template_is_filled()
    {
        var page = StatusRenderer.Render(StatusSamples.Parse(StatusSamples.Valid));

        Assert.DoesNotContain("{{", page, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void The_front_matter_carries_the_updated_date_so_freshness_checks_see_it()
    {
        var page = StatusRenderer.Render(StatusSamples.Parse(StatusSamples.Build(("updated", "2027-01-02"))));

        Assert.StartsWith("---\ntitle: \"Project status\"", page, StringComparison.Ordinal);
        Assert.Contains("last-verified: 2027-01-02", page, StringComparison.Ordinal);
    }
}
