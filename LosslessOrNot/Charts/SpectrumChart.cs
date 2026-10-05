using System.ComponentModel;
using System.Drawing.Drawing2D;
using LosslessOrNot.Core.Spectral;

namespace LosslessOrNot.Charts;

/// <summary>
/// The averaged spectrum the verdict was derived from, with the thresholds it was
/// judged against: the content threshold, the -88 dB codec floor, the rounding noise
/// of the file's bit depth, where content ends and, for a lossy verdict, where the
/// wall is.
/// </summary>
public sealed class SpectrumChart : Control
{
    private const double MinDb = -140;

    private SpectralAnalysis? _analysis;
    private Point? _hover;

    public SpectrumChart()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw,
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
            _analysis = value;
            Invalidate();
        }
    }

    private Rectangle PlotArea()
    {
        int left = LogicalToDeviceUnits(62), right = LogicalToDeviceUnits(14);
        int top = LogicalToDeviceUnits(12), bottom = LogicalToDeviceUnits(26);
        return new Rectangle(left, top, Math.Max(0, Width - left - right), Math.Max(0, Height - top - bottom));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        Rectangle plot = PlotArea();
        SpectralAnalysis? a = _analysis;
        if (a is null || a.AvgSpectrumDb.Length == 0 || plot.Width < 2 || plot.Height < 2)
        {
            ChartPainting.DrawPlaceholder(g, ClientRectangle, Font, "Sélectionnez un fichier analysé pour afficher son spectre moyen.");
            return;
        }

        DrawGrid(g, plot, a);
        DrawCurve(g, plot, a);
        DrawThresholds(g, plot, a);
        if (_hover is { } p && plot.Contains(p))
        {
            DrawReadout(g, plot, a, p);
        }
    }

    private static int XForHz(Rectangle plot, double hz, double nyquistHz) =>
        plot.Left + (int)Math.Round(hz / nyquistHz * plot.Width);

    private static int YForDb(Rectangle plot, double db) =>
        plot.Top + (int)Math.Round(Math.Clamp(db, MinDb, 0) / MinDb * plot.Height);

    private void DrawGrid(Graphics g, Rectangle plot, SpectralAnalysis a)
    {
        using var gridPen = new Pen(ChartPainting.Grid);
        int tick = LogicalToDeviceUnits(4);
        for (double db = 0; db >= MinDb; db -= 20)
        {
            int y = YForDb(plot, db);
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            string label = $"{db:0} dB";
            Size size = TextRenderer.MeasureText(g, label, Font);
            TextRenderer.DrawText(g, label, Font, new Point(plot.Left - tick - size.Width, y - size.Height / 2), ForeColor);
        }

        double nyquistKHz = a.NyquistHz / 1000.0;
        double step = ChartPainting.NiceStep(nyquistKHz, Math.Max(2, plot.Width / LogicalToDeviceUnits(70)));
        foreach (double kHz in ChartPainting.Ticks(0, nyquistKHz, step))
        {
            int x = XForHz(plot, kHz * 1000, a.NyquistHz);
            g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
            string label = $"{kHz:0.#} kHz";
            Size size = TextRenderer.MeasureText(g, label, Font);
            TextRenderer.DrawText(g, label, Font, new Point(x - size.Width / 2, plot.Bottom + tick), ForeColor);
        }
    }

    private static void DrawCurve(Graphics g, Rectangle plot, SpectralAnalysis a)
    {
        double[] spectrum = a.AvgSpectrumDb;
        var points = new PointF[spectrum.Length + 2];
        points[0] = new PointF(plot.Left, plot.Bottom);
        for (int i = 0; i < spectrum.Length; i++)
        {
            // Each point is the loudest bin of its slice of 0..Nyquist.
            float x = plot.Left + (float)((i + 0.5) / spectrum.Length * plot.Width);
            points[i + 1] = new PointF(x, YForDb(plot, spectrum[i]));
        }
        points[^1] = new PointF(plot.Right, plot.Bottom);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var fill = new SolidBrush(Color.FromArgb(70, 0, 200, 255)))
        {
            g.FillPolygon(fill, points);
        }
        using (var line = new Pen(Color.FromArgb(120, 220, 255), 1.5f))
        {
            g.DrawLines(line, points[1..^1]);
        }
        g.SmoothingMode = SmoothingMode.None;
    }

    private void DrawThresholds(Graphics g, Rectangle plot, SpectralAnalysis a)
    {
        int pad = LogicalToDeviceUnits(6);

        // The content threshold rises above -70 dB on a track quiet enough for its
        // rounding noise to come close.
        double contentDb = a.ContentThresholdDb < 0 ? a.ContentThresholdDb : VerdictEngine.ContentFloorDb;
        DrawLevel(contentDb, $"seuil de contenu ({contentDb:0} dB)", ChartPainting.FloorMarker);
        DrawLevel(VerdictEngine.CodecFloorDb, $"plancher d'un codec ({VerdictEngine.CodecFloorDb:0} dB)", ChartPainting.WallMarker);
        if (a.QuantizationFloorDb is { } quantizationDb && quantizationDb > MinDb)
        {
            // On the left: it often sits close to the codec floor, labelled on the right.
            DrawLevel(
                quantizationDb,
                $"bruit de quantification {a.BitsPerSample} bits ({quantizationDb:0} dB)",
                ChartPainting.QuantizationMarker,
                left: true);
        }

        if (a.CutoffHz > 0)
        {
            int x = XForHz(plot, a.CutoffHz, a.NyquistHz);
            using Pen pen = ChartPainting.DashedPen(ChartPainting.ContentMarker);
            g.DrawLine(pen, x, plot.Top, x, plot.Bottom);
            string label = $"contenu jusqu'à {a.CutoffHz / 1000.0:0.00} kHz";
            Size size = TextRenderer.MeasureText(g, label, Font, Size.Empty, TextFormatFlags.NoPadding);
            int labelX = Math.Min(x + pad, plot.Right - size.Width - pad);
            ChartPainting.DrawLabel(g, label, Font, new Point(labelX, plot.Top + pad), ChartPainting.ContentMarker);
        }
        if (a.Verdict == SpectralVerdict.Lossy && a.CliffHz > 0)
        {
            int x = XForHz(plot, a.CliffHz, a.NyquistHz);
            using Pen pen = ChartPainting.DashedPen(ChartPainting.WallMarker);
            g.DrawLine(pen, x, plot.Top, x, plot.Bottom);
            string label = $"mur : {a.ShelfDropDb:0} dB sur 1 kHz";
            Size size = TextRenderer.MeasureText(g, label, Font, Size.Empty, TextFormatFlags.NoPadding);
            ChartPainting.DrawLabel(g, label, Font, new Point(Math.Max(plot.Left + pad, x - size.Width - pad), plot.Top + pad + size.Height + pad), ChartPainting.WallMarker);
        }

        void DrawLevel(double db, string label, Color color, bool left = false)
        {
            int y = YForDb(plot, db);
            using Pen pen = ChartPainting.DashedPen(color);
            g.DrawLine(pen, plot.Left, y, plot.Right, y);
            Size size = TextRenderer.MeasureText(g, label, Font, Size.Empty, TextFormatFlags.NoPadding);
            int x = left ? plot.Left + pad : plot.Right - size.Width - pad;
            ChartPainting.DrawLabel(g, label, Font, new Point(x, y - size.Height - 2), color);
        }
    }

    private void DrawReadout(Graphics g, Rectangle plot, SpectralAnalysis a, Point p)
    {
        double[] spectrum = a.AvgSpectrumDb;
        int i = Math.Clamp((int)((p.X - plot.Left) / (double)plot.Width * spectrum.Length), 0, spectrum.Length - 1);
        double hz = (i + 0.5) / spectrum.Length * a.NyquistHz;
        int x = plot.Left + (int)Math.Round((i + 0.5) / spectrum.Length * plot.Width);
        using (var pen = new Pen(Color.FromArgb(140, 255, 255, 255)))
        {
            g.DrawLine(pen, x, plot.Top, x, plot.Bottom);
        }
        string text = $"{hz / 1000:0.00} kHz · {spectrum[i]:0} dB";
        Size size = TextRenderer.MeasureText(g, text, Font, Size.Empty, TextFormatFlags.NoPadding);
        int pad = LogicalToDeviceUnits(6);
        int labelX = x + pad + size.Width < plot.Right ? x + pad : x - pad - size.Width;
        ChartPainting.DrawLabel(g, text, Font, new Point(labelX, plot.Bottom - size.Height - pad), Color.White);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _hover = e.Location;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
        Invalidate();
    }
}
