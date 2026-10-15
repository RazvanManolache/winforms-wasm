// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Windows.Win32.UI.HiDpi
{
    internal readonly partial struct DPI_AWARENESS_CONTEXT : IEquatable<DPI_AWARENESS_CONTEXT>
    {
        public readonly nint Value;
        public DPI_AWARENESS_CONTEXT(nint value) => Value = value;
        public static readonly DPI_AWARENESS_CONTEXT Null = default;
        public bool IsNull => Value == 0;
        public static readonly DPI_AWARENESS_CONTEXT DPI_AWARENESS_CONTEXT_UNAWARE = new(-1);
        public static readonly DPI_AWARENESS_CONTEXT DPI_AWARENESS_CONTEXT_SYSTEM_AWARE = new(-2);
        public static readonly DPI_AWARENESS_CONTEXT DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE = new(-3);
        public static readonly DPI_AWARENESS_CONTEXT DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);
        public static readonly DPI_AWARENESS_CONTEXT DPI_AWARENESS_CONTEXT_UNAWARE_GDISCALED = new(-5);

        public bool Equals(DPI_AWARENESS_CONTEXT other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is DPI_AWARENESS_CONTEXT other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(DPI_AWARENESS_CONTEXT left, DPI_AWARENESS_CONTEXT right) => left.Value == right.Value;
        public static bool operator !=(DPI_AWARENESS_CONTEXT left, DPI_AWARENESS_CONTEXT right) => left.Value != right.Value;
        public static explicit operator DPI_AWARENESS_CONTEXT(nint value) => new(value);
        public static implicit operator nint(DPI_AWARENESS_CONTEXT value) => value.Value;
    }
}
