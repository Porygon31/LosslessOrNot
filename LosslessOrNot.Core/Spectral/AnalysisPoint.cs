namespace LosslessOrNot.Core.Spectral;

/// <summary>What one point of the analysis says about the file.</summary>
public enum PointSignal
{
    /// <summary>Looks like the work of a lossy encoder (✘).</summary>
    LossyLike,

    /// <summary>Does not look like a lossy encoder (✔).</summary>
    LosslessLike,

    /// <summary>Context, neither for nor against (ℹ).</summary>
    Info,

    /// <summary>Limits how far the result can be trusted (⚠).</summary>
    Warning,
}

/// <summary>One step of the reasoning behind a verdict, explained in plain French.</summary>
/// <param name="Title">What is measured, e.g. "Coupure nette".</param>
/// <param name="Value">The value measured on this file.</param>
/// <param name="Explanation">What the measurement means and why it matters.</param>
/// <param name="Threshold">The rule applied to the value; empty when there is none.</param>
public sealed record AnalysisPoint(
    string Title,
    string Value,
    string Explanation,
    string Threshold,
    PointSignal Signal);

/// <summary>What was asked of an analysis, for the explanations it produces.</summary>
/// <param name="StartSec">Where the decoded audio starts in the track.</param>
/// <param name="RequestedSec">How many seconds the user asked to analyse.</param>
/// <param name="FileDurationSec">The file's duration; 0 when unknown.</param>
internal readonly record struct AnalysisRequest(double StartSec, double RequestedSec, double FileDurationSec);
