// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Windows.Win32;

internal static partial class PInvokeCore
{
    public static string GetWindowText<T>(T hWnd) where T : IHandle<HWND>
    {
        string text = WindowTextStorage.Get(hWnd.Handle);
        GC.KeepAlive(hWnd.Wrapper);
        return text;
    }
}
