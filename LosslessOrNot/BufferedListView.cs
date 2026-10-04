namespace LosslessOrNot;

/// <summary>A ListView that does not flicker while its rows are updated in quick succession.</summary>
public sealed class BufferedListView : ListView
{
    public BufferedListView()
    {
        DoubleBuffered = true;
    }
}
