// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using Windows.Win32.Graphics.Gdi;

namespace System.Private.Windows.Graphics;

internal class IBitmap
{
    public virtual HBITMAP GetHbitmap() => default;
    public virtual Size Size => default;

    public static implicit operator IBitmap?(Bitmap? bitmap) => bitmap is null ? null : new BitmapWrapper(bitmap);

    private sealed class BitmapWrapper : IBitmap
    {
        private readonly Bitmap _bitmap;
        public BitmapWrapper(Bitmap bitmap) => _bitmap = bitmap;
        public override HBITMAP GetHbitmap() => (HBITMAP)_bitmap.GetHbitmap();
        public override Size Size => _bitmap.Size;
    }
}
