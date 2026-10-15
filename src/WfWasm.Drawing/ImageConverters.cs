// Image converters: exact .NET member shapes; codec-backed conversions
// throw until the codec milestone, string/format conversions work now.
using System.Collections;
using System.ComponentModel;
using System.Globalization;

namespace System.Drawing
{
    /// <summary>Image conversion (cf. System.Drawing.ImageConverter).</summary>
    public class ImageConverter : TypeConverter
    {
        public ImageConverter()
        {
        }

        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(byte[]) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            destinationType == typeof(string) || destinationType == typeof(byte[]) || base.CanConvertTo(context, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is byte[] bytes)
                return Image.FromStream(new MemoryStream(bytes));
            return base.ConvertFrom(context, culture, value);
        }

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is Image image)
                return image.ToString();
            return base.ConvertTo(context, culture, value, destinationType);
        }

        public override PropertyDescriptorCollection? GetProperties(ITypeDescriptorContext? context, object value, Attribute[]? attributes) =>
            new([
                new ImageProperty("Width", typeof(int), c => ((Image)c).Width),
                new ImageProperty("Height", typeof(int), c => ((Image)c).Height),
                new ImageProperty("PixelFormat", typeof(Imaging.PixelFormat), c => ((Image)c).PixelFormat),
                new ImageProperty("RawFormat", typeof(Imaging.ImageFormat), c => ((Image)c).RawFormat),
                new ImageProperty("HorizontalResolution", typeof(float), c => ((Image)c).HorizontalResolution),
                new ImageProperty("VerticalResolution", typeof(float), c => ((Image)c).VerticalResolution),
            ]);

        public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;

        private sealed class ImageProperty(string name, Type type, Func<object, object?> getter) : PropertyDescriptor(name, null)
        {
            public override Type ComponentType => typeof(Image);
            public override bool IsReadOnly => true;
            public override Type PropertyType => type;
            public override bool CanResetValue(object component) => false;
            public override object? GetValue(object? component) => component is null ? null : getter(component);
            public override void ResetValue(object component)
            {
            }
            public override void SetValue(object? component, object? value) =>
                throw new NotSupportedException("Image properties are read-only.");
            public override bool ShouldSerializeValue(object component) => false;
        }
    }

    /// <summary>Image-format conversion (cf. System.Drawing.ImageFormatConverter).</summary>
    public class ImageFormatConverter : TypeConverter
    {
        private static readonly Imaging.ImageFormat[] Formats =
        [
            Imaging.ImageFormat.Bmp, Imaging.ImageFormat.Emf, Imaging.ImageFormat.Exif,
            Imaging.ImageFormat.Gif, Imaging.ImageFormat.Icon, Imaging.ImageFormat.Jpeg,
            Imaging.ImageFormat.MemoryBmp, Imaging.ImageFormat.Png, Imaging.ImageFormat.Tiff,
            Imaging.ImageFormat.Wmf, Imaging.ImageFormat.Heif, Imaging.ImageFormat.Webp,
        ];

        public ImageFormatConverter()
        {
        }

        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string text)
            {
                foreach (Imaging.ImageFormat format in Formats)
                {
                    if (string.Equals(format.ToString(), text, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(FormatName(format), text, StringComparison.OrdinalIgnoreCase))
                        return format;
                }
            }
            return base.ConvertFrom(context, culture, value);
        }

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is Imaging.ImageFormat format)
                return FormatName(format);
            return base.ConvertTo(context, culture, value, destinationType);
        }

        public override StandardValuesCollection? GetStandardValues(ITypeDescriptorContext? context) =>
            new(Formats);

        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

        internal static string FormatName(Imaging.ImageFormat format)
        {
            if (format.Equals(Imaging.ImageFormat.MemoryBmp)) return "MemoryBmp";
            if (format.Equals(Imaging.ImageFormat.Bmp)) return "Bmp";
            if (format.Equals(Imaging.ImageFormat.Emf)) return "Emf";
            if (format.Equals(Imaging.ImageFormat.Wmf)) return "Wmf";
            if (format.Equals(Imaging.ImageFormat.Gif)) return "Gif";
            if (format.Equals(Imaging.ImageFormat.Jpeg)) return "Jpeg";
            if (format.Equals(Imaging.ImageFormat.Png)) return "Png";
            if (format.Equals(Imaging.ImageFormat.Tiff)) return "Tiff";
            if (format.Equals(Imaging.ImageFormat.Exif)) return "Exif";
            if (format.Equals(Imaging.ImageFormat.Icon)) return "Icon";
            if (format.Equals(Imaging.ImageFormat.Heif)) return "Heif";
            if (format.Equals(Imaging.ImageFormat.Webp)) return "Webp";
            return format.ToString();
        }
    }
}
