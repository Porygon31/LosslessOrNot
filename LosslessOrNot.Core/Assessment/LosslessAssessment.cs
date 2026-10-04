namespace LosslessOrNot.Core.Assessment;

/// <summary>The overall answer for one file: what it claims to be, checked against its spectrum.</summary>
public enum LosslessStatus
{
    /// <summary>A lossless codec with a full-band spectrum.</summary>
    Authentic,

    /// <summary>A lossless codec, but the spectrum carries a lossy encoder's wall: a transcode.</summary>
    FakeLossless,

    /// <summary>A lossless codec with a lossy-looking spectrum, on too little evidence to assert it.</summary>
    Suspect,

    /// <summary>A lossless codec, but the spectrum cannot settle the question either way.</summary>
    Inconclusive,

    /// <summary>The codec itself is lossy (MP3, AAC…): not lossless, whatever the spectrum shows.</summary>
    LossyFormat,

    /// <summary>The file could not be probed or analysed.</summary>
    Error,
}

/// <param name="Label">Short status for the file list.</param>
/// <param name="Explanation">One or two sentences for the detail panel.</param>
/// <param name="IsHiRes">Authentic and at least 24-bit / 88.2 kHz.</param>
public sealed record LosslessAssessment(
    LosslessStatus Status,
    string Label,
    string Explanation,
    bool IsHiRes = false);
