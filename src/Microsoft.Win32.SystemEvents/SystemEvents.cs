// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel;

namespace Microsoft.Win32;

public enum UserPreferenceCategory
{
    Accessibility = 1,
    Color = 2,
    Desktop = 3,
    General = 4,
    Icon = 5,
    Keyboard = 6,
    Menu = 7,
    Mouse = 8,
    Policy = 9,
    Power = 10,
    Screensaver = 11,
    Window = 12,
    Locale = 13,
    VisualStyle = 14
}

public class UserPreferenceChangedEventArgs : EventArgs
{
    public UserPreferenceCategory Category { get; }
    public UserPreferenceChangedEventArgs(UserPreferenceCategory category) => Category = category;
}

public delegate void UserPreferenceChangedEventHandler(object sender, UserPreferenceChangedEventArgs e);

public class UserPreferenceChangingEventArgs : EventArgs
{
    public UserPreferenceCategory Category { get; }
    public UserPreferenceChangingEventArgs(UserPreferenceCategory category) => Category = category;
}

public delegate void UserPreferenceChangingEventHandler(object sender, UserPreferenceChangingEventArgs e);

public enum PowerModes
{
    Resume = 1,
    StatusChange = 2,
    Suspend = 3
}

public class PowerModeChangedEventArgs : EventArgs
{
    public PowerModes Mode { get; }
    public PowerModeChangedEventArgs(PowerModes mode) => Mode = mode;
}

public delegate void PowerModeChangedEventHandler(object sender, PowerModeChangedEventArgs e);

public enum SessionEndReasons
{
    Logoff = 1,
    SystemShutdown = 2
}

public class SessionEndingEventArgs : EventArgs
{
    public bool Cancel { get; set; }
    public SessionEndReasons Reason { get; }
    public SessionEndingEventArgs(SessionEndReasons reason) => Reason = reason;
}

public delegate void SessionEndingEventHandler(object sender, SessionEndingEventArgs e);

public class SessionEndedEventArgs : EventArgs
{
    public SessionEndReasons Reason { get; }
    public SessionEndedEventArgs(SessionEndReasons reason) => Reason = reason;
}

public delegate void SessionEndedEventHandler(object sender, SessionEndedEventArgs e);

public enum SessionSwitchReason
{
    ConsoleConnect = 1,
    ConsoleDisconnect = 2,
    RemoteConnect = 3,
    RemoteDisconnect = 4,
    SessionLock = 7,
    SessionUnlock = 8,
    SessionLogon = 5,
    SessionLogoff = 6,
    SessionRemoteControl = 9
}

public class SessionSwitchEventArgs : EventArgs
{
    public SessionSwitchReason Reason { get; }
    public SessionSwitchEventArgs(SessionSwitchReason reason) => Reason = reason;
}

public delegate void SessionSwitchEventHandler(object sender, SessionSwitchEventArgs e);

public class TimerElapsedEventArgs : EventArgs
{
    public IntPtr TimerId { get; }
    public TimerElapsedEventArgs(IntPtr timerId) => TimerId = timerId;
}

public delegate void TimerElapsedEventHandler(object sender, TimerElapsedEventArgs e);

public static class SystemEvents
{
    public static event UserPreferenceChangedEventHandler? UserPreferenceChanged { add { } remove { } }
    public static event UserPreferenceChangingEventHandler? UserPreferenceChanging { add { } remove { } }
    public static event EventHandler? DisplaySettingsChanging { add { } remove { } }
    public static event EventHandler? DisplaySettingsChanged { add { } remove { } }
    public static event EventHandler? InstalledFontsChanged { add { } remove { } }
    public static event SessionSwitchEventHandler? SessionSwitch { add { } remove { } }
    public static event SessionEndingEventHandler? SessionEnding { add { } remove { } }
    public static event EventHandler? SessionEnded { add { } remove { } }
    public static event PowerModeChangedEventHandler? PowerModeChanged { add { } remove { } }
    public static event EventHandler? LowMemory { add { } remove { } }
    public static event EventHandler? PaletteChanged { add { } remove { } }
    public static event EventHandler? TimeChanged { add { } remove { } }
    public static event TimerElapsedEventHandler? TimerElapsed { add { } remove { } }
    public static event EventHandler? EventsThreadShutdown { add { } remove { } }

    public static IntPtr CreateTimer(int interval) => IntPtr.Zero;
    public static void KillTimer(IntPtr timerId) { }
    public static void InvokeOnEventsThread(Delegate method) => method.DynamicInvoke();
}
