// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Windows.Win32.Foundation;

namespace Windows.Win32.UI.Input.Ime;

internal readonly unsafe partial struct HIMC : IHandle<HIMC>, IEquatable<HIMC>
{
    public readonly nint Value;
    public HIMC(nint value) => Value = value;
    public HIMC Handle => this;
    public static explicit operator HIMC(nint value) => new(value);
    public static implicit operator nint(HIMC value) => value.Value;
    public static explicit operator HIMC(void* value) => new((nint)value);
    public static implicit operator void*(HIMC value) => (void*)value.Value;
    public static readonly HIMC Null = default;
    public bool IsNull => Value == 0;
    public bool Equals(HIMC other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is HIMC other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(HIMC left, HIMC right) => left.Value == right.Value;
    public static bool operator !=(HIMC left, HIMC right) => !left.Equals(right);
}

[Flags]
internal enum IME_CONVERSION_MODE : uint
{
    IME_CMODE_ALPHANUMERIC = 0x0000,
    IME_CMODE_NATIVE = 0x0001,
    IME_CMODE_CHINESE = IME_CMODE_NATIVE,
    IME_CMODE_HANGUL = IME_CMODE_NATIVE,
    IME_CMODE_JAPANESE = IME_CMODE_NATIVE,
    IME_CMODE_KATAKANA = 0x0002,
    IME_CMODE_LANGUAGE = 0x0003,
    IME_CMODE_FULLSHAPE = 0x0008,
    IME_CMODE_ROMAN = 0x0010,
    IME_CMODE_CHARCODE = 0x0020,
    IME_CMODE_HANJACONVERT = 0x0040,
    IME_CMODE_SOFTKBD = 0x0080,
    IME_CMODE_NOCONVERSION = 0x0100,
    IME_CMODE_EUDC = 0x0200,
    IME_CMODE_SYMBOL = 0x0400,
    IME_CMODE_FIXED = 0x0800,
}

internal enum NOTIFY_IME_INDEX : uint
{
    CPS_COMPLETE = 1,
    CPS_CONVERT = 2,
    CPS_REVERT = 3,
    CPS_CANCEL = 4,
}

[Flags]
public enum IME_SENTENCE_MODE : uint
{
    IME_SMODE_NONE = 0x0000,
    IME_SMODE_PLAURALCLAUSE = 0x0001,
    IME_SMODE_SINGLECONVERT = 0x0002,
    IME_SMODE_AUTOMATIC = 0x0004,
    IME_SMODE_PHRASEPREDICT = 0x0008,
    IME_SMODE_CONVERSATION = 0x0010,
}

[Flags]
public enum IME_COMPOSITION_STRING : uint
{
    GCS_COMPREADSTR = 0x0001,
    GCS_COMPREADATTR = 0x0002,
    GCS_COMPREADCLAUSE = 0x0004,
    GCS_COMPSTR = 0x0008,
    GCS_COMPATTR = 0x0010,
    GCS_COMPCLAUSE = 0x0020,
    GCS_CURSORPOS = 0x0080,
    GCS_DELTASTART = 0x0100,
    GCS_RESULTREADSTR = 0x0200,
    GCS_RESULTREADCLAUSE = 0x0400,
    GCS_RESULTSTR = 0x0800,
    GCS_RESULTCLAUSE = 0x1000,
}

public enum NOTIFY_IME_ACTION : uint
{
    NI_OPENCANDIDATE = 0x0010,
    NI_CLOSECANDIDATE = 0x0011,
    NI_SELECTCANDIDATESTR = 0x0012,
    NI_CHANGECANDIDATELISTRANGE = 0x0013,
    NI_FINALIZECONVERSIONRESULT = 0x0014,
    NI_COMPOSITIONSTR = 0x0015,
}

