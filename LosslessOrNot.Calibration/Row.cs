using System.Globalization;
using System.Text;
using LosslessOrNot.Core;
using LosslessOrNot.Core.Assessment;
using LosslessOrNot.Core.Spectral;

namespace LosslessOrNot.Calibration;

/// <summary>Everything measured on one file of the corpus: a genuine original or one of its fakes.</summary>
internal sealed record Row(
    int Group,
    string Source,
    string Recipe,
    bool IsTranscode,
    int SampleRate,
    int Bits,
    int Frames,
    LosslessStatus Status,
    LosslessStatus LegacyStatus,
    double Confidence,
    double LossyProbability,
    bool HasCodecWall,
    int CutoffHz,
    double LegacyContentTopHz,
    int CliffHz,
    double DropDb,
    double FloorDb,
    double? FloorAboveQuantizationDb,
    double? FloorCorrelation,
    double? FloorDepthDb,
    double? FloorSlopeDbPerKHz,
    double? Persistence,
    double? HighBandCorrelation,
    double[] Contributions,
    double WallTopDb = 0,
    double? FloorSpreadDb = null,
    double? MusicSpreadDb = null,
    double? HighBandDepthDb = null,
    double? HighBandSpreadDb = null,
    string FloorKind = "")
{
    /// <summary>The indicators, in the order of <see cref="Contributions"/> and of the fitted weights.</summary>
    public static readonly string[] Indicators = ["Steepness", "Position", "Floor", "Persistence", "HighBand"];

    public static Row From(int group, string source, string recipe, bool isTranscode, TrackResult result)
    {
        SpectralAnalysis s = result.Spectral ?? throw new InvalidOperationException(result.Error ?? "no spectrum");
        Measurements m = s.Measurements ?? throw new InvalidOperationException("no measurements");
        Score score = s.Score ?? throw new InvalidOperationException("no score");
        double[] contributions = [score.Steepness, score.Position, score.Floor, score.Persistence, score.HighBand];
        (LosslessStatus legacy, double legacyTop) = Legacy(s);
        return new Row(
            group,
            source,
            recipe,
            isTranscode,
            s.SampleRate,
            s.BitsPerSample,
            s.FramesAnalyzed,
            result.Assessment.Status,
            legacy,
            s.CutoffConfidence,
            s.LossyProbability,
            s.HasCodecWall,
            s.CutoffHz,
            legacyTop,
            s.CliffHz,
            s.ShelfDropDb,
            s.EnergyAboveCutoffDb,
            m.FloorAboveQuantizationDb,
            m.Wall?.Correlation,
            m.Wall?.DepthDb,
            m.FloorSlopeDbPerKHz,
            m.Wall?.Persistence,
            m.HighBand?.Correlation,
            contributions,
            m.WallTopDb,
            m.Wall?.FloorSpreadDb,
            m.Wall?.MusicSpreadDb,
            m.HighBand?.DepthDb,
            m.HighBand?.MusicSpreadDb,
            score.FloorKind.ToString())
        {
            HasCodecCandidate = ConfidenceModel.IsCodecCandidate(m),
        };
    }

    /// <summary>
    /// The status the rules ported from Go gave before the probability: the three
    /// criteria with the -88 dB floor, and content above -70 dB beyond 20.5 kHz. The
    /// content reach is read off the max-pooled spectrum, so it is approximate.
    /// </summary>
    private static (LosslessStatus Status, double ContentTopHz) Legacy(SpectralAnalysis s)
    {
        double top = 0;
        double[] spectrum = s.AvgSpectrumDb;
        for (int i = spectrum.Length - 1; i >= 0; i--)
        {
            if (spectrum[i] > VerdictEngine.ContentFloorDb)
            {
                top = (i + 1.0) / spectrum.Length * s.NyquistHz;
                break;
            }
        }
        bool wall = s.ShelfDropDb >= VerdictEngine.WallDropMinDb
            && s.CliffHz > 0 && s.CliffHz <= VerdictEngine.CodecMaxWallHz
            && s.EnergyAboveCutoffDb <= VerdictEngine.CodecFloorDb;
        if (wall)
        {
            double confidence = s.ShelfDropDb switch { >= 35 => 0.95, >= 27 => 0.85, _ => 0.75 };
            if (s.FramesAnalyzed < VerdictEngine.MinFramesForFullConfidence)
            {
                confidence *= VerdictEngine.FewFramesFactor;
            }
            return (confidence >= LosslessAssessor.FakeLosslessMinConfidence ? LosslessStatus.FakeLossless : LosslessStatus.Suspect, top);
        }
        return (top >= VerdictEngine.LosslessTopHz ? LosslessStatus.Authentic : LosslessStatus.Inconclusive, top);
    }

    /// <summary>
    /// Whether the spectrum shows an abrupt wall where encoders cut. A transcode
    /// without one — an encoder passing the full band — looks exactly like its
    /// original: no spectral analysis can tell them apart, and no weight can help.
    /// </summary>
    public bool HasCodecCandidate { get; init; }

    /// <summary>The status these measurements would get with another probability, by the verdict rules of the app.</summary>
    public LosslessStatus StatusFor(double probability, double fakeLosslessMinConfidence = LosslessAssessor.FakeLosslessMinConfidence)
    {
        double factor = Frames < VerdictEngine.MinFramesForFullConfidence ? VerdictEngine.FewFramesFactor : 1;
        if (HasCodecWall && probability >= 0.5)
        {
            return probability * factor >= fakeLosslessMinConfidence ? LosslessStatus.FakeLossless : LosslessStatus.Suspect;
        }
        return CutoffHz >= VerdictEngine.LosslessTopHz && probability <= VerdictEngine.LosslessMaxLossyProbability
            ? LosslessStatus.Authentic
            : LosslessStatus.Inconclusive;
    }

    public static string CsvHeader =>
        "group,source,recipe,transcode,sample_rate,bits,frames,status,legacy_status,confidence,lossy_probability,codec_wall,"
        + "cutoff_hz,legacy_content_top_hz,wall_hz,drop_db,floor_db,floor_above_quantization_db,floor_correlation,floor_depth_db,floor_slope_db_per_khz,"
        + "persistence,high_band_correlation,wall_top_db,floor_spread_db,music_spread_db,high_band_depth_db,high_band_music_spread_db,floor_kind,"
        + string.Join(',', Indicators.Select(name => "w_" + name.ToLowerInvariant()));

    public string ToCsv()
    {
        var b = new StringBuilder();
        b.Append(Group).Append(',').Append(Quote(Source)).Append(',').Append(Recipe).Append(',').Append(IsTranscode ? 1 : 0);
        foreach (object? value in new object?[]
        {
            SampleRate, Bits, Frames, Status, LegacyStatus, Confidence, LossyProbability, HasCodecWall ? 1 : 0, CutoffHz,
            LegacyContentTopHz, CliffHz, DropDb, FloorDb, FloorAboveQuantizationDb, FloorCorrelation, FloorDepthDb, FloorSlopeDbPerKHz, Persistence,
            HighBandCorrelation, WallTopDb, FloorSpreadDb, MusicSpreadDb, HighBandDepthDb, HighBandSpreadDb, FloorKind,
        })
        {
            b.Append(',').Append(Format(value));
        }
        foreach (double c in Contributions)
        {
            b.Append(',').Append(Format(c));
        }
        return b.ToString();
    }

    private static string Format(object? value) => value switch
    {
        null => "",
        double d => d.ToString("0.####", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static string Quote(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";
}
