using System.Numerics;

namespace WfWasm.Rendering;

public sealed record LayerVisual(string Id, SurfaceSnapshot Surface, Matrix3x2 Transform,
    RenderRect? Clip, float Opacity, int ZIndex);

public sealed class SceneCommit
{
    public DeviceIdentity Device { get; }
    public long Revision { get; }
    public IReadOnlyList<LayerVisual> Layers { get; }
    internal SceneCommit(DeviceIdentity device, long revision, IEnumerable<LayerVisual> layers)
    {
        Device = device; Revision = revision; Layers = Array.AsReadOnly(layers.ToArray());
    }
}

/// <summary>Retained metadata only. Does not draw, allocate GPU resources or fake readiness.</summary>
public sealed class RetainedScene
{
    private readonly Dictionary<string, (LayerVisual Visual, long Order)> _layers = new(StringComparer.Ordinal);
    private long _nextOrder, _revision;
    public DeviceIdentity Device { get; private set; }
    public long Revision => _revision;
    public RetainedScene(DeviceIdentity device)
    {
        ResourceGuards.SameDevice(device, device); Device = device;
    }

    public void SetLayer(LayerVisual layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentException.ThrowIfNullOrWhiteSpace(layer.Id);
        ArgumentNullException.ThrowIfNull(layer.Surface);
        ResourceGuards.SameDevice(Device, layer.Surface.Handle.Device);
        if (layer.Surface.Handle.Id <= 0 || layer.Surface.Handle.Generation <= 0 || layer.Surface.ContentVersion < 0 ||
            layer.Surface.Authority == PixelAuthority.Lost) throw new InvalidOperationException("Invalid/lost surface.");
        ValidatePlacement(layer.Transform, layer.Clip, layer.Opacity);
        if (_layers.TryGetValue(layer.Id, out var old) && old.Visual == layer) return;
        _layers[layer.Id] = (layer, _layers.TryGetValue(layer.Id, out old) ? old.Order : _nextOrder++);
        _revision++;
    }

    public void SetPlacement(string id, Matrix3x2 transform, RenderRect? clip, float opacity, int zIndex)
    {
        if (!_layers.TryGetValue(id, out var old)) throw new KeyNotFoundException(id);
        // Preserve the surface and content version. No repaint/upload is implied.
        SetLayer(old.Visual with { Transform = transform, Clip = clip, Opacity = opacity, ZIndex = zIndex });
    }

    public bool Remove(string id)
    {
        if (!_layers.Remove(id)) return false;
        _revision++; return true;
    }
    public IReadOnlyList<string> InvalidateSurface(SurfaceHandle handle)
    {
        var removed = _layers.Where(item => item.Value.Visual.Surface.Handle == handle).Select(item => item.Key).ToArray();
        foreach (var id in removed) Remove(id);
        return Array.AsReadOnly(removed);
    }
    public void Clear() { if (_layers.Count == 0) return; _layers.Clear(); _revision++; }
    public SceneCommit Capture() => new(Device, _revision,
        _layers.Values.OrderBy(item => item.Visual.ZIndex).ThenBy(item => item.Order).Select(item => item.Visual));

    public IReadOnlyList<string> ResetDevice(DeviceIdentity replacement)
    {
        if (replacement.SessionId != Device.SessionId || replacement.Generation <= Device.Generation)
            throw new InvalidOperationException("Recovery must advance the same session's device generation.");
        var invalidated = Array.AsReadOnly(_layers.Keys.ToArray());
        Clear(); Device = replacement; _revision++;
        return invalidated;
    }

    private static void ValidatePlacement(Matrix3x2 transform, RenderRect? clip, float opacity)
    {
        ResourceGuards.Finite(transform); clip?.Validate();
        if (!float.IsFinite(opacity) || opacity < 0 || opacity > 1) throw new ArgumentOutOfRangeException(nameof(opacity));
    }
}
