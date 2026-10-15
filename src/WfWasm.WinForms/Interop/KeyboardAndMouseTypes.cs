// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using Windows.Win32.Foundation;

namespace Windows.Win32.UI.Input.KeyboardAndMouse
{
    internal enum VIRTUAL_KEY : ushort
    {
        VK_LBUTTON = 0x01,
        VK_RBUTTON = 0x02,
        VK_CANCEL = 0x03,
        VK_MBUTTON = 0x04,
        VK_BACK = 0x08,
        VK_TAB = 0x09,
        VK_CLEAR = 0x0C,
        VK_RETURN = 0x0D,
        VK_SHIFT = 0x10,
        VK_CONTROL = 0x11,
        VK_MENU = 0x12,
        VK_PAUSE = 0x13,
        VK_CAPITAL = 0x14,
        VK_KANA = 0x15,
        VK_ESCAPE = 0x1B,
        VK_CONVERT = 0x1C,
        VK_NONCONVERT = 0x1D,
        VK_ACCEPT = 0x1E,
        VK_MODECHANGE = 0x1F,
        VK_SPACE = 0x20,
        VK_PRIOR = 0x21,
        VK_NEXT = 0x22,
        VK_END = 0x23,
        VK_HOME = 0x24,
        VK_LEFT = 0x25,
        VK_UP = 0x26,
        VK_RIGHT = 0x27,
        VK_DOWN = 0x28,
        VK_SELECT = 0x29,
        VK_PRINT = 0x2A,
        VK_EXECUTE = 0x2B,
        VK_SNAPSHOT = 0x2C,
        VK_INSERT = 0x2D,
        VK_DELETE = 0x2E,
        VK_HELP = 0x2F,
        VK_0 = 0x30,
        VK_1 = 0x31,
        VK_2 = 0x32,
        VK_3 = 0x33,
        VK_4 = 0x34,
        VK_5 = 0x35,
        VK_6 = 0x36,
        VK_7 = 0x37,
        VK_8 = 0x38,
        VK_9 = 0x39,
        VK_A = 0x41,
        VK_B = 0x42,
        VK_C = 0x43,
        VK_D = 0x44,
        VK_E = 0x45,
        VK_F = 0x46,
        VK_G = 0x47,
        VK_H = 0x48,
        VK_I = 0x49,
        VK_J = 0x4A,
        VK_K = 0x4B,
        VK_L = 0x4C,
        VK_M = 0x4D,
        VK_N = 0x4E,
        VK_O = 0x4F,
        VK_P = 0x50,
        VK_Q = 0x51,
        VK_R = 0x52,
        VK_S = 0x53,
        VK_T = 0x54,
        VK_U = 0x55,
        VK_V = 0x56,
        VK_W = 0x57,
        VK_X = 0x58,
        VK_Y = 0x59,
        VK_Z = 0x5A,
        VK_LWIN = 0x5B,
        VK_RWIN = 0x5C,
        VK_APPS = 0x5D,
        VK_SLEEP = 0x5F,
        VK_NUMPAD0 = 0x60,
        VK_NUMPAD1 = 0x61,
        VK_NUMPAD2 = 0x62,
        VK_NUMPAD3 = 0x63,
        VK_NUMPAD4 = 0x64,
        VK_NUMPAD5 = 0x65,
        VK_NUMPAD6 = 0x66,
        VK_NUMPAD7 = 0x67,
        VK_NUMPAD8 = 0x68,
        VK_NUMPAD9 = 0x69,
        VK_MULTIPLY = 0x6A,
        VK_ADD = 0x6B,
        VK_SEPARATOR = 0x6C,
        VK_SUBTRACT = 0x6D,
        VK_DECIMAL = 0x6E,
        VK_DIVIDE = 0x6F,
        VK_F1 = 0x70,
        VK_F2 = 0x71,
        VK_F3 = 0x72,
        VK_F4 = 0x73,
        VK_F5 = 0x74,
        VK_F6 = 0x75,
        VK_F7 = 0x76,
        VK_F8 = 0x77,
        VK_F9 = 0x78,
        VK_F10 = 0x79,
        VK_F11 = 0x7A,
        VK_F12 = 0x7B,
        VK_F13 = 0x7C,
        VK_F14 = 0x7D,
        VK_F15 = 0x7E,
        VK_F16 = 0x7F,
        VK_F17 = 0x80,
        VK_F18 = 0x81,
        VK_F19 = 0x82,
        VK_F20 = 0x83,
        VK_F21 = 0x84,
        VK_F22 = 0x85,
        VK_F23 = 0x86,
        VK_F24 = 0x87,
        VK_NUMLOCK = 0x90,
        VK_SCROLL = 0x91,
        VK_LSHIFT = 0xA0,
        VK_RSHIFT = 0xA1,
        VK_LCONTROL = 0xA2,
        VK_RCONTROL = 0xA3,
        VK_LMENU = 0xA4,
        VK_RMENU = 0xA5,
    }

    internal readonly unsafe partial struct HKL : IHandle<HKL>, IEquatable<HKL>
    {
        public readonly nint Value;
        public HKL(nint value) => Value = value;
        public HKL Handle => this;
        public static explicit operator HKL(nint value) => new(value);
        public static implicit operator nint(HKL value) => value.Value;
        public static explicit operator HKL(void* value) => new((nint)value);
        public static implicit operator void*(HKL value) => (void*)value.Value;
        public static readonly HKL Null = default;
        public bool IsNull => Value == 0;
        public bool Equals(HKL other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is HKL other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(HKL left, HKL right) => left.Value == right.Value;
        public static bool operator !=(HKL left, HKL right) => !left.Equals(right);
    }

    internal enum INPUT_TYPE : uint
    {
        INPUT_MOUSE = 0,
        INPUT_KEYBOARD = 1,
        INPUT_HARDWARE = 2,
    }

    [Flags]
    internal enum KEYBD_EVENT_FLAGS : uint
    {
        KEYEVENTF_EXTENDEDKEY = 0x0001,
        KEYEVENTF_KEYUP = 0x0002,
        KEYEVENTF_UNICODE = 0x0004,
        KEYEVENTF_SCANCODE = 0x0008,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public VIRTUAL_KEY wVk;
        public ushort wScan;
        public KEYBD_EVENT_FLAGS dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public Windows.Win32.UI.WindowsAndMessaging.MOUSE_EVENT_FLAGS dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public INPUT_TYPE type;
        public _Anonymous_e__Union Anonymous;

        [StructLayout(LayoutKind.Explicit)]
        public struct _Anonymous_e__Union
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
            [FieldOffset(0)]
            public HARDWAREINPUT hi;
        }
    }
}

namespace Windows.Win32
{
    internal static partial class PInvoke
    {
        public static uint SendInput(ReadOnlySpan<UI.Input.KeyboardAndMouse.INPUT> pInputs, int cbSize) => (uint)pInputs.Length;
        public static BOOL BlockInput(BOOL fBlock) => (BOOL)true;
        // Mouse buttons reflect the host-fed WasmMouseState (high bit set
        // while pressed, matching Win32); keyboard/modifier keys still read
        // 0 — the host does not feed them yet.
        public static short GetKeyState(int nVirtKey)
        {
            var buttons = global::System.Windows.Forms.WasmMouseState.Buttons;
            return nVirtKey switch
            {
                0x01 when (buttons & global::System.Windows.Forms.MouseButtons.Left) != 0 => unchecked((short)0x8000),
                0x02 when (buttons & global::System.Windows.Forms.MouseButtons.Right) != 0 => unchecked((short)0x8000),
                0x04 when (buttons & global::System.Windows.Forms.MouseButtons.Middle) != 0 => unchecked((short)0x8000),
                0x05 when (buttons & global::System.Windows.Forms.MouseButtons.XButton1) != 0 => unchecked((short)0x8000),
                0x06 when (buttons & global::System.Windows.Forms.MouseButtons.XButton2) != 0 => unchecked((short)0x8000),
                _ => 0,
            };
        }
        public static UI.Input.KeyboardAndMouse.HKL GetKeyboardLayout(uint idThread) => UI.Input.KeyboardAndMouse.HKL.Null;
    }
}
