namespace CHDSharp.Models;

/// <summary>The result of matching a CUE sheet against a known hash (CHDlite <c>match_cue</c> parity).</summary>
/// <param name="Style">The style whose normalized output hashes to <paramref name="Hash" />.</param>
/// <param name="CueData">The normalized CUE sheet in <see cref="Style" /> form.</param>
/// <param name="Hash">The hash of <see cref="CueData" /> (hex, lowercase).</param>
public sealed record CueMatchResult(CueStyle? Style, string? CueData, string? Hash);