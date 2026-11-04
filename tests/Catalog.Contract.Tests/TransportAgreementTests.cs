using System.Reflection;
using Catalog.Contract.Tests.Support;
using Catalog.Contracts.V1;
using Microsoft.OpenApi;
using Xunit;

namespace Catalog.Contract.Tests;

/// <summary>
/// The transport records of <c>Catalog.Contracts.V1</c> agree with the schemas of the authored contract (REQ-CON-002, REQ-API-001, ADR-0042). The served document takes its schemas from the contract, because the code has
/// no other statement of a length or a pattern, so this test is what keeps the records that the API reads and writes from drifting away from them: the same members, every required member present, and a member that can be
/// null only where the schema allows it or does not require it.
/// </summary>
public sealed class TransportAgreementTests
{
    // The records that the API reads from a body can leave out a member that the contract requires, so that the API answers with a validation error and not with a failure of the serializer.
    private static readonly HashSet<string> RequestRecords = ["CategoryCreate", "CategoryUpdate", "ProductCreate", "ProductUpdate"];

    public static TheoryData<string> Records => [.. typeof(Money).Assembly.GetTypes().Where(type => type.Namespace == "Catalog.Contracts.V1" && type.IsPublic && !type.Name.StartsWith("ProblemDetails", StringComparison.Ordinal) && !type.Name.StartsWith("ValidationProblem", StringComparison.Ordinal)).Select(type => type.Name).Order(StringComparer.Ordinal)];

    private static string CamelCase(string name) => string.Concat(char.ToLowerInvariant(name[0]).ToString(), name.AsSpan(1));

    [Theory]
    [MemberData(nameof(Records))]
    [Trait("Requirement", "REQ-CON-002")]
    [Trait("Requirement", "REQ-API-001")]
    public void A_record_has_the_members_of_its_schema_and_requires_what_the_schema_requires(string name)
    {
        var type = typeof(Money).Assembly.GetType($"Catalog.Contracts.V1.{name}")!;
        var schema = Authored.Document.Components!.Schemas![name];
        var members = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var nullability = new NullabilityInfoContext();

        Assert.Equal(schema.Properties!.Keys.Order(StringComparer.Ordinal), members.Select(member => CamelCase(member.Name)).Order(StringComparer.Ordinal));

        foreach (var member in members)
        {
            var property = schema.Properties![CamelCase(member.Name)];
            var required = schema.Required!.Contains(CamelCase(member.Name));
            var canBeNull = nullability.Create(member).ReadState == NullabilityState.Nullable || Nullable.GetUnderlyingType(member.PropertyType) is not null;
            var schemaAllowsNull = property.Type is { } schemaType && schemaType.HasFlag(JsonSchemaType.Null);

            if (RequestRecords.Contains(name))
            {
                // A request record is nullable exactly where the contract requires the member or allows null, so a missing member reaches the validators as null.
                Assert.True(canBeNull == (required || schemaAllowsNull), $"{name}.{member.Name}: the record {(canBeNull ? "can" : "cannot")} be null, and the schema {(required ? "requires" : "does not require")} it and {(schemaAllowsNull ? "allows" : "does not allow")} null.");
            }
            else
            {
                Assert.True(required, $"{name}.{member.Name} is a member of a response and the schema must require it.");
                Assert.Equal(schemaAllowsNull, canBeNull);
            }
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-CON-002")]
    public void Every_schema_that_an_operation_reads_or_writes_has_a_record()
    {
        var records = typeof(Money).Assembly.GetTypes().Where(type => type.Namespace == "Catalog.Contracts.V1").Select(type => type.Name).ToHashSet(StringComparer.Ordinal);
        List<string> used = [];

        foreach (var operation in ContractOperation.All)
        {
            if (operation.Operation.RequestBody?.Content?["application/json"].Schema is OpenApiSchemaReference request)
            {
                used.Add(request.Reference.Id!);
            }

            foreach (var response in operation.Operation.Responses!.Where(response => response.Key is "200" or "201"))
            {
                if (response.Value.Content?["application/json"].Schema is OpenApiSchemaReference body)
                {
                    used.Add(body.Reference.Id!);
                }
            }
        }

        Assert.NotEmpty(used);
        Assert.All(used, id => Assert.Contains(id, records));
    }
}
