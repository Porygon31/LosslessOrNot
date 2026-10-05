namespace LosslessOrNot.Core.Spectral;

/// <summary>What <see cref="VerdictEngine"/> measured on one file, before any judgement.</summary>
/// <param name="ContentTopHz">The highest frequency still carrying content.</param>
/// <param name="WallHz">Where the steepest 1 kHz drop above 8 kHz starts; 0 when there is none.</param>
/// <param name="WallDropDb">How much that drop loses over 1 kHz.</param>
/// <param name="FloorAboveDb">The level 1–3 kHz above the wall, in dB relative to the spectrum's peak.</param>
/// <param name="BitsPerSample">The integer depth the file declares; 0 when unknown or floating-point.</param>
/// <param name="QuantizationFloorDb">
/// The rounding noise that depth implies in each bin, in dB relative to the peak;
/// null when the depth is unknown.
/// </param>
/// <param name="HighBandDb">The averaged level beyond 21 kHz, relative to the peak; null below 21.3 kHz of Nyquist.</param>
/// <param name="Wall">How the region above the wall behaves frame by frame; null when it cannot be measured.</param>
/// <param name="HighBand">Whether what lies beyond 21 kHz follows the music; null when it cannot be measured.</param>
/// <param name="WallTopDb">The smoothed level where the steepest drop starts: the music just under the wall.</param>
/// <param name="FloorSlopeDbPerKHz">
/// How the averaged spectrum slopes 1 to 5 kHz above the wall: music keeps falling
/// there, clipping distortion and dither lie flat or rise. Null with less than 2 kHz
/// of room above the wall.
/// </param>
/// <param name="FftSize">Samples per frame.</param>
internal sealed record Measurements(
    double ContentTopHz,
    double WallHz,
    double WallDropDb,
    double FloorAboveDb,
    double NyquistHz,
    int Frames,
    int BitsPerSample,
    double? QuantizationFloorDb,
    double? HighBandDb,
    WallTracking? Wall,
    HighBandTracking? HighBand,
    double WallTopDb = 0,
    double? FloorSlopeDbPerKHz = null,
    int FftSize = SpectralAnalyzer.DefaultFftSize)
{
    /// <summary>The floor above the wall compared with the format's own rounding noise.</summary>
    public double? FloorAboveQuantizationDb => FloorAboveDb - QuantizationFloorDb;

    /// <summary>The band beyond 21 kHz compared with the format's own rounding noise.</summary>
    public double? HighBandAboveQuantizationDb => HighBandDb - QuantizationFloorDb;

    /// <summary>
    /// Whether the steepest drop is abrupt enough to be a wall. Its position and
    /// what lies above it only mean something when it is.
    /// </summary>
    /// <remarks>
    /// A lossy decode of a loud master clips, and the distortion fills the region
    /// above the wall with a flat floor 20 dB or so under the music: the drop onto it
    /// can no longer reach <see cref="VerdictEngine.WallDropMinDb"/>. A smaller drop
    /// counts when it lands on such a flat floor, which music, still falling, never is.
    /// </remarks>
    public bool IsSteep => WallHz > 0
        && (WallDropDb >= VerdictEngine.WallDropMinDb
            || (WallDropDb >= VerdictEngine.FlatFloorWallDropMinDb
                && FloorSlopeDbPerKHz >= ConfidenceModel.FlatSlopeDbPerKHz));
}
