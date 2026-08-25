namespace CHDSharp.Models;

/// <summary>
///     Result of a <see cref="Chd.ComputeHashesWithReporting" /> operation, consolidating error code and hash
///     results.
/// </summary>
public sealed record ChdHashComputeResult
{
    /// <summary>Creates a new <see cref="ChdHashComputeResult" /> with the given error and results.</summary>
    public ChdHashComputeResult(ChdError error, IReadOnlyList<ChdHashResult> results)
    {
        Error = error;
        Results = results;
    }

    /// <summary>The error code indicating success or failure of the operation.</summary>
    public ChdError Error { get; }

    /// <summary>Computed hash results, one per region (track or whole image). Empty on error.</summary>
    public IReadOnlyList<ChdHashResult> Results { get; }

    /// <summary>
    ///     <c>true</c> if the operation completed successfully (<see cref="Error" /> is
    ///     <see cref="ChdError.Chderrnone" />).
    /// </summary>
    public bool IsSuccess => Error == ChdError.Chderrnone;
}
