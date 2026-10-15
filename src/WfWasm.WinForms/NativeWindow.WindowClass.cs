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
}
