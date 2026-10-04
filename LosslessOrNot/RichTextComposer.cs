namespace LosslessOrNot;

/// <summary>
/// Writes formatted, read-only text into a RichTextBox, one paragraph and one run
/// at a time. Shared by the detail panel and the "Explications" tab.
/// </summary>
internal sealed class RichTextComposer : IDisposable
{
    private readonly RichTextBox _box;

    public RichTextComposer(RichTextBox box)
    {
        _box = box;
        Bold = new Font(box.Font, FontStyle.Bold);
        Italic = new Font(box.Font, FontStyle.Italic);
        Title = new Font(box.Font.FontFamily, box.Font.SizeInPoints + 3, FontStyle.Bold, GraphicsUnit.Point);
        // Segoe UI lacks some of the symbols used (✔ ✘ ⚠); Segoe UI Symbol has them all.
        Symbol = new Font("Segoe UI Symbol", box.Font.SizeInPoints, FontStyle.Bold, GraphicsUnit.Point);
    }

    public Font Bold { get; }

    public Font Italic { get; }

    public Font Title { get; }

    public Font Symbol { get; }

    /// <summary>Empties the box and sets the tab stop that aligns what follows a label or a symbol.</summary>
    public void Reset(int tabStop)
    {
        _box.Clear();
        _box.SelectionTabs = [tabStop];
    }

    public void Heading(string text)
    {
        Gap();
        Line(text, font: Bold);
    }

    /// <summary>A whole paragraph in one style.</summary>
    public void Line(string text, int indent = 0, Font? font = null, Color? color = null)
    {
        Paragraph(indent);
        Run(text, font, color);
        EndParagraph();
    }

    /// <summary>A label and its value, the value aligned on the tab stop.</summary>
    public void Field(string label, string value)
    {
        Paragraph(0);
        Run(label + "\t", color: SystemColors.GrayText);
        Run(value);
        EndParagraph();
    }

    /// <summary>An empty line — except at the very top, where it would only waste space.</summary>
    public void Gap()
    {
        if (_box.TextLength > 0)
        {
            Paragraph(0);
            EndParagraph();
        }
    }

    /// <summary>
    /// Starts a paragraph: the runs added until <see cref="EndParagraph"/> share its
    /// indent, and its wrapped lines start <paramref name="hanging"/> pixels further in.
    /// </summary>
    public void Paragraph(int indent, int hanging = 0)
    {
        _box.Select(_box.TextLength, 0);
        _box.SelectionIndent = indent;
        _box.SelectionHangingIndent = hanging;
    }

    public void Run(string text, Font? font = null, Color? color = null)
    {
        _box.Select(_box.TextLength, 0);
        _box.SelectionFont = font ?? _box.Font;
        _box.SelectionColor = color ?? _box.ForeColor;
        _box.AppendText(text);
    }

    public void EndParagraph() => Run("\n");

    public void ScrollToTop()
    {
        _box.Select(0, 0);
        _box.ScrollToCaret();
    }

    public void Dispose()
    {
        Bold.Dispose();
        Italic.Dispose();
        Title.Dispose();
        Symbol.Dispose();
    }
}
