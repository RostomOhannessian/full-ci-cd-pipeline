using Governance.Auditor.Status;
using Governance.Auditor.Trace;

namespace Governance.Auditor.Tests.Support;

/// <summary>Small requirement and evidence registers, and a repository to run <c>trace</c> against.</summary>
internal static class TraceSamples
{
    private const string Areas = """
        areas:
          ARC: Architecture and layering
          DOM: Domain model
          APP: Application layer
          API: HTTP API and contract
          DATA: Persistence and data
          CACHE: Caching
          SEC: Application and platform security
          IDN: Identity and authorization
          OBS: Observability
          QUA: Quality and testing
          CI: Continuous integration
          SUP: Supply chain
          INF: Infrastructure as code and platform
          NET: Network segmentation
          POL: Runtime policy
          GIT: GitOps and delivery
          CON: Contracts and consumer testing
          DOC: Documentation and learning
          DX: Developer experience and portability
          GOV: Governance and project management
          AI: AI enablement
        """;

    public static string RequirementsYaml(params (string Id, string[] DeliveredBy, string Status)[] requirements)
    {
        var blocks = requirements.Select(requirement => $"""
            - id: {requirement.Id}
              title: Requirement {requirement.Id}
              statement: The system satisfies requirement {requirement.Id} in a way a test can check.
              source: plan
              delivered-by: [{string.Join(", ", requirement.DeliveredBy)}]
              verification: [unit]
              status: {requirement.Status}
            """);

        return $"schema-version: 1\nlast-verified: '2026-10-08'\n{Areas}\nrequirements:\n{string.Join("\n", blocks)}\n";
    }

    public static string EvidenceYaml(params (string Requirement, string Kind, string Path)[] entries)
    {
        if (entries.Length == 0)
        {
            return "schema-version: 1\nlast-verified: '2026-10-08'\nentries: []\n";
        }

        var blocks = entries.Select(entry => $"""
            - requirements: [{entry.Requirement}]
              kind: {entry.Kind}
              path: {entry.Path}
              summary: This file shows that the requirement holds, as a reviewer can read.
              recorded: '2026-10-08'
            """);

        return $"schema-version: 1\nlast-verified: '2026-10-08'\nentries:\n{string.Join("\n", blocks)}\n";
    }

    /// <summary>A repository with the real schemas, the sample status file (WP1.1 is completed), and the given registers.</summary>
    public static TestRepository Repository(string requirementsYaml, string evidenceYaml)
    {
        return new TestRepository()
            .CopyFromRepository(StatusFile.SchemaPath)
            .CopyFromRepository(TraceFiles.RequirementsSchemaPath)
            .CopyFromRepository(TraceFiles.EvidenceSchemaPath)
            .Add(StatusFile.DocumentPath, StatusSamples.Valid)
            .Add(TraceFiles.RequirementsPath, requirementsYaml)
            .Add(TraceFiles.EvidencePath, evidenceYaml)
            .Add("docs/evidence/first.md", "Evidence.");
    }

    public static Requirement Requirement(string id, string[] deliveredBy, string status = "planned") => new()
    {
        Id = id,
        Title = $"Requirement {id}",
        Statement = $"The system satisfies requirement {id} in a way a test can check.",
        Source = "plan",
        DeliveredBy = deliveredBy,
        Verification = ["unit"],
        Status = status,
    };

    public static RequirementsRegister Register(params Requirement[] requirements) => new()
    {
        SchemaVersion = 1,
        LastVerified = "2026-10-08",
        Areas = new Dictionary<string, string> { ["GOV"] = "Governance and project management", ["QUA"] = "Quality and testing" },
        Requirements = requirements,
    };

    public static EvidenceRegister Evidence(params EvidenceEntry[] entries) => new() { SchemaVersion = 1, LastVerified = "2026-10-08", Entries = entries };

    public static EvidenceEntry Entry(string requirement, string path, string kind = "manual") => new()
    {
        Requirements = [requirement],
        Kind = kind,
        Path = path,
        Summary = "This file shows that the requirement holds.",
        Recorded = "2026-10-08",
    };
}
