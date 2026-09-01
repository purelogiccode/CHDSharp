namespace CHDSharp;

/// <summary>
///     Captures a human-readable description of the most recent codec-level failure on the
///     current thread. Decompression codecs return bare <see cref="ChdError" /> codes through
///     fixed-signature delegates; this side-channel lets them attach the concrete reason
///     (e.g. "deflate stream ended after 120 of 4096 expected bytes") so that verification
///     failures can be diagnosed from logs and bug reports.
/// </summary>
internal static class ChdDiagnostics
{
    [ThreadStatic] private static string? _detail;

    /// <summary>Records a failure detail for the current thread, overwriting any previous one.</summary>
    internal static void SetDetail(string detail) => _detail = detail;

    /// <summary>
    ///     Returns the most recent failure detail recorded on the current thread (or
    ///     <c>null</c> when none was recorded) and clears it.
    /// </summary>
    internal static string? TakeDetail()
    {
        var detail = _detail;
        _detail = null;
        return detail;
    }
}
