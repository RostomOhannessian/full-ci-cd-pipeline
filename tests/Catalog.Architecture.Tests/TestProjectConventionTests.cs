using System.Globalization;
using Xunit;

namespace Catalog.Architecture.Tests;

/// <summary>
/// Determinism rules that apply to every test project (plan section 10.3). Each test project declares its parallelism, and the
/// shared test props set the invariant culture, so a failure on one host can be replayed on another.
/// </summary>
public sealed class TestProjectConventionTests
{
    [Fact]
    [Trait("Requirement", "REQ-QUA-004")]
    public void Every_test_project_declares_its_parallelism_in_xunit_runner_json()
    {
        var missing = SolutionLayout.ProjectFilesOnDisk()
            .Where(path => path.StartsWith("tests/", StringComparison.Ordinal))
            .Select(path => Path.GetDirectoryName(Path.Combine(SolutionLayout.Root, path))!)
            .Where(directory => !File.Exists(Path.Combine(directory, "xunit.runner.json")))
            .Select(directory => Path.GetRelativePath(SolutionLayout.Root, directory).Replace('\\', '/'))
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-004")]
    public void Tests_run_under_the_invariant_culture()
    {
        Assert.Equal(CultureInfo.InvariantCulture, CultureInfo.CurrentCulture);
    }
}
