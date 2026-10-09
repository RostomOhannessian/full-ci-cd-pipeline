using Governance.Auditor.Documents;
using Governance.Auditor.Status;

namespace Governance.Auditor.Tests.Support;

/// <summary>
/// A small, valid <c>status.yaml</c> with named placeholders, so a test changes one fact by name instead of editing text. A placeholder
/// that a test names but the template lacks is an error, so a typo cannot make a test pass without testing anything.
/// </summary>
internal static class StatusSamples
{
    private const string Template = """
        schema-version: 1
        updated: "{{updated}}"
        project:
          name: "Sample project"
          repository: "owner/name"
          plan: "docs/plans/plan.md"
          plan-version: "1.0.0"
        current:
          phase: {{currentPhase}}
          work-package: {{currentWorkPackage}}
          branch: "{{currentBranch}}"
        resume:
          last-completed: "WP1.1 is complete."
          next-action: "Finish WP1.2 | then open a pull request."
          open-pull-requests: {{openPullRequests}}
          environment-notes:
            - "Use the noreply identity."
        phases:
          - id: 1
            name: "Sample phase"
            state: {{phase1State}}
            branch: "{{phase1Branch}}"
            plan: "docs/plans/phase-1.md"
            milestone: "Phase 1: Sample"
            milestone-url: "https://github.com/owner/name/milestone/2"
            release: "v0.1.0"
            issue: null
            pull-request: null
            work-packages:
              - id: "{{wp11Id}}"
                title: "First"
                size: S
                state: {{wp11State}}
                branch: "{{wp11Branch}}"
                issue: {{wp11Issue}}
                pull-request: 18
                depends-on: {{wp11DependsOn}}
                evidence: {{wp11Evidence}}
                blockers: []
              - id: "WP1.2"
                title: "Second"
                size: M
                state: {{wp12State}}
                branch: {{wp12Branch}}
                issue: 4
                pull-request: null
                depends-on: {{wp12DependsOn}}
                evidence: []
                blockers: {{wp12Blockers}}
          - id: 2
            name: "Later phase"
            state: {{phase2State}}
            branch: "phase/2-later"
            plan: "docs/plans/phase-2.md"
            milestone: "Phase 2: Later"
            milestone-url: null
            release: "v0.2.0"
            issue: null
            pull-request: null
            work-packages:
              - id: "{{wp21Id}}"
                title: "Third"
                size: L
                state: {{wp21State}}
                branch: null
                issue: null
                pull-request: null
                depends-on: ["WP1.2"]
                evidence: []
                blockers: []
        """;

    private static readonly Dictionary<string, string> Defaults = new(StringComparer.Ordinal)
    {
        ["updated"] = "2026-10-08",
        ["currentPhase"] = "1",
        ["currentWorkPackage"] = "\"WP1.2\"",
        ["currentBranch"] = "wp/1.2-second",
        ["openPullRequests"] = "[]",
        ["phase1State"] = "in_progress",
        ["phase1Branch"] = "phase/1-sample",
        ["phase2State"] = "planned",
        ["wp11Id"] = "WP1.1",
        ["wp11State"] = "completed",
        ["wp11Branch"] = "wp/1.1-first",
        ["wp11Issue"] = "3",
        ["wp11DependsOn"] = "[]",
        ["wp11Evidence"] = "[\"docs/evidence/first.md\", \"docs/evidence/\"]",
        ["wp12State"] = "in_progress",
        ["wp12Branch"] = "\"wp/1.2-second\"",
        ["wp12DependsOn"] = "[\"WP1.1\"]",
        ["wp12Blockers"] = "[]",
        ["wp21Id"] = "WP2.1",
        ["wp21State"] = "planned",
    };

    public static string Valid => Build();

    /// <summary>Builds the sample with the named values changed. Each override is a placeholder name and the YAML text to put there.</summary>
    public static string Build(params (string Name, string Value)[] overrides)
    {
        var values = new Dictionary<string, string>(Defaults, StringComparer.Ordinal);

        foreach (var (name, value) in overrides)
        {
            values[name] = values.ContainsKey(name) ? value : throw new InvalidOperationException($"The sample has no placeholder '{name}'.");
        }

        return values.Aggregate(Template, (text, pair) => text.Replace("{{" + pair.Key + "}}", pair.Value, StringComparison.Ordinal));
    }

    public static StatusDocument Parse(string yaml) =>
        DocumentJson.Deserialize<StatusDocument>(YamlDocument.Parse(yaml, "status.yaml")!, "status.yaml");
}
