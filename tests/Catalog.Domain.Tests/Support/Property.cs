using CsCheck;

namespace Catalog.Domain.Tests.Support;

/// <summary>
/// Runs CsCheck property tests the same way everywhere: one thread, so the parallelism of the assembly stays the one that <c>xunit.runner.json</c> declares,
/// and 500 iterations unless <c>CsCheck_Iter</c> says otherwise. CsCheck starts each run from a random seed and names the seed of a failing case in its
/// message, such as <c>Set seed: "q3Lk9mZx0RaB" or -e CsCheck_Seed=q3Lk9mZx0RaB to reproduce</c>. Setting <c>CsCheck_Seed</c> to that value replays the
/// failing case first, on any host, so a failure from CI can be debugged locally (plan section 10.3, REQ-QUA-004).
/// </summary>
internal static class Property
{
    private const int DefaultIterations = 500;

    public static void Check<T>(Gen<T> generator, Action<T> assertion)
    {
        var iterations = Environment.GetEnvironmentVariable("CsCheck_Iter") is null ? DefaultIterations : -1;
        generator.Sample(assertion, iter: iterations, threads: 1);
    }
}
