// Codec descriptors: exact .NET member shapes; no encoders ship yet, so
// lookup returns empty and save paths throw until the codec milestone.
namespace System.Drawing.Imaging
{
    /// <summary>Encoder identity (cf. System.Drawing.Imaging.Encoder).</summary>
    public sealed class Encoder
    {
        public Encoder(Guid guid)
        {
            Guid = guid;
        }

        public Guid Guid { get; }

        public static readonly Encoder Compression = new(new Guid("e09d739d-ccd4-44ee-8eba-3fbf8be4fc58"));
        public static readonly Encoder ColorDepth = new(new Guid("66087055-ad66-4c7c-9a18-38a2310b8337"));
        public static readonly Encoder ScanMethod = new(new Guid("3a4e2661-3109-4e56-8536-42c156e7dcfa"));
        public static readonly Encoder Version = new(new Guid("24d18c76-814a-41a4-bf53-1c219cccf797"));
        public static readonly Encoder RenderMethod = new(new Guid("6d42c53a-229a-4825-8bb7-5c99e2b9a8b8"));
        public static readonly Encoder Quality = new(new Guid("1d5be4b5-fa4a-452d-9cdd-5db35105e7eb"));
        public static readonly Encoder Transformation = new(new Guid("8d0eb2d1-a58e-4ea8-aa14-108074b7b6f9"));
        public static readonly Encoder LuminanceTable = new(new Guid("edb33bce-0266-4a77-b904-27216099e717"));
        public static readonly Encoder ChrominanceTable = new(new Guid("f2e455dc-09b3-4316-8260-676ada32481c"));
        public static readonly Encoder SaveFlag = new(new Guid("292266fc-ac40-47bf-8cfc-a85b89a655de"));
        public static readonly Encoder ColorSpace = new(new Guid("ae7a62a0-ee2c-49d8-9d07-1ba8a927596e"));
        public static readonly Encoder ImageItems = new(new Guid("63875e13-1f1d-45ab-9195-a29b6066a650"));
        public static readonly Encoder SaveAsCmyk = new(new Guid("a219bbc9-0a9d-4005-a3ee-3a421b8bb06c"));

        public override bool Equals(object? obj) => obj is Encoder other && Guid.Equals(other.Guid);
        public override int GetHashCode() => Guid.GetHashCode();
        public override string ToString() => Guid.ToString();
    }

    /// <summary>Encoder argument (cf. System.Drawing.Imaging.EncoderParameter).</summary>
    public sealed class EncoderParameter : IDisposable
    {
        public EncoderParameter(Encoder encoder, byte value)
            : this(encoder, 1, EncoderParameterValueType.ValueTypeByte, new byte[] { value })
        {
        }

        public EncoderParameter(Encoder encoder, byte[] value)
            : this(encoder, value?.Length ?? 0, EncoderParameterValueType.ValueTypeByte, value)
        {
        }

        public EncoderParameter(Encoder encoder, byte value, bool undefined)
            : this(encoder, 1, undefined ? EncoderParameterValueType.ValueTypeUndefined : EncoderParameterValueType.ValueTypeByte, new byte[] { value })
        {
        }

        public EncoderParameter(Encoder encoder, byte[] value, bool undefined)
            : this(encoder, value?.Length ?? 0, undefined ? EncoderParameterValueType.ValueTypeUndefined : EncoderParameterValueType.ValueTypeByte, value)
        {
        }

        public EncoderParameter(Encoder encoder, int numerator, int denominator)
            : this(encoder, 1, EncoderParameterValueType.ValueTypeRational, new long[] { numerator, denominator })
        {
        }

        public EncoderParameter(Encoder encoder, int numerator1, int denominator1, int numerator2, int denominator2)
            : this(encoder, 2, EncoderParameterValueType.ValueTypeRationalRange, new long[] { numerator1, denominator1, numerator2, denominator2 })
        {
        }

        public EncoderParameter(Encoder encoder, int[] numerator, int[] denominator)
            : this(encoder, numerator?.Length ?? 0, EncoderParameterValueType.ValueTypeRational, (numerator, denominator))
        {
        }

        public EncoderParameter(Encoder encoder, int[] numerator1, int[] denominator1, int[] numerator2, int[] denominator2)
            : this(encoder, numerator1?.Length ?? 0, EncoderParameterValueType.ValueTypeRationalRange, (numerator1, denominator1, numerator2, denominator2))
        {
        }

        public EncoderParameter(Encoder encoder, long rangebegin, long rangeend)
            : this(encoder, 2, EncoderParameterValueType.ValueTypeLongRange, new long[] { rangebegin, rangeend })
        {
        }

        public EncoderParameter(Encoder encoder, long[] rangebegin, long[] rangeend)
            : this(encoder, rangebegin?.Length ?? 0, EncoderParameterValueType.ValueTypeLongRange, (rangebegin, rangeend))
        {
        }

        public EncoderParameter(Encoder encoder, int numberOfValues, int type, int value)
            : this(encoder, numberOfValues, (EncoderParameterValueType)type, new long[] { value })
        {
        }

        public EncoderParameter(Encoder encoder, short value)
            : this(encoder, 1, EncoderParameterValueType.ValueTypeShort, new short[] { value })
        {
        }

        public EncoderParameter(Encoder encoder, short[] value)
            : this(encoder, value?.Length ?? 0, EncoderParameterValueType.ValueTypeShort, value)
        {
        }

        public EncoderParameter(Encoder encoder, long value)
            : this(encoder, 1, EncoderParameterValueType.ValueTypeLong, new long[] { value })
        {
        }

        public EncoderParameter(Encoder encoder, long[] value)
            : this(encoder, value?.Length ?? 0, EncoderParameterValueType.ValueTypeLong, value)
        {
        }

        public EncoderParameter(Encoder encoder, int value)
            : this(encoder, 1, EncoderParameterValueType.ValueTypeLong, new long[] { value })
        {
        }

        public EncoderParameter(Encoder encoder, int[] value)
            : this(encoder, value?.Length ?? 0, EncoderParameterValueType.ValueTypeLong, value)
        {
        }

        public EncoderParameter(Encoder encoder, int numberOfValues, EncoderParameterValueType type, IntPtr value)
        {
            ArgumentNullException.ThrowIfNull(encoder);
            Encoder = encoder;
            NumberOfValues = numberOfValues;
            Type = type;
        }

        public EncoderParameter(Encoder encoder, string value)
        {
            ArgumentNullException.ThrowIfNull(encoder);
            Encoder = encoder;
            NumberOfValues = (value?.Length ?? 0) + 1;
            Type = EncoderParameterValueType.ValueTypeAscii;
        }

        private EncoderParameter(Encoder encoder, int numberOfValues, EncoderParameterValueType type, object? values)
        {
            Encoder = encoder;
            NumberOfValues = numberOfValues;
            Type = type;
        }

        public Encoder Encoder { get; set; }
        public int NumberOfValues { get; }
        public EncoderParameterValueType Type { get; }
        public EncoderParameterValueType ValueType => Type;
        public IntPtr Value => IntPtr.Zero;

        ~EncoderParameter()
        {
            Dispose(false);
        }

        private void Dispose(bool disposing)
        {
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>Encoder argument list (cf. System.Drawing.Imaging.EncoderParameters).</summary>
    public sealed class EncoderParameters : IDisposable
    {
        public EncoderParameters()
            : this(0)
        {
        }

        public EncoderParameters(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            Param = new EncoderParameter[count];
        }

        public EncoderParameter[] Param { get; set; }

        public void Dispose()
        {
        }
    }

    /// <summary>Codec descriptor (cf. System.Drawing.Imaging.ImageCodecInfo).</summary>
    public sealed class ImageCodecInfo
    {
        public Guid Clsid { get; set; }
        public Guid FormatID { get; set; }
        public string? CodecName { get; set; }
        public string? DllName { get; set; }
        public string? FormatDescription { get; set; }
        public string? FilenameExtension { get; set; }
        public string? MimeType { get; set; }
        public ImageCodecFlags Flags { get; set; }
        public int Version { get; set; }
        public byte[][]? SignaturePatterns { get; set; }
        public byte[][]? SignatureMasks { get; set; }

        public static ImageCodecInfo[] GetImageDecoders() => [];

        public static ImageCodecInfo[] GetImageEncoders() => [];
    }
}
