namespace Governance.Auditor.Common;

/// <summary>An expected failure, such as a missing file or invalid input. The command line reports it without a stack trace.</summary>
internal sealed class GovernanceException : Exception
{
    public GovernanceException(string message)
        : base(message)
    {
    }

    public GovernanceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
