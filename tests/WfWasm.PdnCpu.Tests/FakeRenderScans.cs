// Same-named stand-ins for the PDN render-scan types (the resume path
// resolves them by full name across loaded assemblies): they pin the
// full-surface Result contract without referencing PDN.
namespace PaintDotNet.Rendering
{
    public struct RectInt32
    {
        public RectInt32(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
    }
}

namespace PaintDotNet.Functional
{
    public sealed class ResultValueImpl<T>
    {
        public ResultValueImpl(T value)
        {
            Value = value;
        }

        public T Value { get; }
    }
}

namespace PaintDotNet.Effects
{
    public interface IEffect
    {
        object get_EffectInfo();
    }
}

namespace PaintDotNet.HistoryMementos
{
    public sealed class BitmapHistoryMemento
    {
        public BitmapHistoryMemento(string name, object? image, object? workspace,
            int layerIndex, object? scans, object? original, object? progressCallback)
        {
            SavedName = name;
            SavedLayerIndex = layerIndex;
            SavedScans = scans;
            SavedOriginal = original;
        }

        public string SavedName { get; }
        public int SavedLayerIndex { get; }
        public object? SavedScans { get; }
        public object? SavedOriginal { get; }
    }
}
