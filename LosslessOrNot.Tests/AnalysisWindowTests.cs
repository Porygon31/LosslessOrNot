using LosslessOrNot.Core;
using LosslessOrNot.Core.Spectral;
using Xunit.Abstractions;

namespace LosslessOrNot.Tests;

/// <summary>
/// The user chooses how many seconds are analysed. A file shorter than that is
/// analysed whole, and the result must say so.
/// </summary>
public sealed class AnalysisWindowTests(ITestOutputHelper output) : IDisposable
{
    private readonly FixtureDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [FfmpegFact]
    public async Task FileShorterThanTheWindow_IsAnalysedWhole_WithAWarning()
    {
        string path = await _dir.WhiteNoiseAsync("short.wav");

        SpectralAnalysis res = await SpectralAnalyzer.AnalyzeFileAsync(path, Ffmpeg.Exe, 48000, durationSec: 12, windowSec: 30);
        Log(res);

        Assert.InRange(res.AnalyzedSec, 11.9, 12.1);
        Assert.Equal(30, res.RequestedSec);
        Assert.Equal(0, res.TimeStartSec);
        AnalysisPoint warning = Assert.Single(res.Observations, point => point.Title == "Durée du fichier");
        Assert.Equal(PointSignal.Warning, warning.Signal);
        Assert.Contains("moins que les 30 s demandées", warning.Value);
        Assert.Contains(res.Evidence, line => line.Contains("analysé en entier"));
    }

    [FfmpegFact]
    public async Task WindowShorterThanTheFile_AnalysesOnlyThatMuch()
    {
        string path = await _dir.WhiteNoiseAsync("long.wav");

        SpectralAnalysis res = await SpectralAnalyzer.AnalyzeFileAsync(path, Ffmpeg.Exe, 48000, durationSec: 12, windowSec: 10);
        Log(res);

        Assert.InRange(res.AnalyzedSec, 9.95, 10.05);
        Assert.DoesNotContain(res.Observations, point => point.Title == "Durée du fichier");
    }

    [FfmpegFact]
    public async Task TrackAnalyzer_AnalysesTheChosenDuration()
    {
        string path = await _dir.WhiteNoiseAsync("track.wav");
        var analyzer = new TrackAnalyzer(Ffmpeg.Tools!, new AnalysisOptions(WindowSec: 5));

        TrackResult result = await analyzer.AnalyzeAsync(path);
        Log(result.Spectral!);

        Assert.Equal(5, result.Spectral!.RequestedSec);
        Assert.InRange(result.Spectral.AnalyzedSec, 4.95, 5.05);
    }

    private void Log(SpectralAnalysis res) =>
        output.WriteLine(
            $"requested {res.RequestedSec} s, analysed {res.AnalyzedSec:0.000} s from {res.TimeStartSec} s "
            + $"({res.FramesAnalyzed} frames) -> {res.Verdict}");
}
