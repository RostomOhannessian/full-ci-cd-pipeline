using System.Text.Json.Serialization;
using Catalog.Contracts.V1;

namespace Catalog.Api.Http;

/// <summary>
/// The serializer context of the request and response bodies (ADR-0041). The names are camel case, as the contract writes them, and a null member is written as <c>null</c>, because the contract lists
/// <c>description</c> and <c>nextCursor</c> as required members that can be null. The source generator keeps the serializer free of reflection, and its property metadata tells the body reader which
/// members the contract knows.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = false, Converters = [typeof(UtcDateTimeOffsetConverter)])]
[JsonSerializable(typeof(Category))]
[JsonSerializable(typeof(CategoryCreate))]
[JsonSerializable(typeof(CategoryUpdate))]
[JsonSerializable(typeof(CategoryPage))]
[JsonSerializable(typeof(Product))]
[JsonSerializable(typeof(ProductCreate))]
[JsonSerializable(typeof(ProductUpdate))]
[JsonSerializable(typeof(ProductPage))]
[JsonSerializable(typeof(Money))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;
