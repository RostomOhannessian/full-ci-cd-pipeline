using System.Text.Json.Nodes;

namespace Catalog.Contract.Tests.Support;

/// <summary>Reads the numbers of a YAML document, which the OpenAPI reader gives as decimals.</summary>
internal static class Json
{
    public static int Int(JsonNode? node) => decimal.ToInt32(node!.GetValue<decimal>());
}
