using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Governance.Auditor.Common;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Governance.Auditor.Documents;

/// <summary>
/// Reads YAML into a JSON tree, so one schema validator and one deserializer serve every document. Scalars follow the YAML 1.2 core
/// schema: a quoted scalar is always a string, and only an unquoted <c>true</c>, <c>false</c>, <c>null</c>, or number changes type.
/// A date such as <c>2026-10-08</c> stays a string. Aliases and merge keys are not supported, because no repository document uses them.
/// </summary>
internal static partial class YamlDocument
{
    [GeneratedRegex(@"^[-+]?[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex IntegerPattern();

    [GeneratedRegex(@"^[-+]?(?:\.[0-9]+|[0-9]+\.[0-9]*)(?:[eE][-+]?[0-9]+)?$|^[-+]?[0-9]+[eE][-+]?[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex FloatPattern();

    public static JsonNode? Parse(string yaml, string sourceName)
    {
        var stream = new YamlStream();

        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException exception)
        {
            throw new GovernanceException($"{sourceName}: invalid YAML at line {exception.Start.Line}: {exception.Message}", exception);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // YamlDotNet raises these for a duplicate key and for some unbalanced flow collections, without a position.
            throw new GovernanceException($"{sourceName}: invalid YAML: {exception.Message}", exception);
        }

        return stream.Documents.Count switch
        {
            0 => null,
            1 => Convert(stream.Documents[0].RootNode, sourceName),
            _ => throw new GovernanceException($"{sourceName}: the file holds {stream.Documents.Count} YAML documents, and one was expected."),
        };
    }

    private static JsonNode? Convert(YamlNode node, string source)
    {
        // YamlDotNet resolves an alias to the node it names, so an alias would copy a whole subtree each time it is used. Refusing the
        // anchor refuses every alias, and with it the exponential growth that nested aliases cause when a file comes from a pull request.
        if (!node.Anchor.IsEmpty)
        {
            throw new GovernanceException($"{source}: line {node.Start.Line}: YAML anchors and aliases are not supported.");
        }

        switch (node)
        {
            case YamlMappingNode mapping:
                var result = new JsonObject();

                foreach (var (key, value) in mapping.Children)
                {
                    if (key is not YamlScalarNode scalarKey)
                    {
                        throw new GovernanceException($"{source}: line {key.Start.Line}: a mapping key must be a plain scalar.");
                    }

                    var name = scalarKey.Value ?? string.Empty;

                    if (result.ContainsKey(name))
                    {
                        throw new GovernanceException($"{source}: line {key.Start.Line}: the key '{name}' appears twice.");
                    }

                    result[name] = Convert(value, source);
                }

                return result;

            case YamlSequenceNode sequence:
                return new JsonArray([.. sequence.Children.Select(child => Convert(child, source))]);

            case YamlScalarNode scalar:
                return ConvertScalar(scalar);

            default:
                throw new GovernanceException($"{source}: line {node.Start.Line}: this YAML node type is not supported.");
        }
    }

    private static JsonValue? ConvertScalar(YamlScalarNode scalar)
    {
        var value = scalar.Value ?? string.Empty;

        if (scalar.Style != ScalarStyle.Plain)
        {
            return JsonValue.Create(value);
        }

        if (value.Length == 0 || value is "~" or "null" or "Null" or "NULL")
        {
            return null;
        }

        if (value is "true" or "True" or "TRUE")
        {
            return JsonValue.Create(true);
        }

        if (value is "false" or "False" or "FALSE")
        {
            return JsonValue.Create(false);
        }

        if (IntegerPattern().IsMatch(value) && long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
        {
            return JsonValue.Create(integer);
        }

        if (FloatPattern().IsMatch(value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return JsonValue.Create(number);
        }

        return JsonValue.Create(value);
    }
}
