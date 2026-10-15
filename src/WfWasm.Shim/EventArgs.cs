extern alias WfDraw;

namespace WfWasm.Shim;

// Event data, delegates and clipboard contracts (cf. System.Windows.Forms
// event args): plain data holders with the real member shapes, so rewritten
// handlers resolve. Members typed against graphics types that do not exist
// yet (Graphics, Image, IDataObject payloads) are omitted until the Drawing
// backend provides them; each omission is noted on the type.

public delegate void KeyEventHandler(object? sender, KeyEventArgs e);
public delegate void KeyPressEventHandler(object? sender, KeyPressEventArgs e);
public delegate void PreviewKeyDownEventHandler(object? sender, PreviewKeyDownEventArgs e);
public delegate void MouseEventHandler(object? sender, MouseEventArgs e);
public delegate void PaintEventHandler(object? sender, PaintEventArgs e);
public delegate void LayoutEventHandler(object? sender, LayoutEventArgs e);
public delegate void DragEventHandler(object? sender, DragEventArgs e);
public delegate void HelpEventHandler(object? sender, HelpEventArgs e);
public delegate void MeasureItemEventHandler(object? sender, MeasureItemEventArgs e);
public delegate void DrawItemEventHandler(object? sender, DrawItemEventArgs e);
public delegate void ScrollEventHandler(object? sender, ScrollEventArgs e);
public delegate void FormClosingEventHandler(object? sender, FormClosingEventArgs e);
public delegate void LinkClickedEventHandler(object? sender, LinkClickedEventArgs e);
public delegate void LinkLabelLinkClickedEventHandler(object? sender, LinkLabelLinkClickedEventArgs e);
public delegate void ToolStripDropDownClosedEventHandler(object? sender, ToolStripDropDownClosedEventArgs e);

/// <summary>Key press data (cf. KeyEventArgs).</summary>
public class KeyEventArgs : EventArgs
{
    public KeyEventArgs(Keys keyData)
    {
        KeyData = keyData;
    }

    public Keys KeyData { get; }
    public Keys KeyCode => KeyData & Keys.KeyCode;
    public Keys Modifiers => KeyData & Keys.Modifiers;
    public bool Shift => (KeyData & Keys.Shift) == Keys.Shift;
    public bool Control => (KeyData & Keys.Control) == Keys.Control;
    public bool Alt => (KeyData & Keys.Alt) == Keys.Alt;
    public bool Handled { get; set; }
    public bool SuppressKeyPress { get; set; }
}

/// <summary>Character key data (cf. KeyPressEventArgs).</summary>
public class KeyPressEventArgs : EventArgs
{
    public KeyPressEventArgs(char keyChar)
    {
        KeyChar = keyChar;
    }

    public char KeyChar { get; set; }
    public bool Handled { get; set; }
}

/// <summary>Pre-preview key data (cf. PreviewKeyDownEventArgs).</summary>
public class PreviewKeyDownEventArgs : EventArgs
{
    public PreviewKeyDownEventArgs(Keys keyData)
    {
        KeyData = keyData;
    }

    public Keys KeyData { get; }
    public Keys KeyCode => KeyData & Keys.KeyCode;
    public Keys Modifiers => KeyData & Keys.Modifiers;
    public bool Alt => (KeyData & Keys.Alt) == Keys.Alt;
    public bool Control => (KeyData & Keys.Control) == Keys.Control;
    public bool Shift => (KeyData & Keys.Shift) == Keys.Shift;
    public bool IsInputKey { get; set; }
}

/// <summary>Mouse data (cf. MouseEventArgs).</summary>
public class MouseEventArgs : EventArgs
{
    public MouseEventArgs(MouseButtons button, int clicks, int x, int y, int delta)
    {
        Button = button;
        Clicks = clicks;
        X = x;
        Y = y;
        Delta = delta;
    }

    public MouseButtons Button { get; }
    public int Clicks { get; }
    public int X { get; }
    public int Y { get; }
    public int Delta { get; }
    public WfDraw::System.Drawing.Point Location => new(X, Y);
}

/// <summary>Paint data (cf. PaintEventArgs).</summary>
public class PaintEventArgs : EventArgs
{
    public PaintEventArgs(WfDraw::System.Drawing.Rectangle clipRect)
    {
        ClipRectangle = clipRect;
    }

    public PaintEventArgs(WfDraw::System.Drawing.Graphics? graphics, WfDraw::System.Drawing.Rectangle clipRect)
        : this(clipRect)
    {
        Graphics = graphics;
    }

    public WfDraw::System.Drawing.Graphics? Graphics { get; set; }
    public WfDraw::System.Drawing.Rectangle ClipRectangle { get; }
}

/// <summary>Layout data (cf. LayoutEventArgs).</summary>
public class LayoutEventArgs : EventArgs
{
    public LayoutEventArgs(Control? affectedControl, string? affectedProperty)
    {
        AffectedControl = affectedControl;
        AffectedProperty = affectedProperty;
    }

    public Control? AffectedControl { get; }
    public string? AffectedProperty { get; }
}

/// <summary>Drag data (cf. DragEventArgs). The Data payload waits for a
/// clipboard backend; Effect/KeyState/position are available now.</summary>
public class DragEventArgs : EventArgs
{
    public DragEventArgs(IDataObject? data, int keyState, int x, int y, DragDropEffects allowedEffect, DragDropEffects effect)
    {
        Data = data;
        KeyState = keyState;
        X = x;
        Y = y;
        AllowedEffect = allowedEffect;
        Effect = effect;
    }

    public IDataObject? Data { get; }
    public DragDropEffects AllowedEffect { get; }
    public DragDropEffects Effect { get; set; }
    public int KeyState { get; }
    public int X { get; }
    public int Y { get; }
}

/// <summary>Help-request data (cf. HelpEventArgs).</summary>
public class HelpEventArgs : EventArgs
{
    public HelpEventArgs(WfDraw::System.Drawing.Point mousePos)
    {
        MousePos = mousePos;
    }

    public WfDraw::System.Drawing.Point MousePos { get; }
    public bool Handled { get; set; }
}

/// <summary>Owner-draw measure data (cf. MeasureItemEventArgs).</summary>
public class MeasureItemEventArgs : EventArgs
{
    public MeasureItemEventArgs(int index)
    {
        Index = index;
    }

    public int Index { get; }
    public int ItemHeight { get; set; }
    public int ItemWidth { get; set; }
}

/// <summary>Owner-draw paint data (cf. DrawItemEventArgs).</summary>
public class DrawItemEventArgs : EventArgs
{
    public DrawItemEventArgs(WfDraw::System.Drawing.Font? font, int index, DrawItemState state)
        : this(font, index, state, WfDraw::System.Drawing.Rectangle.Empty)
    {
    }

    public DrawItemEventArgs(WfDraw::System.Drawing.Font? font, int index, DrawItemState state, WfDraw::System.Drawing.Rectangle bounds)
    {
        Font = font;
        Index = index;
        State = state;
        Bounds = bounds;
    }

    public DrawItemEventArgs(WfDraw::System.Drawing.Graphics? graphics, WfDraw::System.Drawing.Font? font, WfDraw::System.Drawing.Rectangle bounds, int index, DrawItemState state)
        : this(font, index, state, bounds)
    {
        Graphics = graphics;
    }

    /// <summary>Creates owner-draw data with explicit colors (cf. DrawItemEventArgs(Graphics, Font, Rectangle, int, DrawItemState, Color, Color)).</summary>
    public DrawItemEventArgs(WfDraw::System.Drawing.Graphics graphics, WfDraw::System.Drawing.Font font, WfDraw::System.Drawing.Rectangle rect, int index, DrawItemState state, WfDraw::System.Drawing.Color foreColor, WfDraw::System.Drawing.Color backColor)
    {
        Graphics = graphics;
        Font = font;
        Bounds = rect;
        Index = index;
        State = state;
        ForeColor = foreColor;
        BackColor = backColor;
    }

    public WfDraw::System.Drawing.Graphics? Graphics { get; set; }
    public WfDraw::System.Drawing.Font? Font { get; }
    public int Index { get; }
    public DrawItemState State { get; }
    public WfDraw::System.Drawing.Rectangle Bounds { get; }

    /// <summary>Item text color (cf. ForeColor).</summary>
    public WfDraw::System.Drawing.Color ForeColor { get; }

    /// <summary>Item fill color (cf. BackColor).</summary>
    public WfDraw::System.Drawing.Color BackColor { get; }

    /// <summary>Fills the item background (cf. DrawBackground).</summary>
    public virtual void DrawBackground()
    {
        if (Graphics is null || BackColor.A == 0)
            return;
        using var brush = new WfDraw::System.Drawing.SolidBrush(BackColor);
        Graphics.FillRectangle(brush, Bounds);
    }

    /// <summary>Draws the focus rectangle (cf. DrawFocusRectangle).</summary>
    public virtual void DrawFocusRectangle()
    {
        if (Graphics is not null)
            ControlPaint.DrawFocusRectangle(Graphics, Bounds);
    }
}

/// <summary>Scroll data (cf. ScrollEventArgs).</summary>
public class ScrollEventArgs : EventArgs
{
    public ScrollEventArgs(ScrollEventType type, int newValue)
        : this(type, newValue, ScrollOrientation.HorizontalScroll)
    {
    }

    public ScrollEventArgs(ScrollEventType type, int newValue, ScrollOrientation orientation)
        : this(type, -1, newValue, orientation)
    {
    }

    public ScrollEventArgs(ScrollEventType type, int oldValue, int newValue, ScrollOrientation orientation)
    {
        Type = type;
        OldValue = oldValue;
        NewValue = newValue;
        ScrollOrientation = orientation;
    }

    public ScrollEventType Type { get; }
    public int OldValue { get; set; }
    public int NewValue { get; set; }
    public ScrollOrientation ScrollOrientation { get; }
}

/// <summary>Form-closing data (cf. FormClosingEventArgs).</summary>
public class FormClosingEventArgs : System.ComponentModel.CancelEventArgs
{
    public FormClosingEventArgs(CloseReason reason, bool cancel)
        : base(cancel)
    {
        CloseReason = reason;
    }

    public CloseReason CloseReason { get; }
}

/// <summary>Clipboard contract (cf. System.Windows.Forms.IDataObject).</summary>
public interface IDataObject
{
    object? GetData(string format);
    object? GetData(string format, bool autoConvert);
    object? GetData(Type format);
    void SetData(string format, object? data);
    void SetData(object? data);
    void SetData(string format, bool autoConvert, object? data);
    void SetData(Type format, object? data);
    bool GetDataPresent(string format);
    bool GetDataPresent(string format, bool autoConvert);
    bool GetDataPresent(Type format);
    string[] GetFormats();
    string[] GetFormats(bool autoConvert);
}

/// <summary>Dictionary-backed clipboard object (cf. DataObject).</summary>
public class DataObject : IDataObject
{
    private readonly Dictionary<string, object?> _data = new(StringComparer.Ordinal);

    public DataObject()
    {
    }

    public DataObject(object? data)
    {
        if (data is not null)
            _data[data.GetType().FullName ?? ""] = data;
    }

    public DataObject(string format, object? data)
    {
        SetData(format, data);
    }

    public object? GetData(string format) => GetData(format, true);

    public object? GetData(string format, bool autoConvert) =>
        _data.TryGetValue(format, out object? value) ? value : null;

    public object? GetData(Type format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return GetData(format.FullName ?? "");
    }

    public void SetData(string format, object? data) => SetData(format, true, data);

    public void SetData(object? data)
    {
        if (data is not null)
            SetData(data.GetType().FullName ?? "", data);
    }

    public void SetData(string format, bool autoConvert, object? data) => _data[format] = data;

    public void SetData(Type format, object? data)
    {
        ArgumentNullException.ThrowIfNull(format);
        SetData(format.FullName ?? "", data);
    }

    public bool GetDataPresent(string format) => GetDataPresent(format, true);

    public bool GetDataPresent(string format, bool autoConvert) => _data.ContainsKey(format);

    public bool GetDataPresent(Type format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return GetDataPresent(format.FullName ?? "");
    }

    public string[] GetFormats() => GetFormats(true);

    public string[] GetFormats(bool autoConvert) => _data.Keys.ToArray();
}

/// <summary>Link click data (cf. LinkClickedEventArgs).</summary>
public class LinkClickedEventArgs : EventArgs
{
    public LinkClickedEventArgs(string linkText)
    {
        LinkText = linkText;
    }

    public string LinkText { get; }
}

/// <summary>Link-label click data (cf. LinkLabelLinkClickedEventArgs).</summary>
public class LinkLabelLinkClickedEventArgs : EventArgs
{
    public LinkLabelLinkClickedEventArgs(LinkLabelLink link)
    {
        Link = link;
    }

    public LinkLabelLink? Link { get; }
}

/// <summary>Drop-down close data (cf. ToolStripDropDownClosedEventArgs).</summary>
public class ToolStripDropDownClosedEventArgs : EventArgs
{
    public ToolStripDropDownClosedEventArgs(ToolStripDropDownCloseReason reason)
    {
        CloseReason = reason;
    }

    public ToolStripDropDownCloseReason CloseReason { get; }
}

/// <summary>Drop-down closing data (cf. ToolStripDropDownClosingEventArgs).</summary>
public class ToolStripDropDownClosingEventArgs : System.ComponentModel.CancelEventArgs
{
    public ToolStripDropDownClosingEventArgs(ToolStripDropDownCloseReason reason, bool cancel = false)
        : base(cancel)
    {
        CloseReason = reason;
    }

    public ToolStripDropDownCloseReason CloseReason { get; }
}

/// <summary>Child-control event data (cf. ControlEventArgs).</summary>
public class ControlEventArgs : EventArgs
{
    /// <summary>Creates event data (cf. ControlEventArgs(Control)).</summary>
    public ControlEventArgs(Control? control)
    {
        Control = control;
    }

    /// <summary>Affected control (cf. Control).</summary>
    public Control? Control { get; }
}

/// <summary>Child-control event handler (cf. ControlEventHandler).</summary>
public delegate void ControlEventHandler(object? sender, ControlEventArgs e);

/// <summary>Handled mouse data (cf. HandledMouseEventArgs).</summary>
public class HandledMouseEventArgs : MouseEventArgs
{
    /// <summary>Creates handled mouse data (cf. HandledMouseEventArgs(...)).</summary>
    public HandledMouseEventArgs(MouseButtons button, int clicks, int x, int y, int delta)
        : base(button, clicks, x, y, delta)
    {
    }

    /// <summary>Creates handled mouse data (cf. HandledMouseEventArgs(..., bool)).</summary>
    public HandledMouseEventArgs(MouseButtons button, int clicks, int x, int y, int delta, bool defaultHandledValue)
        : base(button, clicks, x, y, delta)
    {
        Handled = defaultHandledValue;
    }

    /// <summary>Whether the event is handled (cf. Handled).</summary>
    public bool Handled { get; set; }
}

/// <summary>Link range (cf. LinkArea).</summary>
public struct LinkArea : IEquatable<LinkArea>
{
    /// <summary>Creates a range (cf. LinkArea(int, int)).</summary>
    public LinkArea(int start, int length)
    {
        Start = start;
        Length = length;
    }

    /// <summary>Range start (cf. Start).</summary>
    public int Start { get; set; }

    /// <summary>Range length (cf. Length).</summary>
    public int Length { get; set; }

    /// <summary>Whether the range is empty (cf. IsEmpty).</summary>
    public bool IsEmpty => Start == 0 && Length == 0;

    /// <inheritdoc/>
    public bool Equals(LinkArea other) => Start == other.Start && Length == other.Length;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is LinkArea other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Start, Length);

    /// <summary>Equality (cf. ==).</summary>
    public static bool operator ==(LinkArea left, LinkArea right) => left.Equals(right);

    /// <summary>Inequality (cf. !=).</summary>
    public static bool operator !=(LinkArea left, LinkArea right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => $"{{Start={Start}, Length={Length}}}";
}

/// <summary>Up-down button data (cf. UpDownEventArgs).</summary>
public class UpDownEventArgs : EventArgs
{
    /// <summary>Creates button data (cf. UpDownEventArgs(int)).</summary>
    public UpDownEventArgs(int buttonID)
    {
        ButtonID = buttonID;
    }

    /// <summary>Pressed button id (cf. ButtonID).</summary>
    public int ButtonID { get; }
}

/// <summary>Up-down button handler (cf. UpDownEventHandler).</summary>
public delegate void UpDownEventHandler(object? source, UpDownEventArgs e);

/// <summary>Acceleration step (cf. NumericUpDownAcceleration).</summary>
public class NumericUpDownAcceleration
{
    /// <summary>Creates a step (cf. NumericUpDownAcceleration(int, decimal)).</summary>
    public NumericUpDownAcceleration(int seconds, decimal increment)
    {
        Seconds = seconds;
        Increment = increment;
    }

    /// <summary>Seconds held before this step (cf. Seconds).</summary>
    public int Seconds { get; set; }

    /// <summary>Step size (cf. Increment).</summary>
    public decimal Increment { get; set; }
}

/// <summary>Acceleration table (cf. NumericUpDownAccelerationCollection).</summary>
public class NumericUpDownAccelerationCollection : System.Collections.ObjectModel.Collection<NumericUpDownAcceleration>
{
    /// <summary>Whether the table is read-only (cf. IsReadOnly).</summary>
    public bool IsReadOnly => false;

    /// <summary>Adds steps (cf. AddRange).</summary>
    public void AddRange(NumericUpDownAcceleration[] accelerations)
    {
        ArgumentNullException.ThrowIfNull(accelerations);
        foreach (NumericUpDownAcceleration acceleration in accelerations)
            Add(acceleration);
    }
}
