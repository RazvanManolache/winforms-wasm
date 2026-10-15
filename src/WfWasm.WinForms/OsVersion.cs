// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

namespace System.Private.Windows;

internal static class OsVersion
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SupportedOSPlatformGuard("windows10.0.10240")]
    public static bool IsWindows10_1507OrGreater() => true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SupportedOSPlatformGuard("windows10.0.14393")]
    public static bool IsWindows10_1607OrGreater() => true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SupportedOSPlatformGuard("windows10.0.15063")]
    public static bool IsWindows10_1703OrGreater() => true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SupportedOSPlatformGuard("windows10.0.16299")]
    public static bool IsWindows10_1709OrGreater() => true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SupportedOSPlatformGuard("windows10.0.17134")]
    public static bool IsWindows10_18030rGreater() => true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SupportedOSPlatformGuard("windows11")]
    public static bool IsWindows11_OrGreater() => true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SupportedOSPlatformGuard("windows11.0.22621")]
    public static bool IsWindows11_22H2OrGreater() => true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SupportedOSPlatformGuard("windows8.1")]
    public static bool IsWindows8_1OrGreater() => true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SupportedOSPlatformGuard("windows8")]
    public static bool IsWindows8OrGreater() => true;
}
