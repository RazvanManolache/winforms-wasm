// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Drawing;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace System.Drawing
{
    [Flags]
    internal enum ApplyGraphicsProperties
    {
        None = 0x0000_0000,
        Clipping = 0x0000_0001,
        TranslateTransform = 0x0000_0002,
        All = Clipping | TranslateTransform
    }

    // Fork: exposes the IDeviceContext wrapped by DeviceContextExtensions'
    // adapter so the HDC scope can register the paint Graphics.
    internal interface IDeviceContextSource
    {
        IDeviceContext DeviceContext { get; }
    }

    internal interface IGraphicsHdcProvider
    {
        bool IsGraphicsStateClean { get; }
        HDC GetHdc();
        IGraphics? GetGraphics(bool createIfNeeded);
    }

    internal class IGraphics : IHdcContext
    {
        public Graphics? Graphics { get; }
        public IGraphics(Graphics? graphics) => Graphics = graphics;
        public static implicit operator IGraphics?(Graphics? graphics) => graphics is null ? null : new(graphics);
        public static implicit operator Graphics?(IGraphics? ig) => ig?.Graphics;
        public (HDC hdc, int saveState) GetHdc(ApplyGraphicsProperties apply, bool alwaysSaveState) => (default, 0);
        public HDC GetHdc() => default;
        public void ReleaseHdc() { }
    }
}

namespace Windows.Win32.Graphics.Gdi
{
    #if DEBUG
    internal class DeviceContextHdcScope : DisposalTracking.Tracker, IDisposable
    #else
    internal readonly ref struct DeviceContextHdcScope
    #endif
    {
        public IHdcContext? DeviceContext { get; }
        public HDC HDC { get; }
        private readonly int _savedHdcState;
        private readonly global::System.IDisposable? _fillScope;

        public DeviceContextHdcScope(
            IHdcContext deviceContext,
            ApplyGraphicsProperties applyGraphicsState = ApplyGraphicsProperties.All,
            bool saveHdcState = false)
        {
            DeviceContext = deviceContext;
            _savedHdcState = 0;
            HDC = deviceContext?.GetHdc() ?? default;
        // WASM bridges: HDCs are zero here, so register the Graphics behind
        // this scope; FillRect/DrawTextEx then paint for real on it.
        global::System.Drawing.Graphics? fillTarget = (deviceContext as IGraphics)?.Graphics
            ?? (deviceContext as IGraphicsHdcProvider)?.GetGraphics(createIfNeeded: false)?.Graphics;
        if (fillTarget is null && deviceContext is IDeviceContextSource source)
        {
            fillTarget = global::System.Windows.Forms.DeviceContextExtensions.TryGetGraphics(source.DeviceContext);
        }

        if (fillTarget is not null)
        {
            _fillScope = Windows.Win32.PInvoke.WasmFillBridge.TrackTarget(HDC, fillTarget);
        }
        }

        public DeviceContextHdcScope(
            IHdcContext deviceContext,
            bool applyGraphicsState,
            bool saveHdcState = false)
            : this(deviceContext, applyGraphicsState ? ApplyGraphicsProperties.All : ApplyGraphicsProperties.None, saveHdcState)
        {
        }

        public DeviceContextHdcScope(
            global::System.Drawing.Graphics graphics,
            ApplyGraphicsProperties applyGraphicsState = ApplyGraphicsProperties.All,
            bool saveHdcState = false)
            : this((IHdcContext)new IGraphics(graphics), applyGraphicsState, saveHdcState)
        {
        }

        public DeviceContextHdcScope(
            global::System.Drawing.Graphics graphics,
            bool applyGraphicsState,
            bool saveHdcState = false)
            : this((IHdcContext)new IGraphics(graphics), applyGraphicsState ? ApplyGraphicsProperties.All : ApplyGraphicsProperties.None, saveHdcState)
        {
        }

        public static implicit operator HDC(in DeviceContextHdcScope scope) => scope.HDC;
        public static implicit operator nint(in DeviceContextHdcScope scope) => scope.HDC;
        public static explicit operator WPARAM(in DeviceContextHdcScope scope) => (WPARAM)scope.HDC;

        public void Dispose()
        {
            _fillScope?.Dispose();
            if (DeviceContext is not null && DeviceContext is not IGraphicsHdcProvider)
            {
                DeviceContext.ReleaseHdc();
            }
#if DEBUG
            DisposalTracking.SuppressFinalize(this!);
#endif
        }

        private readonly struct DeviceContextAdapter : IHdcContext
        {
            private readonly IDeviceContext? _dc;
            public DeviceContextAdapter(IDeviceContext? dc) => _dc = dc;
            public HDC GetHdc() => _dc is not null ? (HDC)_dc.GetHdc() : default;
            public void ReleaseHdc() => _dc?.ReleaseHdc();
        }
    }
}
