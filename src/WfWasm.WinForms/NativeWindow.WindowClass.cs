// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace System.Windows.Forms;

public partial class NativeWindow
{
    /// <summary>
    ///  WindowClass encapsulates a Windows window class for Wasm.
    /// </summary>
    private class WindowClass
    {
        internal static WindowClass? s_cache;

        internal WindowClass? _next;
        internal string? _className;
        internal string? _windowClassName;
        internal NativeWindow? _targetWindow;

        private readonly WNDCLASS_STYLES _classStyle;
        private IntPtr _defaultWindProc;

        private WNDPROC? _windProc;

        private static readonly string s_currentAppDomainHash = Convert.ToString(AppDomain.CurrentDomain.GetHashCode(), 16);

        private static readonly Lock s_wcInternalSyncObject = new();

        internal WindowClass(string? className, WNDCLASS_STYLES classStyle)
        {
            _className = className;
            _classStyle = classStyle;
            RegisterClass();
        }

        public LRESULT Callback(HWND hwnd, uint msg, WPARAM wparam, LPARAM lparam)
        {
            Debug.Assert(!hwnd.IsNull, "Windows called us with an HWND of 0");

            PInvokeCore.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_WNDPROC, _defaultWindProc);

            Debug.Assert(_targetWindow is not null);

            _targetWindow.AssignHandle(hwnd);
            return _targetWindow.Callback(hwnd, msg, wparam, lparam);
        }

        internal static WindowClass FindOrCreate(string? className, WNDCLASS_STYLES classStyle)
        {
            lock (s_wcInternalSyncObject)
            {
                WindowClass? wc = s_cache;
                if (className is null)
                {
                    while (wc is not null
                        && (wc._className is not null || wc._classStyle != classStyle))
                    {
                        wc = wc._next;
                    }
                }
                else
                {
                    while (wc is not null && !className.Equals(wc._className))
                    {
                        wc = wc._next;
                    }
                }

                if (wc is null)
                {
                    wc = new WindowClass(className, classStyle)
                    {
                        _next = s_cache
                    };

                    s_cache = wc;
                }

                return wc;
            }
        }

        private static string GetFullClassName(string className)
        {
            const string versionSuffix = "_r3_ad1";
            return $"{Application.WindowsFormsVersion}.{className}.app.0.{s_currentAppDomainHash}{versionSuffix}";
        }

        private void RegisterClass()
        {
            string? localClassName = _className;
            if (_className is null)
            {
                _defaultWindProc = DefaultWindowProc;
                localClassName = $"Window.{(int)_classStyle:x}";
            }
            else
            {
                localClassName = _className;
                _defaultWindProc = DefaultWindowProc;
            }

            _windowClassName = GetFullClassName(localClassName);
            _windProc = Callback;
        }
    }

    /// <summary>
    ///  Fork: CreateWindowEx is minted locally, so the OS never calls the
    ///  window-class proc. Dispatch the creation messages (WM_NCCREATE,
    ///  then WM_CREATE) to the pending target window, mirroring native
    ///  CreateWindowEx, which delivers both before returning the handle.
    ///  This is what assigns the handle and raises OnHandleCreated.
    /// </summary>

    internal static unsafe void DispatchCreateWindow(HWND hwnd, char* lpClassName)
    {
        if ((nuint)lpClassName <= 0xFFFF)
        {
            return; // Class atom: not one of our registered classes.
        }

        string className = new string(lpClassName);
        WindowClass? wc = WindowClass.s_cache;
        while (wc is not null && wc._windowClassName != className && wc._className != className)
        {
            wc = wc._next;
        }

        if (wc?._targetWindow is not { } target)
        {
            return;
        }

        // The class proc assigns the handle on the first message and switches
        // the window proc; later messages reach the target's own Callback.
        wc.Callback(hwnd, PInvokeCore.WM_NCCREATE, default, default);
        target.Callback(hwnd, PInvokeCore.WM_CREATE, default, default);
        // Visible parentless controls own top-level windows: track popups here
        // (some never call ShowWindow; see WasmPopupHost.NoteHandleCreated).
        WasmPopupHost.NoteHandleCreated((IntPtr)hwnd.Value);
    }
}
