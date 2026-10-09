using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Governance.Auditor.Common;

namespace Governance.Auditor.Documents;

/// <summary>
/// Validates a JSON tree against a JSON Schema (draft 2020-12). It implements the keywords this repository's schemas use and rejects a
/// schema that uses any other keyword, so a schema can never silently check less than it says (ADR-0021).
/// </summary>
internal sealed class JsonSchemaValidator
{
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(1);

    // Keywords that describe the schema and do not constrain a value.
    private static readonly HashSet<string> Annotations =
    [
        "$schema", "$id", "$comment", "title", "description", "examples", "default", "deprecated", "readOnly", "writeOnly", "format",
    ];

    private static readonly HashSet<string> Constraints =
    [
        "$defs", "$ref", "type", "enum", "const", "properties", "required", "additionalProperties", "propertyNames", "minProperties",
        "maxProperties", "items", "minItems", "maxItems", "uniqueItems", "minLength", "maxLength", "pattern", "minimum", "maximum",
        "exclusiveMinimum", "exclusiveMaximum", "oneOf", "anyOf", "allOf", "not",
    ];

    private readonly JsonNode _root;
    private readonly Dictionary<string, Regex> _patterns = [];

    private JsonSchemaValidator(JsonNode root)
    {
        _root = root;
    }

    /// <summary>Creates a validator, or throws when the schema uses a keyword this validator does not implement.</summary>
    public static JsonSchemaValidator Create(JsonNode schema, string sourceName)
    {
        List<string> problems = [];
        CheckKeywords(schema, "#", problems);

        if (problems.Count > 0)
        {
            throw new GovernanceException($"{sourceName}: the schema uses features the validator does not implement. {string.Join(" ", problems)}");
        }

        return new JsonSchemaValidator(schema);
    }

    /// <summary>Returns one message per violation, each starting with the JSON pointer of the value. An empty list means the value is valid.</summary>
    public IReadOnlyList<string> Validate(JsonNode? instance)
    {
        List<string> errors = [];
        ValidateNode(instance, _root, string.Empty, errors);
        return errors;
    }

    private static void CheckKeywords(JsonNode? schema, string pointer, List<string> problems)
    {
        if (schema is not JsonObject definition)
        {
            return;
        }

        foreach (var (keyword, value) in definition)
        {
            if (!Annotations.Contains(keyword) && !Constraints.Contains(keyword))
            {
                problems.Add($"{pointer}: the keyword '{keyword}' is not supported.");
                continue;
            }

            switch (keyword)
            {
                case "properties" or "$defs" when value is JsonObject children:
                    foreach (var (name, child) in children)
                    {
                        CheckKeywords(child, $"{pointer}/{keyword}/{name}", problems);
                    }

                    break;
                case "items" or "additionalProperties" or "propertyNames" or "not":
                    CheckKeywords(value, $"{pointer}/{keyword}", problems);
                    break;
                case "oneOf" or "anyOf" or "allOf" when value is JsonArray alternatives:
                    for (var index = 0; index < alternatives.Count; index++)
                    {
                        CheckKeywords(alternatives[index], $"{pointer}/{keyword}/{index}", problems);
                    }

                    break;
            }
        }
    }

    private void ValidateNode(JsonNode? instance, JsonNode schema, string pointer, List<string> errors)
    {
        if (schema is JsonValue flag && flag.TryGetValue<bool>(out var allowed))
        {
            if (!allowed)
            {
                errors.Add($"{Describe(pointer)}: no value is allowed here.");
            }

            return;
        }

        var definition = schema.AsObject();

        if (definition["$ref"] is JsonValue reference)
        {
            ValidateNode(instance, Resolve(reference.GetValue<string>()), pointer, errors);
        }

        ValidateType(instance, definition, pointer, errors);
        ValidateValue(instance, definition, pointer, errors);

        switch (instance)
        {
            case JsonObject obj:
                ValidateObject(obj, definition, pointer, errors);
                break;
            case JsonArray array:
                ValidateArray(array, definition, pointer, errors);
                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                ValidateString(value.GetValue<string>(), definition, pointer, errors);
                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.Number:
                ValidateNumber(NumberOf(value), definition, pointer, errors);
                break;
        }

        ValidateCombinators(instance, definition, pointer, errors);
    }

    private JsonNode Resolve(string reference)
    {
        if (!reference.StartsWith('#'))
        {
            throw new GovernanceException($"The schema reference '{reference}' is not local. Only '#/...' references are supported.");
        }

        JsonNode? current = _root;

        foreach (var segment in reference.TrimStart('#').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var key = segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            current = current is JsonObject obj && obj.TryGetPropertyValue(key, out var next)
                ? next
                : throw new GovernanceException($"The schema reference '{reference}' does not resolve.");
        }

        return current ?? throw new GovernanceException($"The schema reference '{reference}' does not resolve.");
    }

    private static void ValidateType(JsonNode? instance, JsonObject definition, string pointer, List<string> errors)
    {
        if (definition["type"] is not { } typeNode)
        {
            return;
        }

        string[] expected = typeNode is JsonArray list
            ? [.. list.Select(item => item!.GetValue<string>())]
            : [typeNode.GetValue<string>()];

        if (!expected.Any(type => HasType(instance, type)))
        {
            errors.Add($"{Describe(pointer)}: expected {string.Join(" or ", expected)} but found {TypeName(instance)}.");
        }
    }

    private static bool HasType(JsonNode? instance, string type) => type switch
    {
        "null" => instance is null,
        "object" => instance is JsonObject,
        "array" => instance is JsonArray,
        "string" => instance is JsonValue value && value.GetValueKind() == JsonValueKind.String,
        "boolean" => instance is JsonValue value && value.GetValueKind() is JsonValueKind.True or JsonValueKind.False,
        "number" => instance is JsonValue value && value.GetValueKind() == JsonValueKind.Number,
        "integer" => instance is JsonValue value && value.GetValueKind() == JsonValueKind.Number && double.IsInteger(NumberOf(value)),
        _ => throw new GovernanceException($"The schema type '{type}' is not supported."),
    };

    // A value built in code (a long) and a value parsed from JSON (an element) store numbers differently, so read the JSON text.
    private static double NumberOf(JsonValue value) => double.Parse(value.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);

    private static string TypeName(JsonNode? instance) => instance switch
    {
        null => "null",
        JsonObject => "object",
        JsonArray => "array",
        JsonValue value => value.GetValueKind() switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            _ => "boolean",
        },
        _ => "unknown",
    };

    private static void ValidateValue(JsonNode? instance, JsonObject definition, string pointer, List<string> errors)
    {
        if (definition["enum"] is JsonArray choices && !choices.Any(choice => JsonNode.DeepEquals(choice, instance)))
        {
            errors.Add($"{Describe(pointer)}: the value must be one of {string.Join(", ", choices.Select(choice => choice?.ToJsonString() ?? "null"))}.");
        }

        if (definition.TryGetPropertyValue("const", out var constant) && !JsonNode.DeepEquals(constant, instance))
        {
            errors.Add($"{Describe(pointer)}: the value must be {constant?.ToJsonString() ?? "null"}.");
        }
    }

    private void ValidateObject(JsonObject obj, JsonObject definition, string pointer, List<string> errors)
    {
        if (definition["required"] is JsonArray required)
        {
            foreach (var name in required.Select(item => item!.GetValue<string>()).Where(name => !obj.ContainsKey(name)))
            {
                errors.Add($"{Describe(pointer)}: the required property '{name}' is missing.");
            }
        }

        var properties = definition["properties"] as JsonObject;

        foreach (var (name, value) in obj)
        {
            var childPointer = $"{pointer}/{Escape(name)}";

            if (properties is not null && properties.TryGetPropertyValue(name, out var propertySchema))
            {
                ValidateNode(value, propertySchema!, childPointer, errors);
            }
            else if (definition["additionalProperties"] is { } additional)
            {
                if (additional is JsonValue flag && flag.TryGetValue<bool>(out var allowed) && !allowed)
                {
                    errors.Add($"{Describe(pointer)}: the property '{name}' is not allowed.");
                }
                else
                {
                    ValidateNode(value, additional, childPointer, errors);
                }
            }

            if (definition["propertyNames"] is { } nameSchema)
            {
                ValidateNode(JsonValue.Create(name), nameSchema, childPointer, errors);
            }
        }

        if (definition["minProperties"] is JsonValue min && obj.Count < min.GetValue<int>())
        {
            errors.Add($"{Describe(pointer)}: expected at least {min.GetValue<int>()} properties but found {obj.Count}.");
        }

        if (definition["maxProperties"] is JsonValue max && obj.Count > max.GetValue<int>())
        {
            errors.Add($"{Describe(pointer)}: expected at most {max.GetValue<int>()} properties but found {obj.Count}.");
        }
    }

    private void ValidateArray(JsonArray array, JsonObject definition, string pointer, List<string> errors)
    {
        if (definition["items"] is { } itemSchema)
        {
            for (var index = 0; index < array.Count; index++)
            {
                ValidateNode(array[index], itemSchema, $"{pointer}/{index}", errors);
            }
        }

        if (definition["minItems"] is JsonValue min && array.Count < min.GetValue<int>())
        {
            errors.Add($"{Describe(pointer)}: expected at least {min.GetValue<int>()} items but found {array.Count}.");
        }

        if (definition["maxItems"] is JsonValue max && array.Count > max.GetValue<int>())
        {
            errors.Add($"{Describe(pointer)}: expected at most {max.GetValue<int>()} items but found {array.Count}.");
        }

        if (definition["uniqueItems"] is JsonValue unique && unique.GetValue<bool>())
        {
            for (var first = 0; first < array.Count; first++)
            {
                for (var second = first + 1; second < array.Count; second++)
                {
                    if (JsonNode.DeepEquals(array[first], array[second]))
                    {
                        errors.Add($"{Describe(pointer)}: items {first} and {second} are the same, and every item must be unique.");
                    }
                }
            }
        }
    }

    private void ValidateString(string value, JsonObject definition, string pointer, List<string> errors)
    {
        var length = value.EnumerateRunes().Count();

        if (definition["minLength"] is JsonValue min && length < min.GetValue<int>())
        {
            errors.Add($"{Describe(pointer)}: the text must have at least {min.GetValue<int>()} characters.");
        }

        if (definition["maxLength"] is JsonValue max && length > max.GetValue<int>())
        {
            errors.Add($"{Describe(pointer)}: the text must have at most {max.GetValue<int>()} characters.");
        }

        if (definition["pattern"] is JsonValue pattern)
        {
            var expression = pattern.GetValue<string>();

            if (!_patterns.TryGetValue(expression, out var regex))
            {
                regex = new Regex(expression, RegexOptions.CultureInvariant, PatternTimeout);
                _patterns[expression] = regex;
            }

            if (!regex.IsMatch(value))
            {
                errors.Add($"{Describe(pointer)}: the text does not match the pattern {expression}.");
            }
        }
    }

    private static void ValidateNumber(double value, JsonObject definition, string pointer, List<string> errors)
    {
        if (definition["minimum"] is JsonValue min && value < min.GetValue<double>())
        {
            errors.Add($"{Describe(pointer)}: the number must be at least {min.GetValue<double>()}.");
        }

        if (definition["maximum"] is JsonValue max && value > max.GetValue<double>())
        {
            errors.Add($"{Describe(pointer)}: the number must be at most {max.GetValue<double>()}.");
        }

        if (definition["exclusiveMinimum"] is JsonValue exclusiveMin && value <= exclusiveMin.GetValue<double>())
        {
            errors.Add($"{Describe(pointer)}: the number must be greater than {exclusiveMin.GetValue<double>()}.");
        }

        if (definition["exclusiveMaximum"] is JsonValue exclusiveMax && value >= exclusiveMax.GetValue<double>())
        {
            errors.Add($"{Describe(pointer)}: the number must be less than {exclusiveMax.GetValue<double>()}.");
        }
    }

    private void ValidateCombinators(JsonNode? instance, JsonObject definition, string pointer, List<string> errors)
    {
        if (definition["allOf"] is JsonArray all)
        {
            foreach (var alternative in all)
            {
                ValidateNode(instance, alternative!, pointer, errors);
            }
        }

        if (definition["anyOf"] is JsonArray any && !any.Any(alternative => Matches(instance, alternative!)))
        {
            errors.Add($"{Describe(pointer)}: the value matches none of the {any.Count} allowed shapes.");
        }

        if (definition["oneOf"] is JsonArray one)
        {
            var matches = one.Count(alternative => Matches(instance, alternative!));

            if (matches != 1)
            {
                errors.Add($"{Describe(pointer)}: the value must match exactly one of the {one.Count} allowed shapes but matches {matches}.");
            }
        }

        if (definition["not"] is { } forbidden && Matches(instance, forbidden))
        {
            errors.Add($"{Describe(pointer)}: the value matches a shape that is not allowed.");
        }
    }

    private bool Matches(JsonNode? instance, JsonNode schema)
    {
        List<string> scratch = [];
        ValidateNode(instance, schema, string.Empty, scratch);
        return scratch.Count == 0;
    }

    private static string Describe(string pointer) => pointer.Length == 0 ? "(root)" : pointer;

    private static string Escape(string name) => name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
