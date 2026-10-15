extern alias WfDraw;

namespace WfWasm.Shim;

/// <summary>Inner spacing (cf. System.Windows.Forms.Padding).</summary>
public struct Padding : IEquatable<Padding>
{
    public Padding(int all)
        : this(all, all, all, all)
    {
    }

    public Padding(int left, int top, int right, int bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public int All
    {
        get => Top == Left && Top == Right && Top == Bottom ? Top : -1;
        set => Left = Top = Right = Bottom = value;
    }

    public int Left { get; set; }
    public int Top { get; set; }
    public int Right { get; set; }
    public int Bottom { get; set; }
    public int Horizontal => Left + Right;
    public int Vertical => Top + Bottom;
    public WfDraw::System.Drawing.Size Size => new(Horizontal, Vertical);

    public bool Equals(Padding other) =>
        Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;

    public override bool Equals(object? obj) => obj is Padding other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);

    public static bool operator ==(Padding left, Padding right) => left.Equals(right);

    public static bool operator !=(Padding left, Padding right) => !left.Equals(right);

    /// <summary>Zero padding (cf. Empty).</summary>
    public static readonly Padding Empty;

    /// <summary>Adds two paddings (cf. op_Addition).</summary>
    public static Padding operator +(Padding p1, Padding p2) =>
        new(p1.Left + p2.Left, p1.Top + p2.Top, p1.Right + p2.Right, p1.Bottom + p2.Bottom);

    public override string ToString() => $"{{Left={Left},Top={Top},Right={Right},Bottom={Bottom}}}";
}

/// <summary>Window-creation parameters (cf. System.Windows.Forms.CreateParams).</summary>
public class CreateParams
{
    public string? Caption { get; set; }
    public string? ClassName { get; set; }
    public int ClassStyle { get; set; }
    public int ExStyle { get; set; }
    public int Height { get; set; }
    public int Width { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Style { get; set; }
    public IntPtr Param { get; set; }
    public IntPtr Parent { get; set; }
}

/// <summary>Window message (cf. System.Windows.Forms.Message).</summary>
public struct Message
{
    public IntPtr HWnd { get; set; }
    public int Msg { get; set; }
    public IntPtr WParam { get; set; }
    public IntPtr LParam { get; set; }
    public IntPtr Result { get; set; }

    public static Message Create(IntPtr hWnd, int msg, IntPtr wparam, IntPtr lparam) =>
        new() { HWnd = hWnd, Msg = msg, WParam = wparam, LParam = lparam };

    public override string ToString() => $"msg=0x{Msg:x} wp=0x{WParam:x} lp=0x{LParam:x}";
}
