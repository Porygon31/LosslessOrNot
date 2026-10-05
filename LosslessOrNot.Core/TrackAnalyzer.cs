using LosslessOrNot.Core.Assessment;
using LosslessOrNot.Core.Media;
using LosslessOrNot.Core.Spectral;

namespace LosslessOrNot.Core;

/// <summary>Everything known about one file once it has been analysed.</summary>
/// <param name="Error">Why the probe or the analysis failed, if it did.</param>
public sealed record TrackResult(
    string Path,
    AudioInfo? Info,
    SpectralAnalysis? Spectral,
    LosslessAssessment Assessment,
    string? Error = null);

/// <summary>How files are analysed.</summary>
/// <param name="WindowSec">
/// Seconds of audio to analyse in each file, clamped to the range
/// <see cref="SpectralAnalyzer"/> allows. A shorter file is analysed whole.
/// </param>
/// <param name="FftSize">Samples per FFT frame; see <see cref="SpectralAnalyzer.IsValidFftSize"/>.</param>
/// <exception cref="ArgumentOutOfRangeException">An FFT size the analysis does not accept.</exception>
public sealed record AnalysisOptions(
    double WindowSec = SpectralAnalyzer.DefaultWindowSec,
    int FftSize = SpectralAnalyzer.DefaultFftSize)
{
    public double WindowSec { get; } =
        Math.Clamp(WindowSec, SpectralAnalyzer.MinWindowSec, SpectralAnalyzer.MaxWindowSec);

    public int FftSize { get; } = SpectralAnalyzer.IsValidFftSize(FftSize)
        ? FftSize
        : throw new ArgumentOutOfRangeException(
            nameof(FftSize),
            FftSize,
            $"A power of two from {SpectralAnalyzer.MinFftSize} to {SpectralAnalyzer.MaxFftSize}.");
}

/// <summary>Probes, decodes and analyses files. Safe to use from several threads at once.</summary>
public sealed class TrackAnalyzer(FfmpegTools tools, AnalysisOptions? options = null)
{
    public FfmpegTools Tools { get; } = tools;

    public AnalysisOptions Options { get; } = options ?? new AnalysisOptions();

    /// <summary>
    /// Never throws for a bad file: failures come back as an
    /// <see cref="LosslessStatus.Error"/> result, so that one unreadable file cannot
    /// stop a batch. Only cancellation throws.
    /// </summary>
    public async Task<TrackResult> AnalyzeAsync(string path, CancellationToken cancellationToken = default)
    {
        AudioInfo info;
        try
        {
            info = await AudioProbe.ProbeAsync(Tools.FfprobePath, path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new TrackResult(path, null, null, LosslessAssessor.Failed(ex.Message), ex.Message);
        }

        try
        {
            SpectralAnalysis spectral = await SpectralAnalyzer
                .AnalyzeFileAsync(
                    path,
                    Tools.FfmpegPath,
                    info.SampleRate,
                    info.DurationSec,
                    Options.WindowSec,
                    info.IntegerBitsPerSample,
                    Options.FftSize,
                    cancellationToken)
                .ConfigureAwait(false);
            return new TrackResult(path, info, spectral, LosslessAssessor.Assess(info, spectral));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A lossy codec is lossy whatever the spectrum says, so that answer
            // survives a failed FFT pass; a lossless claim simply cannot be checked.
            LosslessAssessment assessment = info.IsLosslessCodec
                ? LosslessAssessor.Failed(ex.Message)
                : LosslessAssessor.Assess(info, null);
            return new TrackResult(path, info, null, assessment, ex.Message);
        }
    }
}
