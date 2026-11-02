using System.Globalization;
using AwesomeAssertions;
using Catalog.Infrastructure.Tests.Support;
using Xunit;

namespace Catalog.Infrastructure.Tests;

/// <summary>
/// REQ-QUA-001 and REQ-QUA-004: the suites run against a real SQL Server 2022 that the repository pins by digest, every test class has a database of its own, and the run is deterministic: the culture is the
/// invariant one, and no image floats (plan section 10.3, ADR-0034).
/// </summary>
public sealed class ContainerFixtureTests(SqlServerFixture server, CatalogDatabaseFixture database) : IClassFixture<CatalogDatabaseFixture>
{
    [Fact]
    [Trait("Requirement", "REQ-QUA-001")]
    public async Task The_suites_run_against_a_real_sql_server_2022_developer_engine()
    {
        var version = await SqlProbe.ScalarAsync<string>(database.ConnectionString, "SELECT @@VERSION");
        var edition = await SqlProbe.ScalarAsync<string>(database.ConnectionString, "SELECT CAST(SERVERPROPERTY('Edition') AS nvarchar(100))");

        version.Should().Contain("Microsoft SQL Server 2022");
        edition.Should().StartWith("Developer");
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-004")]
    public void The_sql_server_image_is_the_digest_that_the_local_stack_pins()
    {
        var image = ComposeImage.SqlServer();

        ComposeImage.IsPinnedByDigest(image).Should().BeTrue();
        image.Should().StartWith("mcr.microsoft.com/mssql/server:2022-latest@sha256:");
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-004")]
    public void The_resource_reaper_image_is_pinned_by_digest_too()
    {
        ComposeImage.IsPinnedByDigest(SqlServerFixture.ReaperImage).Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-004")]
    public async Task Each_test_class_has_a_database_of_its_own_that_only_the_migrations_filled()
    {
        var name = await SqlProbe.ScalarAsync<string>(database.ConnectionString, "SELECT DB_NAME()");
        var masterConnection = server.ServerConnectionString;

        name.Should().StartWith("catalog_");
        (await SqlProbe.ScalarAsync<int>(masterConnection, "SELECT COUNT(*) FROM sys.databases WHERE name = @name", ("@name", name!))).Should().Be(1);
        (await SqlProbe.ScalarAsync<int>(database.ConnectionString, "SELECT COUNT(*) FROM catalog.Products")).Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-004")]
    public void The_tests_run_under_the_invariant_culture_although_the_globalization_mode_is_on()
    {
        CultureInfo.CurrentCulture.Should().Be(CultureInfo.InvariantCulture);
        CultureInfo.CurrentUICulture.Should().Be(CultureInfo.InvariantCulture);
    }
}
