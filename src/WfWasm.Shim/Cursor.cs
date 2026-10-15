extern alias WfDraw;

namespace WfWasm.Shim;

/// <summary>Pointer shape (cf. System.Windows.Forms.Cursor).</summary>
public class Cursor : IEquatable<Cursor>
{
    public Cursor(IntPtr handle)
    {
        Handle = handle;
    }

    /// <summary>
    /// Loads cursor bytes (cf. Cursor(Stream)). The bytes are kept for
    /// round-tripping; there is no OS cursor behind the handle token.
    /// </summary>
    /// <param name="cursorStream">Cursor data stream.</param>
    public Cursor(System.IO.Stream cursorStream)
    {
        ArgumentNullException.ThrowIfNull(cursorStream);
        using var copy = new System.IO.MemoryStream();
        cursorStream.CopyTo(copy);
        Data = copy.ToArray();
    }

    /// <summary>Cursor bytes, when loaded from a stream.</summary>
    public byte[]? Data { get; }

    public IntPtr Handle { get; }
    public WfDraw::System.Drawing.Point HotSpot { get; init; }
    public WfDraw::System.Drawing.Size Size { get; init; } = new(32, 32);

    /// <summary>Caller data (cf. Tag).</summary>
    public object? Tag { get; set; }

    /// <summary>Copies the handle (cf. CopyHandle). Returns the same token: no OS cursor exists.</summary>
    public IntPtr CopyHandle() => Handle;

    private static Cursor? _current;
    private static WfDraw::System.Drawing.Point _position;

    public static Cursor? Current
    {
        get => _current;
        set => _current = value;
    }

    public static WfDraw::System.Drawing.Point Position
    {
        get => _position;
        set => _position = value;
    }

    public bool Equals(Cursor? other) => other is not null && Handle == other.Handle;

    public override bool Equals(object? obj) => obj is Cursor other && Equals(other);

    public override int GetHashCode() => Handle.GetHashCode();

    public static bool operator ==(Cursor? left, Cursor? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(Cursor? left, Cursor? right) => !(left == right);

    public override string ToString() => $"Cursor(0x{Handle:x})";
}

/// <summary>Stock cursors (cf. System.Windows.Forms.Cursors).</summary>
public static class Cursors
{
    public static Cursor AppStarting { get; } = new(1);
    public static Cursor Arrow { get; } = new(2);
    public static Cursor Cross { get; } = new(3);
    public static Cursor Default { get; } = Arrow;
    public static Cursor Hand { get; } = new(4);
    public static Cursor Help { get; } = new(5);
    public static Cursor HSplit { get; } = new(6);
    public static Cursor IBeam { get; } = new(7);
    public static Cursor No { get; } = new(8);
    public static Cursor NoMove2D { get; } = new(9);
    public static Cursor NoMoveHoriz { get; } = new(10);
    public static Cursor NoMoveVert { get; } = new(11);
    public static Cursor PanEast { get; } = new(12);
    public static Cursor PanNE { get; } = new(13);
    public static Cursor PanNorth { get; } = new(14);
    public static Cursor PanNW { get; } = new(15);
    public static Cursor PanSE { get; } = new(16);
    public static Cursor PanSouth { get; } = new(17);
    public static Cursor PanSW { get; } = new(18);
    public static Cursor PanWest { get; } = new(19);
    public static Cursor SizeAll { get; } = new(20);
    public static Cursor SizeNESW { get; } = new(21);
    public static Cursor SizeNS { get; } = new(22);
    public static Cursor SizeNWSE { get; } = new(23);
    public static Cursor SizeWE { get; } = new(24);
    public static Cursor UpArrow { get; } = new(25);
    public static Cursor VSplit { get; } = new(26);
    public static Cursor WaitCursor { get; } = new(27);
}
