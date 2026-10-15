// GDI interop shapes: exact .NET marshaling layouts, web-compatible
// (no native calls; handles are opaque and unsupported here).
using System.Runtime.InteropServices;

namespace System.Drawing.Interop;

/// <summary>Native font description (cf. System.Drawing.Interop.LOGFONT).</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
public unsafe struct LOGFONT
{
    public int lfHeight;
    public int lfWidth;
    public int lfEscapement;
    public int lfOrientation;
    public int lfWeight;
    public byte lfItalic;
    public byte lfUnderline;
    public byte lfStrikeOut;
    public byte lfCharSet;
    public byte lfOutPrecision;
    public byte lfClipPrecision;
    public byte lfQuality;
    public byte lfPitchAndFamily;
    private fixed char _lfFaceName[32];

    public Span<char> lfFaceName
    {
        get
        {
            fixed (char* p = _lfFaceName)
                return new Span<char>(p, 32);
        }
    }

    internal string FaceName()
    {
        Span<char> span = lfFaceName;
        int end = span.IndexOf('\0');
        return new string(span.Slice(0, end < 0 ? span.Length : end));
    }

    internal void SetFaceName(string? name)
    {
        Span<char> span = lfFaceName;
        span.Clear();
        if (!string.IsNullOrEmpty(name))
            name.AsSpan(0, Math.Min(name.Length, span.Length)).CopyTo(span);
    }
}
