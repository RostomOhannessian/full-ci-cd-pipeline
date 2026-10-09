using Xunit;

namespace Governance.Auditor.Tests.Support;

/// <summary>
/// Compares generated text with a reviewed file under <c>Fixtures</c>. A change to the generated text is a change to the file, so it
/// shows up in review. Set <c>GOVERNANCE_UPDATE_GOLDEN=1</c> to rewrite the files, then read the diff before committing.
/// </summary>
internal static class Golden
{
    private const string UpdateVariable = "GOVERNANCE_UPDATE_GOLDEN";

    public static string PathOf(string fileName) => Path.Combine(RealRepository.Root, "tests", "Governance.Auditor.Tests", "Fixtures", fileName);

    public static string Read(string fileName) => File.ReadAllText(PathOf(fileName)).ReplaceLineEndings("\n");

    public static void AssertMatches(string actual, string fileName)
    {
        var path = PathOf(fileName);
        var normalized = actual.ReplaceLineEndings("\n");

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, normalized, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return;
        }

        if (!File.Exists(path))
        {
            Assert.Fail($"The reviewed file Fixtures/{fileName} does not exist. Run the tests with {UpdateVariable}=1 to create it, then review it.");
        }

        var expected = File.ReadAllText(path).ReplaceLineEndings("\n");

        if (!string.Equals(expected, normalized, StringComparison.Ordinal))
        {
            var expectedLines = expected.Split('\n');
            var actualLines = normalized.Split('\n');
            var line = Enumerable.Range(0, Math.Min(expectedLines.Length, actualLines.Length)).FirstOrDefault(index => expectedLines[index] != actualLines[index], Math.Min(expectedLines.Length, actualLines.Length));

            Assert.Fail($"The output differs from Fixtures/{fileName} at line {line + 1}.\nExpected: {(line < expectedLines.Length ? expectedLines[line] : "(end of file)")}\nActual:   {(line < actualLines.Length ? actualLines[line] : "(end of file)")}\nIf the change is intended, run the tests with {UpdateVariable}=1 and review the diff.");
        }
    }
}
