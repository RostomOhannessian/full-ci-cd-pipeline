using AwesomeAssertions;
using Catalog.Application.Results;
using Catalog.Application.Security;
using Xunit;

namespace Catalog.Application.Tests;

/// <summary>
/// REQ-APP-004 and ADR-0028: one mapping table turns each typed error into one entry of the error catalog. The agreement of the table with the catalog page and the
/// contract is checked in <c>Catalog.Contract.Tests</c>, and these tests check that every error case has a row and that each row says what the table claims.
/// </summary>
public sealed class ProblemMappingTests
{
    private static readonly (ApplicationError Error, string Slug, int Status)[] Table =
    [
        (new ValidationFailed([new FieldError("name", "The name is too long.")]), "validation-failed", 400),
        (new Forbidden(AccessLevel.Write, ForbiddenReason.MissingScope), "forbidden", 403),
        (new Forbidden(AccessLevel.Admin, ForbiddenReason.MissingRole), "forbidden", 403),
        (new NotFound("product"), "not-found", 404),
        (new Conflict(ConflictReason.SkuAlreadyExists), "sku-already-exists", 409),
        (new Conflict(ConflictReason.ProductDiscontinued), "product-discontinued", 409),
        (new Conflict(ConflictReason.IdempotencyRequestInProgress), "idempotency-request-in-progress", 409),
        (new PreconditionFailed(), "precondition-failed", 412),
        (new IdempotencyKeyReused(), "idempotency-key-reused", 422),
        (new PreconditionRequired(), "precondition-required", 428),
    ];

    public static TheoryData<ApplicationError, string, int> Rows
    {
        get
        {
            var rows = new TheoryData<ApplicationError, string, int>();

            foreach (var (error, slug, status) in Table)
            {
                rows.Add(error, slug, status);
            }

            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(Rows))]
    [Trait("Requirement", "REQ-APP-004")]
    public void Each_error_maps_to_one_entry_of_the_catalog(ApplicationError error, string slug, int status)
    {
        var problem = ProblemMapping.Map(error);

        problem.Type.Slug.Should().Be(slug);
        problem.Type.Status.Should().Be(status);
        problem.Detail.Should().NotBeNullOrWhiteSpace();
        ProblemMapping.All.Should().Contain(problem.Type);
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public void Every_error_case_and_every_conflict_reason_has_a_row()
    {
        var covered = Table.Select(row => row.Error.GetType()).ToHashSet();
        var cases = typeof(ApplicationError).Assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(ApplicationError))).ToList();
        var reasons = Table.Select(row => row.Error).OfType<Conflict>().Select(conflict => conflict.Reason).ToHashSet();

        covered.Should().BeEquivalentTo(cases);
        reasons.Should().BeEquivalentTo(Enum.GetValues<ConflictReason>());
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public void The_table_lists_each_entry_once_and_every_row_is_reachable_from_an_error()
    {
        var reached = Table.Select(row => ProblemMapping.Map(row.Error).Type).Distinct().ToList();

        ProblemMapping.All.Select(type => type.Slug).Should().OnlyHaveUniqueItems();
        ProblemMapping.All.Should().BeEquivalentTo(reached);
        ProblemMapping.All.Should().OnlyContain(type => type.Title.Length > 0 && type.Slug.Length > 0);
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public void A_validation_error_lists_every_field_message_in_order()
    {
        FieldError[] fields = [new("name", "The name is required."), new("price.amount", "The amount must not be negative."), new("name", "The name is too long.")];

        var problem = ProblemMapping.Map(new ValidationFailed(fields));

        problem.Errors.Should().Equal(fields);
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public void Only_a_validation_error_carries_field_messages()
    {
        var others = Table.Select(row => row.Error).Where(error => error is not ValidationFailed);

        others.Select(error => ProblemMapping.Map(error).Errors).Should().OnlyContain(errors => errors.Count == 0);
    }

    [Theory]
    [InlineData(AccessLevel.Read, ForbiddenReason.MissingScope, "This operation requires the catalog.read scope.")]
    [InlineData(AccessLevel.Write, ForbiddenReason.MissingScope, "This operation requires the catalog.write scope.")]
    [InlineData(AccessLevel.Admin, ForbiddenReason.MissingScope, "This operation requires the catalog.admin scope.")]
    [InlineData(AccessLevel.Write, ForbiddenReason.MissingRole, "This operation requires the catalog.write scope and one of the roles catalog-editor, catalog-admin.")]
    [Trait("Requirement", "REQ-APP-004")]
    [Trait("Requirement", "REQ-IDN-002")]
    public void A_forbidden_error_names_the_scope_and_the_roles_that_it_needs(AccessLevel level, ForbiddenReason reason, string detail)
    {
        ProblemMapping.Map(new Forbidden(level, reason)).Detail.Should().Be(detail);
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public void A_case_that_the_table_does_not_know_is_a_programming_error()
    {
        var unknown = () => ProblemMapping.Map(new Conflict((ConflictReason)99));
        var missing = () => ProblemMapping.Map(null!);

        unknown.Should().Throw<ArgumentOutOfRangeException>();
        missing.Should().Throw<ArgumentNullException>();
    }
}
