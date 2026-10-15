// Drawing type converters: exact .NET member shapes; string conversion and
// standard values work on CPU, codec-backed conversions throw until codecs.
using System.Collections;
using System.ComponentModel;
using System.Globalization;

namespace System.Drawing
{
    /// <summary>Font conversion (cf. System.Drawing.FontConverter).</summary>
    public class FontConverter : TypeConverter
    {
        public FontConverter()
        {
        }

        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string text)
                return FromString(text, culture);
            return base.ConvertFrom(context, culture, value);
        }

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is Font font)
                return ToString(font, culture);
            return base.ConvertTo(context, culture, value, destinationType);
        }

        public override object? CreateInstance(ITypeDescriptorContext? context, IDictionary propertyValues)
        {
            ArgumentNullException.ThrowIfNull(propertyValues);
            string name = propertyValues["Name"] as string ?? "Microsoft Sans Serif";
            float size = propertyValues["Size"] is float f ? f : 8.25f;
            GraphicsUnit unit = propertyValues["Unit"] is GraphicsUnit u ? u : GraphicsUnit.Point;
            return new Font(name, size, unit);
        }

        public override bool GetCreateInstanceSupported(ITypeDescriptorContext? context) => true;

        public override PropertyDescriptorCollection? GetProperties(ITypeDescriptorContext? context, object value, Attribute[]? attributes) =>
            new([
                new FontProperty("Bold", typeof(bool), c => ((Font)c).Bold),
                new FontProperty("GdiCharSet", typeof(byte), c => ((Font)c).GdiCharSet),
                new FontProperty("GdiVerticalFont", typeof(bool), c => ((Font)c).GdiVerticalFont),
                new FontProperty("Italic", typeof(bool), c => ((Font)c).Italic),
                new FontProperty("Name", typeof(string), c => ((Font)c).Name),
                new FontProperty("Size", typeof(float), c => ((Font)c).Size),
                new FontProperty("Strikeout", typeof(bool), c => ((Font)c).Strikeout),
                new FontProperty("Underline", typeof(bool), c => ((Font)c).Underline),
                new FontProperty("Unit", typeof(GraphicsUnit), c => ((Font)c).Unit),
            ]);

        public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;

        internal static Font FromString(string text, CultureInfo? culture)
        {
            string[] parts = text.Split(',');
            if (parts.Length == 0)
                throw new ArgumentException($"Cannot convert '{text}' to a Font.", nameof(text));
            string name = parts[0].Trim();
            float size = 8.25f;
            GraphicsUnit unit = GraphicsUnit.Point;
            if (parts.Length > 1)
            {
                string sizePart = parts[1].Trim().ToLowerInvariant();
                string[] suffixes = ["pt", "px", "in", "mm", "doc", "world"];
                GraphicsUnit[] units = [GraphicsUnit.Point, GraphicsUnit.Pixel, GraphicsUnit.Inch, GraphicsUnit.Millimeter, GraphicsUnit.Document, GraphicsUnit.World];
                for (int i = 0; i < suffixes.Length; i++)
                {
                    if (sizePart.EndsWith(suffixes[i], StringComparison.Ordinal))
                    {
                        unit = units[i];
                        sizePart = sizePart[..^suffixes[i].Length];
                        break;
                    }
                }
                size = float.Parse(sizePart.Trim(), culture ?? CultureInfo.CurrentCulture);
            }
            return new Font(name, size, unit);
        }

        internal static string ToString(Font font, CultureInfo? culture)
        {
            string suffix = font.Unit switch
            {
                GraphicsUnit.Point => "pt",
                GraphicsUnit.Pixel => "px",
                GraphicsUnit.Inch => "in",
                GraphicsUnit.Millimeter => "mm",
                GraphicsUnit.Document => "doc",
                _ => "world",
            };
            return string.Format(culture ?? CultureInfo.CurrentCulture, "{0}, {1}{2}", font.Name, font.Size, suffix);
        }

        private sealed class FontProperty(string name, Type type, Func<object, object?> getter) : PropertyDescriptor(name, null)
        {
            public override Type ComponentType => typeof(Font);
            public override bool IsReadOnly => true;
            public override Type PropertyType => type;
            public override bool CanResetValue(object component) => false;
            public override object? GetValue(object? component) => component is null ? null : getter(component);
            public override void ResetValue(object component)
            {
            }
            public override void SetValue(object? component, object? value) =>
                throw new NotSupportedException("Font properties are read-only.");
            public override bool ShouldSerializeValue(object component) => false;
        }

        /// <summary>Font-name list (cf. System.Drawing.FontConverter.FontNameConverter).</summary>
        public sealed class FontNameConverter : TypeConverter, IDisposable
        {
            public FontNameConverter()
            {
            }

            public void Dispose()
            {
            }

            public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
                sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

            public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
            {
                if (value is string text)
                    return text;
                return base.ConvertFrom(context, culture, value);
            }

            public override StandardValuesCollection? GetStandardValues(ITypeDescriptorContext? context)
            {
                var names = new List<string>();
                foreach (FontFamily family in FontFamily.Families)
                    names.Add(family.Name);
                return new StandardValuesCollection(names);
            }

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;

            public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
        }

        /// <summary>Font-unit list (cf. System.Drawing.FontConverter.FontUnitConverter).</summary>
        public class FontUnitConverter : EnumConverter
        {
            public FontUnitConverter()
                : base(typeof(GraphicsUnit))
            {
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) =>
                base.GetStandardValues(context)!;
        }
    }

    /// <summary>Icon conversion (cf. System.Drawing.IconConverter).</summary>
    public class IconConverter : ExpandableObjectConverter
    {
        public IconConverter()
        {
        }

        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string) || sourceType == typeof(byte[]) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            destinationType == typeof(string) || destinationType == typeof(byte[]) || base.CanConvertTo(context, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string file)
                return new Icon(file);
            if (value is byte[] bytes)
                return new Icon(new MemoryStream(bytes));
            return base.ConvertFrom(context, culture, value);
        }

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is Icon icon)
                return icon.ToString();
            return base.ConvertTo(context, culture, value, destinationType);
        }
    }

    /// <summary>Point conversion (cf. System.Drawing.PointConverter).</summary>
    public class PointConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string) || sourceType.FullName == "System.Drawing.Point" || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string text)
            {
                text = text.Trim();
                if (text.Length == 0) return null;
                var parts = text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    int x = int.Parse(parts[0].Trim(), culture ?? CultureInfo.InvariantCulture);
                    int y = int.Parse(parts[1].Trim(), culture ?? CultureInfo.InvariantCulture);
                    return new Point(x, y);
                }
                throw new ArgumentException($"Cannot convert '{text}' to a Point.", nameof(value));
            }
            if (value != null && value.GetType().FullName == "System.Drawing.Point")
            {
                var px = value.GetType().GetProperty("X")?.GetValue(value);
                var py = value.GetType().GetProperty("Y")?.GetValue(value);
                if (px is int x && py is int y)
                    return new Point(x, y);
            }
            return base.ConvertFrom(context, culture, value);
        }

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is Point p)
                return string.Format(culture ?? CultureInfo.InvariantCulture, "{0}, {1}", p.X, p.Y);
            return base.ConvertTo(context, culture, value, destinationType);
        }

        public override object? CreateInstance(ITypeDescriptorContext? context, IDictionary propertyValues)
        {
            ArgumentNullException.ThrowIfNull(propertyValues);
            int x = Convert.ToInt32(propertyValues["X"]);
            int y = Convert.ToInt32(propertyValues["Y"]);
            return new Point(x, y);
        }

        public override bool GetCreateInstanceSupported(ITypeDescriptorContext? context) => true;

        public override PropertyDescriptorCollection? GetProperties(ITypeDescriptorContext? context, object value, Attribute[]? attributes) =>
            TypeDescriptor.GetProperties(typeof(Point), attributes);

        public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;
    }

    /// <summary>PointF conversion (cf. System.Drawing.PointFConverter).</summary>
    public class PointFConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string) || sourceType.FullName == "System.Drawing.PointF" || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string text)
            {
                text = text.Trim();
                if (text.Length == 0) return null;
                var parts = text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    float x = float.Parse(parts[0].Trim(), culture ?? CultureInfo.InvariantCulture);
                    float y = float.Parse(parts[1].Trim(), culture ?? CultureInfo.InvariantCulture);
                    return new PointF(x, y);
                }
                throw new ArgumentException($"Cannot convert '{text}' to a PointF.", nameof(value));
            }
            if (value != null && value.GetType().FullName == "System.Drawing.PointF")
            {
                var px = value.GetType().GetProperty("X")?.GetValue(value);
                var py = value.GetType().GetProperty("Y")?.GetValue(value);
                if (px != null && py != null)
                    return new PointF(Convert.ToSingle(px), Convert.ToSingle(py));
            }
            return base.ConvertFrom(context, culture, value);
        }

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is PointF p)
                return string.Format(culture ?? CultureInfo.InvariantCulture, "{0}, {1}", p.X, p.Y);
            return base.ConvertTo(context, culture, value, destinationType);
        }

        public override object? CreateInstance(ITypeDescriptorContext? context, IDictionary propertyValues)
        {
            ArgumentNullException.ThrowIfNull(propertyValues);
            float x = Convert.ToSingle(propertyValues["X"]);
            float y = Convert.ToSingle(propertyValues["Y"]);
            return new PointF(x, y);
        }

        public override bool GetCreateInstanceSupported(ITypeDescriptorContext? context) => true;

        public override PropertyDescriptorCollection? GetProperties(ITypeDescriptorContext? context, object value, Attribute[]? attributes) =>
            TypeDescriptor.GetProperties(typeof(PointF), attributes);

        public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;
    }

    /// <summary>Size conversion (cf. System.Drawing.SizeConverter).</summary>
    public class SizeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string) || sourceType.FullName == "System.Drawing.Size" || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string text)
            {
                text = text.Trim();
                if (text.Length == 0) return null;
                var parts = text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    int w = int.Parse(parts[0].Trim(), culture ?? CultureInfo.InvariantCulture);
                    int h = int.Parse(parts[1].Trim(), culture ?? CultureInfo.InvariantCulture);
                    return new Size(w, h);
                }
                throw new ArgumentException($"Cannot convert '{text}' to a Size.", nameof(value));
            }
            if (value != null && value.GetType().FullName == "System.Drawing.Size")
            {
                var pw = value.GetType().GetProperty("Width")?.GetValue(value);
                var ph = value.GetType().GetProperty("Height")?.GetValue(value);
                if (pw is int w && ph is int h)
                    return new Size(w, h);
            }
            return base.ConvertFrom(context, culture, value);
        }

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is Size s)
                return string.Format(culture ?? CultureInfo.InvariantCulture, "{0}, {1}", s.Width, s.Height);
            return base.ConvertTo(context, culture, value, destinationType);
        }

        public override object? CreateInstance(ITypeDescriptorContext? context, IDictionary propertyValues)
        {
            ArgumentNullException.ThrowIfNull(propertyValues);
            int w = Convert.ToInt32(propertyValues["Width"]);
            int h = Convert.ToInt32(propertyValues["Height"]);
            return new Size(w, h);
        }

        public override bool GetCreateInstanceSupported(ITypeDescriptorContext? context) => true;

        public override PropertyDescriptorCollection? GetProperties(ITypeDescriptorContext? context, object value, Attribute[]? attributes) =>
            TypeDescriptor.GetProperties(typeof(Size), attributes);

        public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;
    }

    /// <summary>SizeF conversion (cf. System.Drawing.SizeFConverter).</summary>
    public class SizeFConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string) || sourceType.FullName == "System.Drawing.SizeF" || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string text)
            {
                text = text.Trim();
                if (text.Length == 0) return null;
                var parts = text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    float w = float.Parse(parts[0].Trim(), culture ?? CultureInfo.InvariantCulture);
                    float h = float.Parse(parts[1].Trim(), culture ?? CultureInfo.InvariantCulture);
                    return new SizeF(w, h);
                }
                throw new ArgumentException($"Cannot convert '{text}' to a SizeF.", nameof(value));
            }
            if (value != null && value.GetType().FullName == "System.Drawing.SizeF")
            {
                var pw = value.GetType().GetProperty("Width")?.GetValue(value);
                var ph = value.GetType().GetProperty("Height")?.GetValue(value);
                if (pw != null && ph != null)
                    return new SizeF(Convert.ToSingle(pw), Convert.ToSingle(ph));
            }
            return base.ConvertFrom(context, culture, value);
        }

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is SizeF s)
                return string.Format(culture ?? CultureInfo.InvariantCulture, "{0}, {1}", s.Width, s.Height);
            return base.ConvertTo(context, culture, value, destinationType);
        }

        public override object? CreateInstance(ITypeDescriptorContext? context, IDictionary propertyValues)
        {
            ArgumentNullException.ThrowIfNull(propertyValues);
            float w = Convert.ToSingle(propertyValues["Width"]);
            float h = Convert.ToSingle(propertyValues["Height"]);
            return new SizeF(w, h);
        }

        public override bool GetCreateInstanceSupported(ITypeDescriptorContext? context) => true;

        public override PropertyDescriptorCollection? GetProperties(ITypeDescriptorContext? context, object value, Attribute[]? attributes) =>
            TypeDescriptor.GetProperties(typeof(SizeF), attributes);

        public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;
    }

    /// <summary>Rectangle conversion (cf. System.Drawing.RectangleConverter).</summary>
    public class RectangleConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string) || sourceType.FullName == "System.Drawing.Rectangle" || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string text)
            {
                text = text.Trim();
                if (text.Length == 0) return null;
                var parts = text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 4)
                {
                    int x = int.Parse(parts[0].Trim(), culture ?? CultureInfo.InvariantCulture);
                    int y = int.Parse(parts[1].Trim(), culture ?? CultureInfo.InvariantCulture);
                    int w = int.Parse(parts[2].Trim(), culture ?? CultureInfo.InvariantCulture);
                    int h = int.Parse(parts[3].Trim(), culture ?? CultureInfo.InvariantCulture);
                    return new Rectangle(x, y, w, h);
                }
                throw new ArgumentException($"Cannot convert '{text}' to a Rectangle.", nameof(value));
            }
            if (value != null && value.GetType().FullName == "System.Drawing.Rectangle")
            {
                var px = value.GetType().GetProperty("X")?.GetValue(value);
                var py = value.GetType().GetProperty("Y")?.GetValue(value);
                var pw = value.GetType().GetProperty("Width")?.GetValue(value);
                var ph = value.GetType().GetProperty("Height")?.GetValue(value);
                if (px is int x && py is int y && pw is int w && ph is int h)
                    return new Rectangle(x, y, w, h);
            }
            return base.ConvertFrom(context, culture, value);
        }

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is Rectangle r)
                return string.Format(culture ?? CultureInfo.InvariantCulture, "{0}, {1}, {2}, {3}", r.X, r.Y, r.Width, r.Height);
            return base.ConvertTo(context, culture, value, destinationType);
        }

        public override object? CreateInstance(ITypeDescriptorContext? context, IDictionary propertyValues)
        {
            ArgumentNullException.ThrowIfNull(propertyValues);
            int x = Convert.ToInt32(propertyValues["X"]);
            int y = Convert.ToInt32(propertyValues["Y"]);
            int w = Convert.ToInt32(propertyValues["Width"]);
            int h = Convert.ToInt32(propertyValues["Height"]);
            return new Rectangle(x, y, w, h);
        }

        public override bool GetCreateInstanceSupported(ITypeDescriptorContext? context) => true;

        public override PropertyDescriptorCollection? GetProperties(ITypeDescriptorContext? context, object value, Attribute[]? attributes) =>
            TypeDescriptor.GetProperties(typeof(Rectangle), attributes);

        public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;
    }

    /// <summary>RectangleF conversion (cf. System.Drawing.RectangleFConverter).</summary>
    public class RectangleFConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string) || sourceType.FullName == "System.Drawing.RectangleF" || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string text)
            {
                text = text.Trim();
                if (text.Length == 0) return null;
                var parts = text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 4)
                {
                    float x = float.Parse(parts[0].Trim(), culture ?? CultureInfo.InvariantCulture);
                    float y = float.Parse(parts[1].Trim(), culture ?? CultureInfo.InvariantCulture);
                    float w = float.Parse(parts[2].Trim(), culture ?? CultureInfo.InvariantCulture);
                    float h = float.Parse(parts[3].Trim(), culture ?? CultureInfo.InvariantCulture);
                    return new RectangleF(x, y, w, h);
                }
                throw new ArgumentException($"Cannot convert '{text}' to a RectangleF.", nameof(value));
            }
            if (value != null && value.GetType().FullName == "System.Drawing.RectangleF")
            {
                var px = value.GetType().GetProperty("X")?.GetValue(value);
                var py = value.GetType().GetProperty("Y")?.GetValue(value);
                var pw = value.GetType().GetProperty("Width")?.GetValue(value);
                var ph = value.GetType().GetProperty("Height")?.GetValue(value);
                if (px != null && py != null && pw != null && ph != null)
                    return new RectangleF(Convert.ToSingle(px), Convert.ToSingle(py), Convert.ToSingle(pw), Convert.ToSingle(ph));
            }
            return base.ConvertFrom(context, culture, value);
        }

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is RectangleF r)
                return string.Format(culture ?? CultureInfo.InvariantCulture, "{0}, {1}, {2}, {3}", r.X, r.Y, r.Width, r.Height);
            return base.ConvertTo(context, culture, value, destinationType);
        }

        public override object? CreateInstance(ITypeDescriptorContext? context, IDictionary propertyValues)
        {
            ArgumentNullException.ThrowIfNull(propertyValues);
            float x = Convert.ToSingle(propertyValues["X"]);
            float y = Convert.ToSingle(propertyValues["Y"]);
            float w = Convert.ToSingle(propertyValues["Width"]);
            float h = Convert.ToSingle(propertyValues["Height"]);
            return new RectangleF(x, y, w, h);
        }

        public override bool GetCreateInstanceSupported(ITypeDescriptorContext? context) => true;

        public override PropertyDescriptorCollection? GetProperties(ITypeDescriptorContext? context, object value, Attribute[]? attributes) =>
            TypeDescriptor.GetProperties(typeof(RectangleF), attributes);

        public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;
    }

    /// <summary>Color conversion (cf. System.Drawing.ColorConverter).</summary>
    public class ColorConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string) || sourceType.FullName == "System.Drawing.Color" || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string text)
            {
                text = text.Trim();
                if (text.Length == 0) return Color.Empty;
                if (text.StartsWith('#'))
                {
                    return ColorTranslator.FromHtml(text);
                }
                if (text.Contains(',') || text.Contains(';'))
                {
                    var parts = text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 3)
                    {
                        int r = int.Parse(parts[0].Trim(), culture ?? CultureInfo.InvariantCulture);
                        int g = int.Parse(parts[1].Trim(), culture ?? CultureInfo.InvariantCulture);
                        int b = int.Parse(parts[2].Trim(), culture ?? CultureInfo.InvariantCulture);
                        return Color.FromArgb(r, g, b);
                    }
                    if (parts.Length == 4)
                    {
                        int a = int.Parse(parts[0].Trim(), culture ?? CultureInfo.InvariantCulture);
                        int r = int.Parse(parts[1].Trim(), culture ?? CultureInfo.InvariantCulture);
                        int g = int.Parse(parts[2].Trim(), culture ?? CultureInfo.InvariantCulture);
                        int b = int.Parse(parts[3].Trim(), culture ?? CultureInfo.InvariantCulture);
                        return Color.FromArgb(a, r, g, b);
                    }
                }
                return Color.FromName(text);
            }
            if (value != null && value.GetType().FullName == "System.Drawing.Color")
            {
                var pa = value.GetType().GetProperty("A")?.GetValue(value);
                var pr = value.GetType().GetProperty("R")?.GetValue(value);
                var pg = value.GetType().GetProperty("G")?.GetValue(value);
                var pb = value.GetType().GetProperty("B")?.GetValue(value);
                if (pa != null && pr != null && pg != null && pb != null)
                    return Color.FromArgb(Convert.ToByte(pa), Convert.ToByte(pr), Convert.ToByte(pg), Convert.ToByte(pb));
            }
            return base.ConvertFrom(context, culture, value);
        }

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is Color c)
            {
                if (c.IsEmpty) return string.Empty;
                if (c.IsNamedColor) return c.Name;
                return string.Format(culture ?? CultureInfo.InvariantCulture, "{0}, {1}, {2}", c.R, c.G, c.B);
            }
            return base.ConvertTo(context, culture, value, destinationType);
        }
    }
}
