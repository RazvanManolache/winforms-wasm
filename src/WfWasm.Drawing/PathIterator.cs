// Path traversal: exact .NET member shapes over the CPU path model.
// Exhaustion behavior probed from .NET 9 (NextSubpath returns 0/0/0 past
// the end and leaves isClosed set).
namespace System.Drawing.Drawing2D
{
    /// <summary>Subpath/marker traversal (cf. System.Drawing.Drawing2D.GraphicsPathIterator).</summary>
    public sealed class GraphicsPathIterator : IDisposable
    {
        public GraphicsPathIterator(GraphicsPath? path)
        {
            Reset(path);
        }

        public int Count => _points.Length;
        public int SubpathCount => _starts.Count;

        public void Rewind()
        {
            _sub = 0;
            _typeIndex = 0;
            _marker = 0;
        }

        public bool HasCurve()
        {
            foreach (byte type in _types)
            {
                if ((type & (byte)PathPointType.PathTypeMask) == (byte)PathPointType.Bezier)
                    return true;
            }

            return false;
        }

        public int NextSubpath(out int startIndex, out int endIndex, out bool isClosed)
        {
            if (_sub >= _starts.Count)
            {
                startIndex = 0;
                endIndex = 0;
                isClosed = true;
                return 0;
            }

            startIndex = _starts[_sub];
            endIndex = _ends[_sub];
            isClosed = _closed[_sub];
            _sub++;
            return endIndex - startIndex + 1;
        }

        public int NextSubpath(GraphicsPath path, out bool isClosed)
        {
            ArgumentNullException.ThrowIfNull(path);
            int count = NextSubpath(out int start, out int end, out isClosed);
            path.Reset();
            if (count > 0)
            {
                var pts = new PointF[count];
                var types = new byte[count];
                Array.Copy(_points, start, pts, 0, count);
                Array.Copy(_types, start, types, 0, count);
                types[0] = (byte)(((int)types[0] & ~(int)PathPointType.PathTypeMask) | (int)PathPointType.Start);
                path.AppendSubpath(pts, types, isClosed);
            }

            return count;
        }

        public int NextPathType(out byte pathType, out int startIndex, out int endIndex)
        {
            while (_typeIndex < _points.Length)
            {
                byte run = _types[_typeIndex];
                int end = _typeIndex;
                while (end + 1 < _points.Length && _types[end + 1] == run)
                    end++;
                pathType = run;
                startIndex = _typeIndex;
                endIndex = end;
                _typeIndex = end + 1;
                return end - startIndex + 1;
            }

            pathType = 0;
            startIndex = 0;
            endIndex = 0;
            return 0;
        }

        public int NextMarker(out int startIndex, out int endIndex)
        {
            if (_marker >= _sections.Count)
            {
                startIndex = 0;
                endIndex = 0;
                return 0;
            }

            (startIndex, endIndex) = _sections[_marker];
            _marker++;
            return endIndex - startIndex + 1;
        }

        public int NextMarker(GraphicsPath path)
        {
            ArgumentNullException.ThrowIfNull(path);
            int count = NextMarker(out int start, out int end);
            path.Reset();
            if (count > 0)
            {
                var pts = new PointF[count];
                var types = new byte[count];
                Array.Copy(_points, start, pts, 0, count);
                Array.Copy(_types, start, types, 0, count);
                types[0] = (byte)(((int)types[0] & ~(int)PathPointType.PathTypeMask) | (int)PathPointType.Start);
                path.AppendSubpath(pts, types, false);
            }

            return count;
        }

        public int Enumerate(ref PointF[] points, ref byte[] types)
        {
            if (points is null || points.Length < _points.Length)
                points = new PointF[_points.Length];
            if (types is null || types.Length < _types.Length)
                types = new byte[_types.Length];
            Array.Copy(_points, points, _points.Length);
            Array.Copy(_types, types, _types.Length);
            return _points.Length;
        }

        public int Enumerate(Span<PointF> points, Span<byte> types)
        {
            int n = Math.Min(Math.Min(points.Length, types.Length), _points.Length);
            _points.AsSpan(0, n).CopyTo(points);
            _types.AsSpan(0, n).CopyTo(types);
            return n;
        }

        public int CopyData(ref PointF[] points, ref byte[] types, int startIndex, int endIndex)
        {
            RangeCheck(startIndex, endIndex);
            int n = endIndex - startIndex + 1;
            if (points is null || points.Length < n)
                points = new PointF[n];
            if (types is null || types.Length < n)
                types = new byte[n];
            Array.Copy(_points, startIndex, points, 0, n);
            Array.Copy(_types, startIndex, types, 0, n);
            return n;
        }

        public int CopyData(Span<PointF> points, Span<byte> types, int startIndex, int endIndex)
        {
            RangeCheck(startIndex, endIndex);
            int n = Math.Min(Math.Min(points.Length, types.Length), endIndex - startIndex + 1);
            _points.AsSpan(startIndex, n).CopyTo(points);
            _types.AsSpan(startIndex, n).CopyTo(types);
            return n;
        }

        ~GraphicsPathIterator()
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

        private void Reset(GraphicsPath? path)
        {
            PathData data = path?.PathData
                ?? new PathData();
            _points = data.Points;
            _types = data.Types;
            _starts = new List<int>();
            _ends = new List<int>();
            _closed = new List<bool>();
            _sections = new List<(int, int)>();
            for (int i = 0; i < _points.Length; i++)
            {
                if ((_types[i] & (byte)PathPointType.PathTypeMask) == (byte)PathPointType.Start)
                {
                    if (_starts.Count > 0)
                        _ends.Add(i - 1);
                    _starts.Add(i);
                }
            }

            if (_starts.Count > 0)
                _ends.Add(_points.Length - 1);
            for (int s = 0; s < _starts.Count; s++)
            {
                int end = _ends[s];
                _closed.Add((_types[end] & (byte)PathPointType.CloseSubpath) != 0);
            }

            int sectionStart = 0;
            for (int i = 0; i < _points.Length; i++)
            {
                if ((_types[i] & (byte)PathPointType.PathMarker) != 0)
                {
                    _sections.Add((sectionStart, i));
                    sectionStart = i + 1;
                }
            }

            if (sectionStart < _points.Length)
                _sections.Add((sectionStart, _points.Length - 1));
            Rewind();
        }

        private void RangeCheck(int startIndex, int endIndex)
        {
            if (startIndex < 0 || endIndex >= _points.Length || endIndex < startIndex)
                throw new ArgumentException("Invalid start/end range for this path.");
        }

        private PointF[] _points = [];
        private byte[] _types = [];
        private List<int> _starts = new();
        private List<int> _ends = new();
        private List<bool> _closed = new();
        private List<(int Start, int End)> _sections = new();
        private int _sub;
        private int _typeIndex;
        private int _marker;
    }
}
