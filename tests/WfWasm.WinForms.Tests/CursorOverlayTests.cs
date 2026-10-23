extern alias WasmDraw;

namespace WfWasm.WinForms.Tests;

// Headless-cursor overlay contract: stream/file construction never
// touches COM/OLE (E_NOTIMPL in the browser); bytes are kept for
// GetData, the .cur hotspot is parsed, and the sentinel handle
// answers. Desktop parity: null stream still throws ANE.
// Regression: func96 OOB trap <- TIE <- RadDragDropService..cctor <-
// CursorFromResource <- new Cursor(Stream) (fileexplorer/diagram/erp
// boot deaths).
public sealed class CursorOverlayTests
{
    private static byte[] CurBytes(int hotX = 7, int hotY = 9)
    {
        // Minimal ICONDIR header + one 32x32 entry, type 2 (.cur).
        byte[] bytes = new byte[22];
        bytes[2] = 2;
        bytes[4] = 1;
        bytes[6] = 32;
        bytes[7] = 32;
        bytes[10] = (byte)hotX;
        bytes[12] = (byte)hotY;
        return bytes;
    }

    [Fact]
    public void Stream_Cursor_Emulates_Without_Throwing()
    {
        using var stream = new MemoryStream(CurBytes());
        using var cursor = new System.Windows.Forms.Cursor(stream);

        Assert.Equal(new WasmDraw::System.Drawing.Point(7, 9), cursor.HotSpot);
        Assert.Equal(new IntPtr(1), cursor.Handle);
        Assert.Equal(System.Windows.Forms.SystemInformation.CursorSize, cursor.Size);
    }

    [Fact]
    public void Stream_Cursor_Keeps_Bytes_For_GetData()
    {
        byte[] bytes = CurBytes();
        using var stream = new MemoryStream(bytes);
        using var cursor = new System.Windows.Forms.Cursor(stream);

        // GetData is internal; exercise it via the Cursors-property path
        // indirectly: stock cursors carry no bytes and throw, while a
        // stream cursor must not take the InvalidOperation branch.
        // (Direct GetData coverage lives in the fork; here we assert the
        // observable emulation: handle + hotspot + copy + dispose.)
        Assert.Equal(cursor.Handle, cursor.CopyHandle());
        cursor.Dispose();
        Assert.Equal(new IntPtr(1), cursor.Handle);
    }

    [Fact]
    public void Garbage_Bytes_Answer_Default_Hotspot_Not_Throw()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        using var cursor = new System.Windows.Forms.Cursor(stream);

        Assert.Equal(WasmDraw::System.Drawing.Point.Empty, cursor.HotSpot);
        Assert.Equal(new IntPtr(1), cursor.Handle);
    }

    [Fact]
    public void Null_Stream_Still_Throws_ArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => new System.Windows.Forms.Cursor((Stream)null!));
    }

    [Fact]
    public void Zero_Handle_Answers_Sentinel_Not_Throw()
    {
        // Browser emulation: third-party controls materialize cursors
        // from native handles that do not exist headless (Telerik
        // viewer cctor path); a zero handle must degrade to the
        // sentinel, not abort construction into the ctor-guard path.
        using var cursor = new System.Windows.Forms.Cursor(IntPtr.Zero);

        Assert.Equal(new IntPtr(1), cursor.Handle);
    }

    [Fact]
    public void Nonzero_Handle_Passes_Through()
    {
        using var cursor = new System.Windows.Forms.Cursor(new IntPtr(42));

        Assert.Equal(new IntPtr(42), cursor.Handle);
    }

    [Fact]
    public void Stock_Cursors_Still_Resolve()
    {
        // Upstream Cursors.cs is untouched: IDC_* via LoadCursor plus
        // .cur resources through the emulated stream path.
        Assert.Equal(new IntPtr(1), System.Windows.Forms.Cursors.Arrow.Handle);
        Assert.Equal(new IntPtr(1), System.Windows.Forms.Cursors.HSplit.Handle);
        Assert.Equal(new IntPtr(1), System.Windows.Forms.Cursors.WaitCursor.Handle);
    }
}
