using System.Collections;

namespace LosslessOrNot;

/// <summary>The columns of the file list, in display order.</summary>
internal enum TrackColumn
{
    File,
    Format,
    SampleRate,
    Bits,
    BitRate,
    Content,
    Status,
    Confidence,
    Source,
}

/// <summary>Sorts the file list on one column, by value rather than by displayed text.</summary>
internal sealed class TrackComparer(TrackColumn column, SortOrder order) : IComparer
{
    public TrackColumn Column { get; } = column;

    public SortOrder Order { get; } = order;

    public int Compare(object? x, object? y)
    {
        var a = (TrackEntry)((ListViewItem)x!).Tag!;
        var b = (TrackEntry)((ListViewItem)y!).Tag!;
        int result = Column switch
        {
            TrackColumn.File => Text(a.FileName, b.FileName),
            TrackColumn.Format => Text(a.Result?.Info?.CodecName, b.Result?.Info?.CodecName),
            TrackColumn.SampleRate => Number(a.Result?.Info?.SampleRate, b.Result?.Info?.SampleRate),
            TrackColumn.Bits => Number(a.Result?.Info?.BitsPerSample, b.Result?.Info?.BitsPerSample),
            TrackColumn.BitRate => Number(a.Result?.Info?.BitRate, b.Result?.Info?.BitRate),
            TrackColumn.Content => Number(a.Result?.Spectral?.CutoffHz, b.Result?.Spectral?.CutoffHz),
            TrackColumn.Status => StatusStyle.Rank(a).CompareTo(StatusStyle.Rank(b)),
            TrackColumn.Confidence => Number(a.Result?.Spectral?.CutoffConfidence, b.Result?.Spectral?.CutoffConfidence),
            TrackColumn.Source => Text(a.Result?.Spectral?.LikelySource, b.Result?.Spectral?.LikelySource),
            _ => 0,
        };
        if (result == 0)
        {
            // Ties keep a stable, predictable order.
            result = Text(a.Path, b.Path);
        }
        return Order == SortOrder.Descending ? -result : result;
    }

    private static int Text(string? a, string? b) => string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);

    // Files not analysed yet have no value and sort before every number.
    private static int Number(double? a, double? b) => (a ?? double.MinValue).CompareTo(b ?? double.MinValue);
}
