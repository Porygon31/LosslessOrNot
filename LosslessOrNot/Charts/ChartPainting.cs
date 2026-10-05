using System.Drawing.Drawing2D;

namespace LosslessOrNot.Charts;

/// <summary>Drawing helpers shared by the spectrogram and the spectrum chart.</summary>
internal static class ChartPainting
{
    public static readonly Color Background = Color.FromArgb(16, 16, 20);
    public static readonly Color Text = Color.FromArgb(205, 205, 212);
    public static readonly Color Grid = Color.FromArgb(48, 255, 255, 255);
    public static readonly Color ContentMarker = Color.FromArgb(0, 200, 255);
    public static readonly Color WallMarker = Color.FromArgb(255, 92, 92);
    public static readonly Color FloorMarker = Color.FromArgb(255, 190, 70);
    public static readonly Color QuantizationMarker = Color.FromArgb(185, 150, 255);

    private static readonly double[] StepMultiples = [1, 2, 5];

    /// <summary>A tick spacing of 1, 2 or 5 × 10^n giving at most <paramref name="maxTicks"/> intervals.</summary>
    public static double NiceStep(double range, int maxTicks)
    {
        double raw = range / Math.Max(1, maxTicks);
        if (raw <= 0 || double.IsNaN(raw))
        {
            return 1;
        }
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        foreach (double multiple in StepMultiples)
        {
            if (multiple * magnitude >= raw)
            {
                return multiple * magnitude;
            }
        }
        return 10 * magnitude;
    }

    /// <summary>The multiples of <paramref name="step"/> lying in [<paramref name="from"/>, <paramref name="to"/>].</summary>
    public static IEnumerable<double> Ticks(double from, double to, double step)
    {
        // Integer multiples, so that repeated additions cannot drift.
        for (long k = (long)Math.Ceiling(from / step - 1e-9); k * step <= to + 1e-9; k++)
        {
            yield return k * step;
        }
    }

    public static Pen DashedPen(Color color, float width = 1f) => new(color, width) { DashStyle = DashStyle.Dash };

    /// <summary>Draws text on a translucent dark plate, for labels laid over data.</summary>
    public static void DrawLabel(Graphics g, string text, Font font, Point location, Color color)
    {
        Size size = TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.NoPadding);
        var plate = new Rectangle(location.X - 2, location.Y - 1, size.Width + 4, size.Height + 2);
        using var brush = new SolidBrush(Color.FromArgb(170, 10, 10, 14));
        g.FillRectangle(brush, plate);
        TextRenderer.DrawText(g, text, font, location, color, TextFormatFlags.NoPadding);
    }

    public static void DrawPlaceholder(Graphics g, Rectangle bounds, Font font, string text) =>
        TextRenderer.DrawText(
            g, text, font, bounds, Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
}
