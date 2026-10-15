extern alias WfDraw;

namespace WfWasm.Shim;

using Draw = WfDraw::System.Drawing;

/// <summary>
/// Button-like control base (cf. System.Windows.Forms.ButtonBase). Holds the
/// shared FlatStyle/image/text-rendering surface; Button and CheckBox build
/// on it instead of duplicating those members.
/// </summary>
public abstract class ButtonBase : Control
{
    /// <summary>Creates a button base (cf. ButtonBase()).</summary>
    protected ButtonBase()
    {
    }

    /// <summary>Creates a named button base.</summary>
    /// <param name="name">Control name.</param>
    protected ButtonBase(string name)
        : base(name)
    {
    }

    /// <summary>Paint style (cf. FlatStyle).</summary>
    public FlatStyle FlatStyle { get; set; } = FlatStyle.System;

    /// <summary>Image (cf. Image). Stored; painters read it.</summary>
    public WfDraw::System.Drawing.Image? Image { get; set; }

    /// <summary>Image alignment (cf. ImageAlign).</summary>
    public Draw.ContentAlignment ImageAlign { get; set; } = Draw.ContentAlignment.MiddleCenter;

    /// <summary>Image-list index (cf. ImageIndex).</summary>
    public int ImageIndex { get; set; } = -1;

    /// <summary>Image-list key (cf. ImageKey).</summary>
    public string ImageKey { get; set; } = string.Empty;

    /// <summary>Image list (cf. ImageList). Stored only.</summary>
    public ImageList? ImageList { get; set; }

    /// <summary>GDI text rendering switch (cf. UseCompatibleTextRendering). Stored only.</summary>
    public bool UseCompatibleTextRendering { get; set; }
}
