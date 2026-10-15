// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Drawing;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;

namespace System.Windows.Forms;

public partial class Control
{
    /// <summary>
    ///  Wrapper for a <see cref="Drawing.Font"/>'s <see cref="HFONT"/> with WASM safety fallback.
    /// </summary>
    internal sealed class FontHandleWrapper : IDisposable
    {
        private HFONT _handle;

        internal FontHandleWrapper(Font font)
        {
            var h = (HFONT)font.ToHfont();
            _handle = !h.IsNull ? h : (HFONT)(nint)1;
        }

        internal HFONT Handle => !_handle.IsNull ? _handle : (HFONT)(nint)1;

        public void Dispose()
        {
            if (!_handle.IsNull)
            {
                PInvokeCore.DeleteObject(_handle);
                _handle = default;
            }

            GC.SuppressFinalize(this);
        }

        ~FontHandleWrapper() => Dispose();
    }
}
