using LosslessOrNot.Core;

namespace LosslessOrNot;

internal enum TrackState
{
    /// <summary>Waiting for the user to start an analysis.</summary>
    Pending,

    /// <summary>Part of the running batch, not started yet.</summary>
    Queued,

    Analyzing,

    Done,
}

/// <summary>One row of the file list. Only ever touched on the UI thread.</summary>
internal sealed class TrackEntry(string path)
{
    public string Path { get; } = path;

    public string FileName => System.IO.Path.GetFileName(Path);

    public TrackState State { get; set; } = TrackState.Pending;

    public TrackResult? Result { get; set; }
}
