using System.Net;
using AwesomeAssertions;
using Catalog.Api.Tests.Support;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// The authorization matrix over every operation of the contract (REQ-IDN-002, REQ-IDN-003, plan section 8.7, ADR-0042). The scope that each operation needs is read from the authored contract, and the roles that grant it are
/// a ladder (reader, editor, admin) that this test writes apart from the permission table of the application. Each operation is sent every one of the 64 combinations of the three scopes and the three roles, and an
/// allowed caller must get past the policy and end at a 400 that the API itself makes before it reads a database, while every other caller must get a 403 that names the requirement. No token is a 401.
/// </summary>
public sealed class AuthorizationMatrixTests(SecuredApiFixture api) : IClassFixture<SecuredApiFixture>
{
    private static readonly string[] Scopes = ["catalog.read", "catalog.write", "catalog.admin"];

    private static readonly string[] Roles = ["catalog-reader", "catalog-editor", "catalog-admin"];

    private static readonly IReadOnlyList<ContractOperation> Contract = ReadContract();

    public static TheoryData<string> OperationIds => [.. Contract.Select(operation => operation.OperationId)];

    private static IReadOnlyList<ContractOperation> ReadContract()
    {
        var settings = new OpenApiReaderSettings();
        settings.AddYamlReader();

        var path = Path.Combine(Repository.Root, "contracts", "openapi", "catalog", "v1", "catalog.openapi.yaml");
        var document = OpenApiDocument.Parse(File.ReadAllText(path), "yaml", settings).Document ?? throw new InvalidOperationException("The contract did not load.");

        return
        [
            .. document.Paths.SelectMany(item => item.Value.Operations!.Select(operation => new ContractOperation(
                operation.Value.OperationId!,
                operation.Key,
                item.Key,
                operation.Value.Security!.Single().Single().Value.Single()))),
        ];
    }

    // The ladder: a role grants its own level and the levels below it.
    private static int RankOfScope(string scope) => Array.IndexOf(Scopes, scope);

    private static int RankOfRole(string role) => Array.IndexOf(Roles, role);

    private static bool IsAllowed(string requiredScope, IEnumerable<string> scopes, IEnumerable<string> roles) =>
        scopes.Contains(requiredScope) && roles.Any(role => RankOfRole(role) >= RankOfScope(requiredScope));

    private static IEnumerable<string[]> Subsets(string[] values) =>
        Enumerable.Range(0, 1 << values.Length).Select(mask => values.Where((_, index) => (mask & (1 << index)) != 0).ToArray());

    // A request that an allowed caller sends and that the API refuses by itself, before any use case touches a database.
    private static string PathThatEndsAtTheApi(ContractOperation operation)
    {
        var path = operation.Path.Replace("{categoryId}", "not-a-uuid", StringComparison.Ordinal).Replace("{productId}", "not-a-uuid", StringComparison.Ordinal);

        return operation.Method == HttpMethod.Get && !path.Contains("not-a-uuid", StringComparison.Ordinal) ? $"{path}?pageSize=abc" : path;
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-002")]
    public void The_contract_has_ten_operations_and_each_needs_one_of_the_three_scopes()
    {
        Contract.Should().HaveCount(10);
        Contract.Select(operation => operation.Scope).Should().OnlyContain(scope => Scopes.Contains(scope));
        Contract.Select(operation => operation.Scope).Distinct().Should().BeEquivalentTo(Scopes);
    }

    [Theory]
    [MemberData(nameof(OperationIds))]
    [Trait("Requirement", "REQ-IDN-002")]
    [Trait("Requirement", "REQ-APP-005")]
    public async Task Every_combination_of_scopes_and_roles_gets_the_answer_that_the_ladder_gives_for_the_operation(string operationId)
    {
        var operation = Contract.Single(candidate => candidate.OperationId == operationId);
        var path = PathThatEndsAtTheApi(operation);
        List<string> wrong = [];

        foreach (var scopes in Subsets(Scopes))
        {
            foreach (var roles in Subsets(Roles))
            {
                var token = new TokenSpec { Scopes = scopes, Roles = roles };
                using var response = await api.SendAsync(operation.Method, path, api.Token(token));
                var allowed = IsAllowed(operation.Scope, scopes, roles);
                var status = response.StatusCode;

                // An allowed caller is not stopped by the policy. The request is invalid on purpose, so the API answers 400 itself.
                if (allowed ? status != HttpStatusCode.BadRequest : status != HttpStatusCode.Forbidden)
                {
                    wrong.Add($"scopes [{string.Join(' ', scopes)}] roles [{string.Join(' ', roles)}] got {(int)status}, expected {(allowed ? 400 : 403)}");
                }
            }
        }

        wrong.Should().BeEmpty($"{operationId} needs {operation.Scope}");
    }

    [Theory]
    [MemberData(nameof(OperationIds))]
    [Trait("Requirement", "REQ-IDN-002")]
    [Trait("Requirement", "REQ-IDN-003")]
    public async Task A_request_without_a_token_or_with_a_token_that_is_not_accepted_is_401_for_every_operation(string operationId)
    {
        var operation = Contract.Single(candidate => candidate.OperationId == operationId);
        var path = PathThatEndsAtTheApi(operation);

        using var none = await api.SendAsync(operation.Method, path);
        using var garbage = await api.SendAsync(operation.Method, path, "not.a.token");

        none.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        garbage.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await none.JsonAsync()).ProblemSlug().Should().Be("unauthorized");
    }

    [Theory]
    [MemberData(nameof(OperationIds))]
    [Trait("Requirement", "REQ-IDN-002")]
    public async Task A_forbidden_answer_names_the_scope_and_never_the_roles_of_the_caller(string operationId)
    {
        var operation = Contract.Single(candidate => candidate.OperationId == operationId);

        using var response = await api.SendAsync(operation.Method, PathThatEndsAtTheApi(operation), api.Token(new TokenSpec { Scopes = [], Roles = ["catalog-admin"] }));
        var body = await response.TextAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        body.Should().Contain(operation.Scope);
        body.Should().NotContain("11111111-2222-3333-4444-555555555555", "the subject of the token is not an answer");
    }

    private sealed record ContractOperation(string OperationId, HttpMethod Method, string Path, string Scope);
}
