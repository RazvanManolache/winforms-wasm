// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using Windows.Win32.Foundation;

namespace Windows.Win32;

internal static class WindowTextStorage
{
    private static readonly ConcurrentDictionary<nint, string> s_texts = new();

    public static void Set(HWND hWnd, string? text)
    {
        s_texts[(nint)hWnd.Value] = text ?? string.Empty;
    }

    public static string Get(HWND hWnd)
    {
        return s_texts.TryGetValue((nint)hWnd.Value, out var text) ? text : string.Empty;
    }

    public static int GetLength(HWND hWnd)
    {
        return s_texts.TryGetValue((nint)hWnd.Value, out var text) ? text.Length : 0;
    }

    public static unsafe int CopyTo(HWND hWnd, PWSTR lpString, int nMaxCount)
    {
        if (lpString.Value == null || nMaxCount <= 0)
        {
            return 0;
        }

        string text = Get(hWnd);
        int len = Math.Min(text.Length, nMaxCount - 1);
        for (int i = 0; i < len; i++)
        {
            lpString.Value[i] = text[i];
        }
        lpString.Value[len] = '\0';
        return len;
    }
}
