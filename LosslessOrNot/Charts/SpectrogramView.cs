using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using LosslessOrNot.Core.Spectral;

namespace LosslessOrNot.Charts;

/// <summary>
/// Interactive spectrogram of a <see cref="SpectralAnalysis"/>: time runs left to
/// right, frequency bottom to top. The wheel zooms around the cursor, dragging
/// pans, a double-click shows everything again, and hovering reads out the time,
/// frequency and level under the cursor.
/// </summary>
public sealed class SpectrogramView : Control
{
    private const double MaxZoom = 16;

    private SpectralAnalysis? _analysis;
    private SpectrogramPalette _palette = SpectrogramPalette.Magma;

    // One pixel per (column, row) of the analysis, rebuilt only when the analysis or
    // the palette changes. Zooming and panning merely pick another source rectangle,
    // so no per-pixel loop runs on mouse moves — the trap that froze the web UI.
    private Bitmap? _image;
    private double _zoom = 1;
    private double _firstColumn;
    private Point? _hover;
    private Point? _dragOrigin;
    private double _dragFirstColumn;

    public SpectrogramView()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw | ControlStyles.Selectable,
            true);
        BackColor = ChartPainting.Background;
        ForeColor = ChartPainting.Text;
    }

    /// <summary>The analysis to draw; null shows a placeholder.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SpectralAnalysis? Analysis
    {
        get => _analysis;
        set
        {
            if (ReferenceEquals(_analysis, value))
            {
                return;
            }
            _analysis = value;
            _zoom = 1;
            _firstColumn = 0;
            RebuildImage();
            Invalidate();
        }
    }

    [DefaultValue(SpectrogramPalette.Magma)]
    public SpectrogramPalette Palette
    {
        get => _palette;
        set
        {
            if (_palette == value)
            {
                return;
            }
            _palette = value;
            RebuildImage();
            Invalidate();
        }
    }

    private int ColumnCount => _image?.Width ?? 0;

    private double VisibleColumns => ColumnCount / _zoom;

    /// <summary>Shows the whole analysed window again.</summary>
    public void ResetView()
    {
        _zoom = 1;
        _firstColumn = 0;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _image?.Dispose();
            _image = null;
        }
        base.Dispose(disposing);
    }

    private void RebuildImage()
    {
        _image?.Dispose();
        _image = null;
        byte[][]? columns = _analysis?.Columns;
        if (columns is null || columns.Length == 0 || columns[0].Length == 0)
        {
            return;
        }

        int width = columns.Length, height = columns[0].Length;
        int[] lut = ColorPalettes.Lookup(_palette);
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var line = new int[width];
            for (int y = 0; y < height; y++)
            {
                // Low frequencies at the bottom, as every spectrogram convention expects.
                int row = height - 1 - y;
                for (int x = 0; x < width; x++)
                {
                    line[x] = lut[columns[x][row]];
                }
                Marshal.Copy(line, 0, data.Scan0 + y * data.Stride, width);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        _image = bitmap;
    }

    private Rectangle PlotArea()
    {
        int left = LogicalToDeviceUnits(54), right = LogicalToDeviceUnits(12);
        int top = LogicalToDeviceUnits(10), bottom = LogicalToDeviceUnits(26);
        return new Rectangle(left, top, Math.Max(0, Width - left - right), Math.Max(0, Height - top - bottom));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        Rectangle plot = PlotArea();
        if (_image is null || _analysis is null || plot.Width < 2 || plot.Height < 2)
        {
            ChartPainting.DrawPlaceholder(g, ClientRectangle, Font, "Sélectionnez un fichier analysé pour afficher son spectrogramme.");
            return;
        }

        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(
            _image, plot, new RectangleF((float)_firstColumn, 0, (float)VisibleColumns, _image.Height), GraphicsUnit.Pixel);
        g.PixelOffsetMode = PixelOffsetMode.Default;

        DrawFrequencyAxis(g, plot, _analysis);
        DrawTimeAxis(g, plot, _analysis);
        DrawMarkers(g, plot, _analysis);

        if (_hover is { } p && plot.Contains(p))
        {
            DrawReadout(g, plot, ReadoutAt(p, plot, _analysis));
        }
        else
        {
            TextRenderer.DrawText(
                g, "molette : zoom · glisser : déplacer · double-clic : vue complète", Font,
                new Point(plot.Left + LogicalToDeviceUnits(6), plot.Top + LogicalToDeviceUnits(4)),
                Color.FromArgb(150, 150, 160));
        }
    }

    private static int YForHz(Rectangle plot, double hz, double nyquistHz) =>
        plot.Bottom - (int)Math.Round(hz / nyquistHz * plot.Height);

    private void DrawFrequencyAxis(Graphics g, Rectangle plot, SpectralAnalysis a)
    {
        double nyquistKHz = a.NyquistHz / 1000.0;
        double step = ChartPainting.NiceStep(nyquistKHz, Math.Max(2, plot.Height / LogicalToDeviceUnits(34)));
        int tick = LogicalToDeviceUnits(4);
        using var pen = new Pen(ForeColor);
        foreach (double kHz in ChartPainting.Ticks(0, nyquistKHz, step))
        {
            int y = YForHz(plot, kHz * 1000, a.NyquistHz);
            g.DrawLine(pen, plot.Left - tick, y, plot.Left - 1, y);
            string label = $"{kHz:0.#} kHz";
            Size size = TextRenderer.MeasureText(g, label, Font);
            TextRenderer.DrawText(g, label, Font, new Point(plot.Left - tick - size.Width, y - size.Height / 2), ForeColor);
        }
    }

    private void DrawTimeAxis(Graphics g, Rectangle plot, SpectralAnalysis a)
    {
        double span = a.TimeEndSec - a.TimeStartSec;
        if (span <= 0 || ColumnCount == 0)
        {
            return;
        }
        double t0 = a.TimeStartSec + _firstColumn / ColumnCount * span;
        double t1 = a.TimeStartSec + (_firstColumn + VisibleColumns) / ColumnCount * span;
        double step = ChartPainting.NiceStep(t1 - t0, Math.Max(2, plot.Width / LogicalToDeviceUnits(80)));
        int tick = LogicalToDeviceUnits(4);
        using var pen = new Pen(ForeColor);
        foreach (double t in ChartPainting.Ticks(t0, t1, step))
        {
            int x = plot.Left + (int)Math.Round((t - t0) / (t1 - t0) * plot.Width);
            g.DrawLine(pen, x, plot.Bottom, x, plot.Bottom + tick);
            string label = $"{t:0.#} s";
            Size size = TextRenderer.MeasureText(g, label, Font);
            TextRenderer.DrawText(g, label, Font, new Point(x - size.Width / 2, plot.Bottom + tick), ForeColor);
        }
    }

    // Only where content ends: that is what the eye reads off a spectrogram. The
    // wall's slope is shown on the spectrum chart, where it can actually be seen —
    // a second line a few hundred Hz lower would only blur this one.
    private void DrawMarkers(Graphics g, Rectangle plot, SpectralAnalysis a)
    {
        if (a.CutoffHz <= 0 || a.CutoffHz >= a.NyquistHz)
        {
            return;
        }
        int y = YForHz(plot, a.CutoffHz, a.NyquistHz);
        using Pen pen = ChartPainting.DashedPen(ChartPainting.ContentMarker);
        g.DrawLine(pen, plot.Left, y, plot.Right, y);
        string label = $"contenu jusqu'à {a.CutoffHz / 1000.0:0.00} kHz";
        int height = TextRenderer.MeasureText(g, label, Font).Height;
        ChartPainting.DrawLabel(
            g, label, Font, new Point(plot.Left + LogicalToDeviceUnits(6), y - height - 2), ChartPainting.ContentMarker);
    }

    private string ReadoutAt(Point p, Rectangle plot, SpectralAnalysis a)
    {
        byte[][] columns = a.Columns;
        double fx = (p.X - plot.Left) / (double)plot.Width;
        double fy = 1 - (p.Y - plot.Top) / (double)plot.Height;
        int c = Math.Clamp((int)(_firstColumn + fx * VisibleColumns), 0, columns.Length - 1);
        int rows = columns[c].Length;
        int r = Math.Clamp((int)(fy * rows), 0, rows - 1);
        double t = a.TimeStartSec + (double)c / columns.Length * (a.TimeEndSec - a.TimeStartSec);
        double hz = r * a.FreqBinHz;
        double db = a.SpecFloorDb + columns[c][r] / 255.0 * (0 - a.SpecFloorDb);
        return $"{t:0.0} s · {hz / 1000:0.00} kHz · {db:0} dB";
    }

    private void DrawReadout(Graphics g, Rectangle plot, string text)
    {
        Size size = TextRenderer.MeasureText(g, text, Font, Size.Empty, TextFormatFlags.NoPadding);
        int pad = LogicalToDeviceUnits(6);
        ChartPainting.DrawLabel(g, text, Font, new Point(plot.Right - size.Width - pad, plot.Top + pad), Color.White);
    }

    private void SetFirstColumn(double value) =>
        _firstColumn = Math.Clamp(value, 0, Math.Max(0, ColumnCount - VisibleColumns));

    private void EndDrag()
    {
        _dragOrigin = null;
        Cursor = Cursors.Default;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Left && _image is not null)
        {
            _dragOrigin = e.Location;
            _dragFirstColumn = _firstColumn;
            Cursor = Cursors.SizeWE;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _hover = e.Location;
        if (_dragOrigin is { } origin)
        {
            Rectangle plot = PlotArea();
            if (plot.Width > 0)
            {
                SetFirstColumn(_dragFirstColumn - (e.X - origin.X) / (double)plot.Width * VisibleColumns);
            }
        }
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        EndDrag();
    }

    // Losing the capture (Alt+Tab mid-drag…) must end the drag too, or every later
    // mouse move would keep panning.
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        EndDrag();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_image is null)
        {
            return;
        }
        Rectangle plot = PlotArea();
        double fx = plot.Width > 0 ? Math.Clamp((e.X - plot.Left) / (double)plot.Width, 0, 1) : 0.5;
        // Keep the column under the cursor where it is.
        double anchor = _firstColumn + fx * VisibleColumns;
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.25 : 0.8), 1, MaxZoom);
        SetFirstColumn(anchor - fx * VisibleColumns);
        Invalidate();
        if (e is HandledMouseEventArgs handled)
        {
            handled.Handled = true;
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        ResetView();
    }
}
