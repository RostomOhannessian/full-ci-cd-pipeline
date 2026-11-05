using Xunit;

namespace Catalog.Api.Tests.Support;

/// <summary>
/// The tests that read the exported series of a host and expect the series of the catalog to be there. The metrics SDK identifies a stream by the name of the meter and of the instrument and not by the instance of the
/// meter, so when another host of the test process disposes its meters, the stream that this host shares with it goes inactive and its series are gone from the next collection. A production process has one host, so the
/// effect belongs to the test process. A collection that disables parallelization runs these classes alone, with no other host created or disposed while they read.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ExportedMetricsAlone
{
    /// <summary>The name of the collection.</summary>
    public const string Name = "Exported metrics of the process";
}
