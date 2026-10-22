using System.Numerics;
using WfWasm.Rendering;

namespace WfWasm.AppIntegration;

/// <summary>
/// An explicitly audited control adapter. The future WinForms adapter supplies
/// Graphics.ForDisplay and invokes the real control's paint lifecycle once.
/// This interface does not claim that arbitrary controls fit the GLES subset.
/// </summary>
public interface IControlPaintAdapter
{
    string ControlId { get; }
    bool IsConnected { get; }
    DrawingFeatures RequiredFeatures { get; }
    void PaintOnce(DrawingCommandRecorder recorder, SurfaceDescriptor target);
}

public sealed class DisconnectedControlPaintAdapter(string controlId) : IControlPaintAdapter
{
    public string ControlId { get; } = controlId;
    public bool IsConnected => false;
    public DrawingFeatures RequiredFeatures => DrawingFeatures.None;
    public void PaintOnce(DrawingCommandRecorder recorder, SurfaceDescriptor target) =>
        throw new NotSupportedException("No production WinForms display adapter is connected.");
}

public enum ControlLayerState { Dirty, Queued, Faulted, Disposed }

/// <summary>
/// Optional single-owner glue between the EXISTING paint coordinator and
/// retained scene. Not installed in WinFormsAppHost. Requires an already
/// selected device and audited adapter; never selects a fallback after paint.
/// Full-control repaint only; partial damage and device recovery are host work.
/// </summary>
public sealed class ControlLayerBridge : IDisposable
{
    private readonly IRenderDevice _device;
    private readonly RetainedScene _scene;
    private readonly IControlPaintAdapter _adapter;
    private readonly DisplayPaintCoordinator _paint;
    private Matrix3x2 _transform = Matrix3x2.Identity;
    private RenderRect? _clip;
    private float _opacity = 1;
    private int _zIndex;

    public SurfaceHandle Surface { get; }
    public ControlLayerState State { get; private set; } = ControlLayerState.Dirty;
    public long PaintCallbacks { get; private set; }

    public ControlLayerBridge(IRenderDevice device, RetainedScene scene,
        IControlPaintAdapter adapter, SurfaceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(adapter.ControlId);
        if (!adapter.IsConnected)
            throw new NotSupportedException("Control adapter is not connected; no surface or app paint was created.");
        ResourceGuards.SameDevice(device.Identity, scene.Device);
        var required = adapter.RequiredFeatures | DrawingFeatures.RetainedComposition;
        if (device.Kind != BackendKind.CanvasKitWebGl2 || device.State != RenderDeviceState.Ready ||
            (device.Features & required) != required)
            throw new NotSupportedException("Selected device cannot satisfy this audited control's requirements.");
        if (scene.Capture().Layers.Any(layer => layer.Id == adapter.ControlId))
            throw new InvalidOperationException("A retained layer already uses this control identity.");
        _device = device;
        _scene = scene;
        _adapter = adapter;
        _paint = new(device);
        Surface = device.Surfaces.Create(descriptor).Handle;
    }

    public void InvalidateContent()
    {
        RequireUsable();
        State = ControlLayerState.Dirty;
    }

    /// <summary>Presentation pixel coordinates, NOT logical WinForms input coordinates.</summary>
    public void SetPlacement(Matrix3x2 transform, RenderRect? clip, float opacity, int zIndex)
    {
        RequireUsable();
        ResourceGuards.Finite(transform);
        clip?.Validate();
        if (!float.IsFinite(opacity) || opacity < 0 || opacity > 1)
            throw new ArgumentOutOfRangeException(nameof(opacity));
        _transform = transform; _clip = clip; _opacity = opacity; _zIndex = zIndex;
        if (_scene.Capture().Layers.Any(layer => layer.Id == _adapter.ControlId))
            _scene.SetPlacement(_adapter.ControlId, transform, clip, opacity, zIndex);
        // Preserve content version and dirty state. Moving alone never calls paint.
    }

    /// <returns>True means a batch was queued, not presented or visibly observed.</returns>
    public bool PaintIfDirty()
    {
        RequireUsable();
        if (State != ControlLayerState.Dirty) return false;
        try
        {
            var target = _device.Surfaces.Describe(Surface);
            _paint.Paint(Surface, recorder =>
            {
                // This first bridge repaints the entire layer onto a transparent base.
                // A future partial-damage adapter needs a separate explicit contract.
                recorder.Record(new ClearSurface(new RenderColor(0)));
                PaintCallbacks++;
                _adapter.PaintOnce(recorder, target.Descriptor);
            });
            var current = _device.Surfaces.Describe(Surface);
            _scene.SetLayer(new(_adapter.ControlId, current, _transform, _clip, _opacity, _zIndex));
            State = ControlLayerState.Queued;
            return true;
        }
        catch
        {
            State = ControlLayerState.Faulted;
            RemoveOwnLayer();
            throw; // No CPU replay, automatic retry, successful frame or ready claim.
        }
    }

    private void RequireUsable()
    {
        ObjectDisposedException.ThrowIf(State == ControlLayerState.Disposed, this);
        if (State == ControlLayerState.Faulted)
            throw new InvalidOperationException("Faulted paint is not replayed. Host must resolve the failure and explicitly rebuild the binding.");
        ResourceGuards.SameDevice(_device.Identity, _scene.Device);
        var required = _adapter.RequiredFeatures | DrawingFeatures.RetainedComposition;
        if (!_adapter.IsConnected || (_device.Features & required) != required)
            throw new NotSupportedException("Control requirements changed beyond the selected device before painting.");
        if (_device.State != RenderDeviceState.Ready || !_device.Surfaces.IsValid(Surface))
            throw new InvalidOperationException("Surface/device unavailable. Host must recreate the binding; app paint was not invoked.");
    }

    public void Dispose()
    {
        if (State == ControlLayerState.Disposed) return;
        State = ControlLayerState.Disposed;
        RemoveOwnLayer();
        if (_device.State == RenderDeviceState.Ready && _device.Surfaces.IsValid(Surface))
            _device.Surfaces.Release(Surface);
        // Device/scene belong to the app render session, not this one control.
    }

    private void RemoveOwnLayer()
    {
        // A delayed old binding must not remove a replacement with the same
        // logical control ID after resize/recovery/session integration changes.
        if (_scene.Capture().Layers.Any(layer => layer.Id == _adapter.ControlId && layer.Surface.Handle == Surface))
            _scene.Remove(_adapter.ControlId);
    }
}
