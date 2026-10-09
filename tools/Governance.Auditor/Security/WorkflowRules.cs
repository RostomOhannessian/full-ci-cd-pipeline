using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Governance.Auditor.Common;
using Governance.Auditor.Documents;

namespace Governance.Auditor.Security;

/// <summary>A GitHub Actions workflow, as text for line numbers and as a tree for structure.</summary>
internal sealed class WorkflowFile
{
    public const string Directory = ".github/workflows";

    private WorkflowFile(string path, string text, JsonObject? root, string? parseError)
    {
        Path = path;
        Lines = text.ReplaceLineEndings("\n").Split('\n');
        Root = root;
        ParseError = parseError;
    }

    /// <summary>The repository-relative path, for example <c>.github/workflows/ci.yml</c>.</summary>
    public string Path { get; }

    public string FileName => System.IO.Path.GetFileName(Path);

    public IReadOnlyList<string> Lines { get; }

    public JsonObject? Root { get; }

    public string? ParseError { get; }

    public IEnumerable<(string Id, JsonObject Job)> Jobs =>
        Root?["jobs"] is JsonObject jobs
            ? jobs.Where(pair => pair.Value is JsonObject).Select(pair => (pair.Key, (JsonObject)pair.Value!))
            : [];

    public static IReadOnlyList<WorkflowFile> LoadAll(RepositoryFiles files) =>
    [
        .. files.ListFiles(Directory)
            .Where(path => path.Count(character => character == '/') == 2 && (path.EndsWith(".yml", StringComparison.Ordinal) || path.EndsWith(".yaml", StringComparison.Ordinal)))
            .Select(path => Load(path, files.ReadAllText(path))),
    ];

    public static WorkflowFile Load(string path, string text)
    {
        try
        {
            return new WorkflowFile(path, text, YamlDocument.Parse(text, path) as JsonObject, null);
        }
        catch (GovernanceException exception)
        {
            return new WorkflowFile(path, text, null, exception.Message);
        }
    }

    /// <summary>The one-based line where a job is declared, or null when it cannot be found.</summary>
    public int? JobLine(string jobId)
    {
        var jobsLine = -1;

        for (var index = 0; index < Lines.Count && jobsLine < 0; index++)
        {
            if (Lines[index].StartsWith("jobs:", StringComparison.Ordinal))
            {
                jobsLine = index;
            }
        }

        if (jobsLine < 0)
        {
            return null;
        }

        var pattern = new Regex($@"^\s+{Regex.Escape(jobId)}:\s*(?:#.*)?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        for (var index = jobsLine + 1; index < Lines.Count; index++)
        {
            if (pattern.IsMatch(Lines[index]))
            {
                return index + 1;
            }
        }

        return null;
    }

    /// <summary>The <c>uses</c> value of a step, or null when the step runs a command instead.</summary>
    public static string? UsesOf(JsonObject step) => StringOf(step["uses"]);

    public static string? StringOf(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>Every step of every job, with the job it belongs to.</summary>
    public IEnumerable<(string JobId, JsonObject Step)> Steps =>
        Jobs.SelectMany(job => (job.Job["steps"] as JsonArray ?? []).OfType<JsonObject>().Select(step => (job.Id, step)));
}

/// <summary>Reports a workflow that cannot be parsed, because every other workflow rule would otherwise skip it without a word.</summary>
internal sealed class WorkflowParseRule : SyncSecurityRule
{
    public override string Id => "workflow-parse";

    protected override IReadOnlyList<Finding> Evaluate(SecurityContext context) =>
        [.. context.Workflows.Where(workflow => workflow.ParseError is not null).Select(workflow => Finding.Error(Id, workflow.ParseError!, workflow.Path))];
}

/// <summary>
/// The token has no more access than a job needs (plan section 9). Every workflow sets top-level permissions to read-only or none, every
/// job states its permissions explicitly, and a write scope needs an entry in the policy that says why.
/// </summary>
internal sealed class WorkflowPermissionsRule : SyncSecurityRule
{
    public override string Id => "workflow-permissions";

    protected override IReadOnlyList<Finding> Evaluate(SecurityContext context)
    {
        List<Finding> findings = [];

        foreach (var workflow in context.Workflows.Where(workflow => workflow.Root is not null))
        {
            var topLevel = workflow.Root!["permissions"];

            if (topLevel is null)
            {
                findings.Add(Finding.Error(Id, "The workflow sets no top-level 'permissions'. Add 'permissions: {}' so that no job inherits a default token.", workflow.Path));
            }
            else
            {
                CheckBlock(topLevel, workflow, "the top level", null, context.Policy, findings, line: null);
            }

            foreach (var (jobId, job) in workflow.Jobs)
            {
                if (job["permissions"] is null)
                {
                    findings.Add(Finding.Error(Id, $"The job '{jobId}' sets no permissions. Set 'permissions: {{}}' or list the scopes it needs, so its token is explicit.", workflow.Path, workflow.JobLine(jobId)));
                }
                else
                {
                    CheckBlock(job["permissions"]!, workflow, $"the job '{jobId}'", jobId, context.Policy, findings, workflow.JobLine(jobId));
                }
            }
        }

        return findings;
    }

    private void CheckBlock(JsonNode permissions, WorkflowFile workflow, string where, string? jobId, SecurityPolicy policy, List<Finding> findings, int? line)
    {
        if (permissions is JsonValue value)
        {
            var text = value.GetValueKind() == System.Text.Json.JsonValueKind.String ? value.GetValue<string>() : string.Empty;

            if (text is "write-all" or "read-all")
            {
                findings.Add(Finding.Error(Id, $"{Capitalize(where)} sets '{text}', which grants every scope. List only the scopes that are needed.", workflow.Path, line));
            }

            return;
        }

        if (permissions is not JsonObject scopes)
        {
            return;
        }

        foreach (var (scope, access) in scopes)
        {
            if (access is not JsonValue accessValue || accessValue.GetValueKind() != System.Text.Json.JsonValueKind.String || accessValue.GetValue<string>() != "write")
            {
                continue;
            }

            var allowed = jobId is not null && policy.Workflows.AllowedWritePermissions.Any(entry =>
                string.Equals(entry.Workflow, workflow.FileName, StringComparison.Ordinal)
                && string.Equals(entry.Job, jobId, StringComparison.Ordinal)
                && string.Equals(entry.Permission, scope, StringComparison.Ordinal));

            if (!allowed)
            {
                findings.Add(Finding.Error(Id, $"{Capitalize(where)} grants write access to '{scope}'. A write scope belongs to one job and needs an entry with a reason in {SecurityPolicy.Path}.", workflow.Path, line));
            }
        }
    }

    private static string Capitalize(string text) => string.Concat(text[..1].ToUpperInvariant(), text.AsSpan(1));
}

/// <summary>
/// Every action, reusable workflow, and container image is pinned to an immutable reference (plan section 9). A tag can be moved to
/// malicious code, as the tj-actions and trivy-action incidents showed, but a commit SHA or an image digest cannot.
/// </summary>
internal sealed partial class ActionPinningRule : SyncSecurityRule
{
    [GeneratedRegex("""^\s*(?:-\s+)?uses:\s*(?<quote>["']?)(?<reference>[^\s"'#]+)\k<quote>\s*(?:#\s*(?<comment>.*))?$""", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UsesPattern();

    [GeneratedRegex(@"^[0-9a-f]{40}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CommitShaPattern();

    [GeneratedRegex(@"@sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DigestPattern();

    [GeneratedRegex(@"^v?\d+(?:\.\d+)*\S*", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex VersionCommentPattern();

    public override string Id => "action-pinning";

    protected override IReadOnlyList<Finding> Evaluate(SecurityContext context)
    {
        List<Finding> findings = [];

        foreach (var workflow in context.Workflows)
        {
            CheckUses(workflow, findings);
            CheckImages(workflow, findings);
        }

        return findings;
    }

    private void CheckUses(WorkflowFile workflow, List<Finding> findings)
    {
        for (var index = 0; index < workflow.Lines.Count; index++)
        {
            if (UsesPattern().Match(workflow.Lines[index]) is not { Success: true } match)
            {
                continue;
            }

            var reference = match.Groups["reference"].Value;
            var line = index + 1;

            if (reference.StartsWith("./", StringComparison.Ordinal))
            {
                continue;
            }

            if (reference.StartsWith("docker://", StringComparison.Ordinal))
            {
                if (!DigestPattern().IsMatch(reference))
                {
                    findings.Add(Finding.Error(Id, $"The container action '{reference}' is not pinned by digest. Use image@sha256:<digest>.", workflow.Path, line));
                }

                continue;
            }

            var at = reference.LastIndexOf('@');

            if (at < 0 || !CommitShaPattern().IsMatch(reference[(at + 1)..]))
            {
                findings.Add(Finding.Error(Id, $"'{reference}' is not pinned to a full 40-character commit SHA. A tag or branch can be moved to different code.", workflow.Path, line));
                continue;
            }

            if (!match.Groups["comment"].Success || !VersionCommentPattern().IsMatch(match.Groups["comment"].Value.Trim()))
            {
                findings.Add(Finding.Warning(Id, $"'{reference[..at]}' is pinned, but has no version comment beside it. Add '# vX.Y.Z' so a reviewer can read the version.", workflow.Path, line));
            }
        }
    }

    private void CheckImages(WorkflowFile workflow, List<Finding> findings)
    {
        foreach (var (jobId, job) in workflow.Jobs)
        {
            var images = new List<(string Where, string Image)>();

            switch (job["container"])
            {
                case JsonValue text when WorkflowFile.StringOf(text) is { } containerImage:
                    images.Add(("container", containerImage));
                    break;
                case JsonObject container when WorkflowFile.StringOf(container["image"]) is { } objectImage:
                    images.Add(("container", objectImage));
                    break;
            }

            if (job["services"] is JsonObject services)
            {
                foreach (var (name, service) in services)
                {
                    if (service is JsonObject serviceObject && WorkflowFile.StringOf(serviceObject["image"]) is { } serviceImage)
                    {
                        images.Add(($"service '{name}'", serviceImage));
                    }
                }
            }

            foreach (var (where, image) in images.Where(entry => !DigestPattern().IsMatch(entry.Image)))
            {
                findings.Add(Finding.Error(Id, $"The {where} image '{image}' in job '{jobId}' is not pinned by digest. Use image@sha256:<digest>.", workflow.Path, workflow.JobLine(jobId)));
            }
        }
    }
}

/// <summary>
/// A workflow uses no secret except the platform token (plan section 8.8). Cloud and broker access comes from OIDC, so a stored secret
/// in GitHub would be a standing credential that the project does not want.
/// </summary>
internal sealed partial class WorkflowSecretsRule : SyncSecurityRule
{
    [GeneratedRegex(@"\bsecrets\.(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NamedSecretPattern();

    [GeneratedRegex(@"\bsecrets\s*\[", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex IndexedSecretPattern();

    [GeneratedRegex(@"^\s*secrets:\s*inherit\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex InheritPattern();

    public override string Id => "workflow-secrets";

    protected override IReadOnlyList<Finding> Evaluate(SecurityContext context)
    {
        List<Finding> findings = [];

        foreach (var workflow in context.Workflows)
        {
            for (var index = 0; index < workflow.Lines.Count; index++)
            {
                var text = workflow.Lines[index];

                if (text.TrimStart().StartsWith('#'))
                {
                    continue;
                }

                foreach (Match match in NamedSecretPattern().Matches(text))
                {
                    var name = match.Groups["name"].Value;

                    if (!context.Policy.Workflows.AllowedSecrets.Contains(name, StringComparer.Ordinal))
                    {
                        findings.Add(Finding.Error(Id, $"The workflow uses 'secrets.{name}'. Only the platform token is allowed, and other access comes from OIDC (plan section 8.8).", workflow.Path, index + 1));
                    }
                }

                if (IndexedSecretPattern().IsMatch(text))
                {
                    findings.Add(Finding.Error(Id, "The workflow reads a secret by a computed name, which cannot be checked. Name each secret you need.", workflow.Path, index + 1));
                }

                if (InheritPattern().IsMatch(text))
                {
                    findings.Add(Finding.Error(Id, "'secrets: inherit' passes every secret to a reusable workflow. Pass nothing, or name what is needed.", workflow.Path, index + 1));
                }
            }
        }

        return findings;
    }
}

/// <summary>A checkout does not leave the token in the Git configuration, where a later step could read or push with it.</summary>
internal sealed class CheckoutCredentialsRule : SyncSecurityRule
{
    public override string Id => "checkout-credentials";

    protected override IReadOnlyList<Finding> Evaluate(SecurityContext context)
    {
        List<Finding> findings = [];

        foreach (var workflow in context.Workflows)
        {
            foreach (var (jobId, step) in workflow.Steps.Where(entry => WorkflowFile.UsesOf(entry.Step)?.StartsWith("actions/checkout@", StringComparison.Ordinal) == true))
            {
                var persist = (step["with"] as JsonObject)?["persist-credentials"];
                var isFalse = persist is JsonValue value
                    && (value.GetValueKind() == System.Text.Json.JsonValueKind.False
                        || (value.GetValueKind() == System.Text.Json.JsonValueKind.String && string.Equals(value.GetValue<string>(), "false", StringComparison.OrdinalIgnoreCase)));

                if (!isFalse)
                {
                    findings.Add(Finding.Error(Id, $"A checkout step in job '{jobId}' does not set 'persist-credentials: false', so the token stays in the Git configuration.", workflow.Path, workflow.JobLine(jobId)));
                }
            }
        }

        return findings;
    }
}

/// <summary>
/// <c>pull_request_target</c> runs with a write token and secrets in the context of the base repository. It must never check out or run
/// code from the pull request, so every use needs an entry in the policy that says why it is safe.
/// </summary>
internal sealed class PullRequestTargetRule : SyncSecurityRule
{
    public override string Id => "pull-request-target";

    protected override IReadOnlyList<Finding> Evaluate(SecurityContext context)
    {
        List<Finding> findings = [];

        foreach (var workflow in context.Workflows.Where(workflow => workflow.Root is not null && Triggers(workflow.Root)))
        {
            var allowed = context.Policy.Workflows.AllowedPullRequestTarget.Any(entry => string.Equals(entry.Workflow, workflow.FileName, StringComparison.Ordinal));

            if (!allowed)
            {
                findings.Add(Finding.Error(Id, $"The workflow uses the 'pull_request_target' trigger, which runs with a write token. Use 'pull_request', or add an entry with a reason to {SecurityPolicy.Path}.", workflow.Path));
            }
        }

        return findings;
    }

    private static bool Triggers(JsonObject root) => root["on"] switch
    {
        JsonValue value => value.GetValueKind() == System.Text.Json.JsonValueKind.String && value.GetValue<string>() == "pull_request_target",
        JsonArray list => list.Any(item => item is JsonValue value && value.GetValueKind() == System.Text.Json.JsonValueKind.String && value.GetValue<string>() == "pull_request_target"),
        JsonObject events => events.ContainsKey("pull_request_target"),
        _ => false,
    };
}

/// <summary>Timeouts and artifact retention are explicit, so a stuck job or a forgotten artifact cannot run or linger by default (REQ-CI-004).</summary>
internal sealed class WorkflowLimitsRule : SyncSecurityRule
{
    public override string Id => "workflow-limits";

    protected override IReadOnlyList<Finding> Evaluate(SecurityContext context)
    {
        List<Finding> findings = [];

        foreach (var workflow in context.Workflows)
        {
            // A job that calls a reusable workflow cannot set a timeout. The called workflow's jobs are checked in their own file.
            foreach (var (jobId, job) in workflow.Jobs.Where(entry => entry.Job["uses"] is null && entry.Job["timeout-minutes"] is null))
            {
                findings.Add(Finding.Error(Id, $"The job '{jobId}' sets no 'timeout-minutes'. The default is six hours, so give every job an explicit limit.", workflow.Path, workflow.JobLine(jobId)));
            }

            foreach (var (jobId, step) in workflow.Steps.Where(entry => WorkflowFile.UsesOf(entry.Step)?.StartsWith("actions/upload-artifact@", StringComparison.Ordinal) == true))
            {
                if ((step["with"] as JsonObject)?["retention-days"] is null)
                {
                    findings.Add(Finding.Error(Id, $"An upload-artifact step in job '{jobId}' sets no 'retention-days', so the artifact lingers for the repository default.", workflow.Path, workflow.JobLine(jobId)));
                }
            }
        }

        return findings;
    }
}
