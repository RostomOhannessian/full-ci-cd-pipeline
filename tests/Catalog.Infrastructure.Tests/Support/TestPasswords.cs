using System.Security.Cryptography;

namespace Catalog.Infrastructure.Tests.Support;

/// <summary>Passwords for the logins that the tests create. They are random for each run and exist only for the life of the container, and no test prints one.</summary>
internal static class TestPasswords
{
    /// <summary>Returns a password that meets the complexity rule of SQL Server: upper case, lower case, a digit, and a symbol, then random hexadecimal text.</summary>
    public static string Next() => "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
}
