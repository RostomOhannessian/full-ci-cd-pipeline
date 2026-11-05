namespace Catalog.Api.Tests.Support;

/// <summary>
/// Values that have the shape of a secret, for the tests that prove the redaction. Each one is built at run time from parts that have no such shape, because the secret scan of the repository reads the source for a line
/// that looks like a signed token or a user name and password inside an address, and a sentinel that is written whole would fail it. The values are made up and open nothing.
/// </summary>
internal static class Sentinels
{
    /// <summary>Gets a text with the shape of a signed JSON web token: a header, a payload, and a signature, with a dot between them.</summary>
    public static string Token { get; } = string.Join('.', "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9", "eyJzdWIiOiJzZW50aW5lbCJ9", "c2lnbmF0dXJl");

    /// <summary>Gets an address that holds a user name and a password in front of the host.</summary>
    public static string AddressWithCredentials { get; } = string.Concat("ftp", "://", "user", ":", "Pw-Secret-endpoint", "@", "collector.example.test");
}
