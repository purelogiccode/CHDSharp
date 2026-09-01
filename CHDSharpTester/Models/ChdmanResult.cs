namespace CHDSharpTester.Models;

/// <summary>Represents the result of a chdman process execution.</summary>
internal sealed class ChdmanResult
{
    /// <summary>The process exit code.</summary>
    internal int ExitCode;

    /// <summary>The captured standard error text.</summary>
    internal string StdErr = "";

    /// <summary>The captured standard output text.</summary>
    internal string StdOut = "";

    /// <summary>Gets the combined standard output and standard error text.</summary>
    internal string All => StdOut + "\n" + StdErr;
}
