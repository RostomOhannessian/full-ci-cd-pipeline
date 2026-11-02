using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Catalog.Architecture.Tests;

/// <summary>
/// REQ-DATA-006: application replicas never apply schema changes. The only identity that may change the schema is the migrator, which runs the bundle that <c>dotnet ef</c> builds (plan sections 8.4 and 8.10), so no production
/// assembly calls a method that migrates or creates a database. The check reads the member references of each built assembly from disk and never loads it. The last test shows that the check sees a member that an assembly does
/// call, so a pass on the others means something.
/// </summary>
public sealed class MigrationPolicyTests
{
    // The methods that change a schema at run time: the migration entry points of EF Core and its creation shortcuts.
    private static readonly string[] SchemaChangingMembers = ["Migrate", "MigrateAsync", "EnsureCreated", "EnsureCreatedAsync", "EnsureDeleted", "EnsureDeletedAsync"];

    private static string BinaryPath(string assemblyName) => Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll");

    private static HashSet<string> ReferencedMembers(string path)
    {
        using var stream = File.OpenRead(path);
        using var image = new PEReader(stream);
        var metadata = image.GetMetadataReader();

        return [.. metadata.MemberReferences.Select(handle => metadata.GetString(metadata.GetMemberReference(handle).Name))];
    }

    [Theory]
    [InlineData("Catalog.Domain")]
    [InlineData("Catalog.Application")]
    [InlineData("Catalog.Infrastructure")]
    [InlineData("Catalog.CrossCutting")]
    [InlineData("Catalog.Api")]
    [Trait("Requirement", "REQ-DATA-006")]
    public void No_production_assembly_migrates_or_creates_a_database(string assemblyName)
    {
        var offending = ReferencedMembers(BinaryPath(assemblyName)).Where(member => SchemaChangingMembers.Contains(member, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToList();

        Assert.True(offending.Count == 0, $"{assemblyName} calls {string.Join(", ", offending)}. Only the migration bundle changes the schema, so an application replica never does.");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-006")]
    public void The_check_sees_a_member_that_an_assembly_does_call()
    {
        // Infrastructure configures EF Core for SQL Server, so a scan that finds nothing in it would be blind.
        Assert.Contains("UseSqlServer", ReferencedMembers(BinaryPath("Catalog.Infrastructure")));
    }
}
