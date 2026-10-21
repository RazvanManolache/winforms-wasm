using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using FormsControl = System.Windows.Forms.Control;
using FormsPaintEventArgs = System.Windows.Forms.PaintEventArgs;

public sealed class DirtyTrackingTests
{
    private sealed class CountingControl : FormsControl
    {
        public int Paints;
        protected override void OnPaint(FormsPaintEventArgs e) => Paints++;
    }

    private sealed class ToggleControl : FormsControl
    {
        public int Paints;
        public bool Blue;
        protected override void OnPaint(FormsPaintEventArgs e)
        {
            Paints++;
            using var brush = new System.Drawing.SolidBrush(
                Blue ? System.Drawing.Color.Blue : System.Drawing.Color.Red);
            e.Graphics.FillRectangle(brush, new System.Drawing.Rectangle(5, 5, 10, 10));
        }
    }

    private sealed class ClipRecControl : FormsControl
    {
        public System.Drawing.Rectangle LastClip = new(-1, -1, 0, 0);
        protected override void OnPaint(FormsPaintEventArgs e) => LastClip = e.ClipRectangle;
    }

    [Fact]
    public void Idle_Frame_Emits_Nothing()
    {
        // A pump tick with no app Invalidate and no host marking must
        // not raster, serialize, or interop anything (the razor skips
        // empty command lists). SelectScene primes one global frame.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);

        var first = new RecordingRenderer();
        host.Paint(first);
        Assert.NotEmpty(first.Commands);

        var idle = new RecordingRenderer();
        host.Paint(idle);
        Assert.Empty(idle.Commands);
    }

    [Fact]
    public void Clean_Control_Skips_Raster_But_Reblits()
    {
        // Second frame with no dirty marks: the self-painted control's
        // OnPaint must NOT re-run, but its retained layer still blits
        // (full replay needs the blit command every frame).
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        var counter = new CountingControl { Left = 4, Top = 4, Width = 40, Height = 40 };
        form.Controls.Add(counter);
        try
        {
            string layer = WinFormsAppHost.LayerIdFor(counter);
            var first = new RecordingRenderer();
            host.Paint(first);
            Assert.Equal(1, counter.Paints);
            Assert.Contains(first.Commands, c => c is DefBitmapCommand d && d.LayerId == layer);

            System.Windows.Forms.WasmRepaintGate.MarkDirty(new FormsControl());
            var second = new RecordingRenderer();
            host.Paint(second);
            Assert.Equal(1, counter.Paints);
            Assert.Contains(second.Commands, c => c is BlitBitmapCommand b && b.LayerId == layer);
            Assert.DoesNotContain(second.Commands, c => c is DefBitmapCommand d && d.LayerId == layer);
        }
        finally
        {
            form.Controls.Remove(counter);
            counter.Dispose();
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }

    [Fact]
    public void Dirty_Control_ReRasters()
    {
        // Marking the control dirty re-runs OnPaint and re-defines the
        // layer on the next frame.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        var counter = new CountingControl { Left = 4, Top = 4, Width = 40, Height = 40 };
        form.Controls.Add(counter);
        try
        {
            string layer = WinFormsAppHost.LayerIdFor(counter);
            host.Paint(new RecordingRenderer());
            Assert.Equal(1, counter.Paints);

            System.Windows.Forms.WasmRepaintGate.MarkDirty(counter);
            var third = new RecordingRenderer();
            host.Paint(third);
            Assert.Equal(2, counter.Paints);
            // Identical pixels hash-skip the upload (BitmapLayerTests
            // owns that contract); the blit proves the layer is live.
            Assert.Contains(third.Commands, c => c is BlitBitmapCommand b && b.LayerId == layer);
        }
        finally
        {
            form.Controls.Remove(counter);
            counter.Dispose();
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }

    [Fact]
    public void Region_Mark_Always_Patches_Without_Identical_Skip()
    {
        // A rect mark runs OnPaint over the retained surface and always
        // patch-uploads the rect: no pre-paint snapshot (a full rect
        // swizzle per frame costs more than uploading small identical
        // regions). Full paints keep hash-skip; regions never Def.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        var counter = new CountingControl { Left = 4, Top = 4, Width = 40, Height = 40 };
        form.Controls.Add(counter);
        try
        {
            string layer = WinFormsAppHost.LayerIdFor(counter);
            host.Paint(new RecordingRenderer());
            Assert.Equal(1, counter.Paints);

            var rc = new System.Drawing.Rectangle(2, 2, 8, 8);
            System.Windows.Forms.WasmRepaintGate.MarkDirtyRect(counter, rc);
            var second = new RecordingRenderer();
            host.Paint(second);
            Assert.Equal(2, counter.Paints);
            var patch = Assert.Single(second.Commands.OfType<PatchBitmapCommand>(), p => p.LayerId == layer);
            Assert.Equal(rc.X, patch.X);
            Assert.Equal(rc.Y, patch.Y);
            Assert.Equal(rc.Width, patch.Width);
            Assert.Equal(rc.Height, patch.Height);
            Assert.DoesNotContain(second.Commands, c => c is DefBitmapCommand d && d.LayerId == layer);
            Assert.Contains(second.Commands, c => c is BlitBitmapCommand b && b.LayerId == layer);
        }
        finally
        {
            form.Controls.Remove(counter);
            counter.Dispose();
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }

    [Fact]
    public void Region_Mark_Changed_Pixels_Patches_Layer()
    {
        // Changed pixels inside the dirty rect upload as a patch (not a
        // full Def); the patch bytes carry the new paint.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        var toggle = new ToggleControl { Left = 4, Top = 4, Width = 40, Height = 40 };
        form.Controls.Add(toggle);
        try
        {
            string layer = WinFormsAppHost.LayerIdFor(toggle);
            host.Paint(new RecordingRenderer());
            Assert.Equal(1, toggle.Paints);

            toggle.Blue = true;
            var rc = new System.Drawing.Rectangle(0, 0, 20, 20);
            System.Windows.Forms.WasmRepaintGate.MarkDirtyRect(toggle, rc);
            var second = new RecordingRenderer();
            host.Paint(second);
            Assert.Equal(2, toggle.Paints);
            var patch = Assert.Single(second.Commands.OfType<PatchBitmapCommand>(), p => p.LayerId == layer);
            Assert.Equal(0, patch.X);
            Assert.Equal(0, patch.Y);
            Assert.Equal(20, patch.Width);
            Assert.Equal(20, patch.Height);
            // Painted pixel (5,5) inside the patch is opaque blue RGBA.
            int o = (5 * 20 + 5) * 4;
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, patch.Rgba[o..(o + 4)]);
            Assert.DoesNotContain(second.Commands, c => c is DefBitmapCommand d && d.LayerId == layer);
            Assert.Contains(second.Commands, c => c is BlitBitmapCommand b && b.LayerId == layer);
        }
        finally
        {
            form.Controls.Remove(toggle);
            toggle.Dispose();
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }

    [Fact]
    public void Region_Mark_Resized_Falls_Back_To_Full()
    {
        // A resized control has no matching retained surface: the rect
        // mark takes the full path (Def, never a patch splice).
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        var toggle = new ToggleControl { Left = 4, Top = 4, Width = 40, Height = 40 };
        form.Controls.Add(toggle);
        try
        {
            string layer = WinFormsAppHost.LayerIdFor(toggle);
            host.Paint(new RecordingRenderer());
            Assert.Equal(1, toggle.Paints);

            toggle.Blue = true;
            toggle.Width = 60;
            System.Windows.Forms.WasmRepaintGate.MarkDirtyRect(toggle, new System.Drawing.Rectangle(0, 0, 20, 20));
            var second = new RecordingRenderer();
            host.Paint(second);
            Assert.Equal(2, toggle.Paints);
            Assert.Contains(second.Commands, c => c is DefBitmapCommand d && d.LayerId == layer);
            Assert.DoesNotContain(second.Commands, c => c is PatchBitmapCommand p && p.LayerId == layer);
        }
        finally
        {
            form.Controls.Remove(toggle);
            toggle.Dispose();
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }

    [Fact]
    public void Region_Repaint_ClipRectangle_Matches_Dirty_Rect()
    {
        // Desktop passes the invalid region as ClipRectangle: full
        // paints see the whole surface, region paints see the rect.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        var rec = new ClipRecControl { Left = 4, Top = 4, Width = 40, Height = 40 };
        form.Controls.Add(rec);
        try
        {
            host.Paint(new RecordingRenderer());
            Assert.Equal(new System.Drawing.Rectangle(0, 0, 40, 40), rec.LastClip);

            var rc = new System.Drawing.Rectangle(7, 9, 11, 13);
            System.Windows.Forms.WasmRepaintGate.MarkDirtyRect(rec, rc);
            host.Paint(new RecordingRenderer());
            Assert.Equal(rc, rec.LastClip);
        }
        finally
        {
            form.Controls.Remove(rec);
            rec.Dispose();
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }

    [Fact]
    public void Resized_Control_ReRasters_On_Next_Running_Frame()
    {
        // Size-change backstop: when a frame runs for another control,
        // a resized-but-unmarked control must still re-raster (the
        // retained pixels are the wrong size).
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        var resized = new CountingControl { Left = 4, Top = 4, Width = 40, Height = 40 };
        var other = new CountingControl { Left = 60, Top = 4, Width = 40, Height = 40 };
        form.Controls.Add(resized);
        form.Controls.Add(other);
        try
        {
            host.Paint(new RecordingRenderer());
            Assert.Equal(1, resized.Paints);
            Assert.Equal(1, other.Paints);

            resized.Width = 60;
            System.Windows.Forms.WasmRepaintGate.MarkDirty(other);
            host.Paint(new RecordingRenderer());
            Assert.Equal(2, resized.Paints);
            Assert.Equal(2, other.Paints);
        }
        finally
        {
            form.Controls.Remove(resized);
            form.Controls.Remove(other);
            resized.Dispose();
            other.Dispose();
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }
}
