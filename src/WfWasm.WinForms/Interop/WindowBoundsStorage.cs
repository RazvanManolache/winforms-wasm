// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Drawing;
using Windows.Win32.Foundation;

namespace Windows.Win32;

internal static class WindowBoundsStorage
{
    private const int CW_USEDEFAULT = unchecked((int)0x80000000);
    private static readonly ConcurrentDictionary<nint, Rectangle> s_bounds = new();

    public static void Set(HWND hWnd, int x, int y, int width, int height)
    {
        if (hWnd.Value == 0) return;
        int realX = (x == CW_USEDEFAULT) ? 0 : x;
        int realY = (y == CW_USEDEFAULT) ? 0 : y;
        int realW = (width == CW_USEDEFAULT || width < 0) ? 0 : width;
        int realH = (height == CW_USEDEFAULT || height < 0) ? 0 : height;

        s_bounds[(nint)hWnd.Value] = new Rectangle(realX, realY, realW, realH);
    }

    public static Rectangle Get(HWND hWnd)
    {
        if (hWnd.Value == 0) return Rectangle.Empty;
        return s_bounds.TryGetValue((nint)hWnd.Value, out var r) ? r : Rectangle.Empty;
    }

    public static void Update(HWND hWnd, int x, int y, int cx, int cy, SET_WINDOW_POS_FLAGS uFlags)
    {
        if (hWnd.Value == 0) return;
        var cur = Get(hWnd);
        bool noMove = (uFlags & SET_WINDOW_POS_FLAGS.SWP_NOMOVE) != 0;
        bool noSize = (uFlags & SET_WINDOW_POS_FLAGS.SWP_NOSIZE) != 0;

        int newX = noMove ? cur.X : ((x == CW_USEDEFAULT) ? cur.X : x);
        int newY = noMove ? cur.Y : ((y == CW_USEDEFAULT) ? cur.Y : y);
        int newW = noSize ? cur.Width : ((cx == CW_USEDEFAULT || cx < 0) ? cur.Width : cx);
        int newH = noSize ? cur.Height : ((cy == CW_USEDEFAULT || cy < 0) ? cur.Height : cy);

        s_bounds[(nint)hWnd.Value] = new Rectangle(newX, newY, newW, newH);
    }
}
