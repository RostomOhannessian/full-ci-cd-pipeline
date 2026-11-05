using System.Text.RegularExpressions;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;

namespace Catalog.Api.Telemetry;

/// <summary>
/// The data classifications of the catalog (plan section 8.11, REQ-OBS-003, REQ-SEC-006, ADR-0043). Two classes of data must never leave the process in a log record or in the tag of a span: a <see cref="Secret"/>
/// (a token, a password, a key, a connection string, a header that carries one) and <see cref="Personal"/> data (an address of a person, a name, a login, a network address of a caller). A <see cref="Redactor"/> that
/// the composition root registers for each class says what replaces the value.
/// </summary>
public static class CatalogTaxonomy
{
    /// <summary>The name of the taxonomy.</summary>
    public const string Name = "Catalog";

    /// <summary>Gets the class of a token, a password, a key, a connection string, and a header that carries one.</summary>
    public static DataClassification Secret { get; } = new(Name, nameof(Secret));

    /// <summary>Gets the class of an address of a person, a name, a login, and a network address of a caller.</summary>
    public static DataClassification Personal { get; } = new(Name, nameof(Personal));
}

/// <summary>Replaces a classified value with a fixed marker, so a reader of a log or a trace sees that a value was there and that it was removed, and never what it was.</summary>
public sealed class MarkerRedactor : Redactor
{
    /// <summary>The text that replaces a classified value.</summary>
    public const string Marker = "[redacted]";

    /// <inheritdoc />
    public override int GetRedactedLength(ReadOnlySpan<char> input) => Marker.Length;

    /// <inheritdoc />
    public override int Redact(ReadOnlySpan<char> source, Span<char> destination)
    {
        Marker.CopyTo(destination);

        return Marker.Length;
    }
}

/// <summary>
/// Decides which attribute is a secret or personal data. The decision has two parts, because a name alone does not catch a token that a developer put in a field called <c>detail</c>, and a value alone does not catch
/// a password that is only a word. A name that holds a word of the lists below is classified whatever its value is, and a text value that has the shape of a token, a connection string, or an email address is classified
/// whatever its name is. The lists lean toward redacting too much, because a lost detail costs less than a leaked secret.
/// </summary>
public static class TelemetryClassifier
{
    // Words of an attribute name. The name is compared in lower case, so a word matches inside a longer name such as http.request.header.authorization or db_connection_string.
    private static readonly string[] SecretNameWords =
    [
        "password", "passwd", "pwd", "secret", "token", "authorization", "cookie", "apikey", "api_key", "api-key", "connectionstring", "connection_string", "connection-string", "credential", "privatekey", "private_key", "bearer",
    ];

    private static readonly string[] PersonalNameWords =
    [
        "email", "e-mail", "phone", "username", "user_name", "user.name", "enduser", "user.id", "user_id", "preferred_username", "given_name", "family_name", "first_name", "last_name", "client.address", "network.peer.address", "ip_address", "remote_address",
    ];

    /// <summary>Classifies an attribute by its name and, for a text value, by its shape.</summary>
    /// <param name="name">The name of the attribute.</param>
    /// <param name="value">The value, which is classified by shape only when it is text.</param>
    /// <returns>The class of the value, or <see cref="DataClassification.None"/> when nothing in the name or the value is sensitive.</returns>
    public static DataClassification Classify(string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (ContainsAny(name, SecretNameWords))
        {
            return CatalogTaxonomy.Secret;
        }

        if (value is string text && (TokenShape.IsMatch(text) || ConnectionStringShape.IsMatch(text)))
        {
            return CatalogTaxonomy.Secret;
        }

        if (ContainsAny(name, PersonalNameWords) || (value is string address && (EmailShape.IsMatch(address) || ProfilePathShape.IsMatch(address))))
        {
            return CatalogTaxonomy.Personal;
        }

        return DataClassification.None;
    }

    private static bool ContainsAny(string name, string[] words)
    {
        foreach (var word in words)
        {
            if (name.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // A JSON web token (three dot-separated parts that start with the encoded header), or the scheme of an authorization header followed by its credential.
    private static readonly Regex TokenShape = Shape(@"eyJ[A-Za-z0-9_-]{4,}\.[A-Za-z0-9_-]{4,}\.[A-Za-z0-9_-]*|\b(Bearer|Basic)\s+[A-Za-z0-9._~+/=-]{8,}", RegexOptions.None);

    // The parts of a connection string that name a credential or a server and a database.
    private static readonly Regex ConnectionStringShape = Shape(@"\b(password|pwd|user\s*id|uid|accountkey|sharedaccesskey)\s*=\s*[^;\s]|\b(server|data\s+source)\s*=[^;]+;.*\b(database|initial\s+catalog)\s*=", RegexOptions.IgnoreCase);

    private static readonly Regex EmailShape = Shape(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+", RegexOptions.None);

    // The folder of a person on Windows, Linux, or macOS, which names the login of the person.
    private static readonly Regex ProfilePathShape = Shape(@"[A-Za-z]:\\Users\\[^\\\s]+|/(home|Users)/[^/\s]+", RegexOptions.None);

    // The engine does not backtrack, so no value can make a pattern run long, and the timeout is a second guard. The shapes are in plain regular expressions and not in generated ones, because a generated type
    // would sit in the assembly of the API under a namespace of the base class library, and the architecture tests read the assembly for types that a tool added.
    private static Regex Shape(string pattern, RegexOptions options) =>
        new(pattern, options | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));
}
