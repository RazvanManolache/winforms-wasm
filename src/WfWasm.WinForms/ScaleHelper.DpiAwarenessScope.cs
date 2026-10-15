// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows.Forms;

internal static partial class ScaleHelper
{
    /// <summary>
    ///  Managed no-op DpiAwarenessScope for WebAssembly.
    /// </summary>
    private class DpiAwarenessScope : IDisposable
    {
        public DpiAwarenessScope(DPI_AWARENESS_CONTEXT context, DPI_HOSTING_BEHAVIOR behavior)
        {
        }

        public void Dispose()
        {
        }
    }
}
