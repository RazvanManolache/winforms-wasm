// Buffered graphics: exact .NET member shapes; buffers are CPU bitmaps.
namespace System.Drawing
{
    /// <summary>Off-screen buffer (cf. System.Drawing.BufferedGraphics).</summary>
    public sealed class BufferedGraphics : IDisposable
    {
        private readonly Bitmap _buffer;
        private readonly Graphics _graphics;
        private readonly Graphics? _target;
        private readonly Rectangle _location;
        private bool _disposed;

        internal BufferedGraphics(Rectangle targetRectangle, Graphics? target)
        {
            _location = targetRectangle;
            _target = target;
            _buffer = new Bitmap(Math.Max(1, targetRectangle.Width), Math.Max(1, targetRectangle.Height));
            _graphics = Graphics.FromImage(_buffer);
        }

        public Graphics Graphics
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _graphics;
            }
        }

        public void Render()
        {
            if (_target is not null)
                Render(_target);
        }

        public void Render(Graphics target)
        {
            ArgumentNullException.ThrowIfNull(target);
            ObjectDisposedException.ThrowIf(_disposed, this);
            target.DrawImageUnscaled(_buffer, _location.X, _location.Y);
        }

        public void Render(IntPtr hdc)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _graphics.Dispose();
            }
        }
    }

    /// <summary>Buffer allocator (cf. System.Drawing.BufferedGraphicsContext).</summary>
    public sealed class BufferedGraphicsContext : IDisposable
    {
        private bool _disposed;

        public BufferedGraphicsContext()
        {
        }

        public Size MaximumBuffer { get; set; }

        public BufferedGraphics Allocate(Graphics target, Rectangle targetRectangle)
        {
            ArgumentNullException.ThrowIfNull(target);
            ObjectDisposedException.ThrowIf(_disposed, this);
            return new BufferedGraphics(targetRectangle, target);
        }

        public BufferedGraphics Allocate(IntPtr targetHdc, Rectangle targetRectangle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return new BufferedGraphics(targetRectangle, null);
        }

        public void Invalidate()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        ~BufferedGraphicsContext()
        {
            Dispose(false);
        }

        private void Dispose(bool disposing)
        {
            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>Shared buffer context (cf. System.Drawing.BufferedGraphicsManager).</summary>
    public static class BufferedGraphicsManager
    {
        public static BufferedGraphicsContext Current { get; } = new();
    }
}
