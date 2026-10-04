using System.ComponentModel;
using System.Windows.Forms.Design;

namespace LosslessOrNot;

/// <summary>A NumericUpDown that can sit in a ToolStrip, and be placed there from the designer.</summary>
[ToolStripItemDesignerAvailability(ToolStripItemDesignerAvailability.ToolStrip)]
public sealed class ToolStripNumericUpDown : ToolStripControlHost
{
    public ToolStripNumericUpDown()
        : base(new NumericUpDown())
    {
    }

    /// <summary>The hosted control.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public NumericUpDown NumericUpDown => (NumericUpDown)Control;
}
