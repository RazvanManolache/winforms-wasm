using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Microsoft.JSInterop;
using WfWasm.Renderer;

namespace WfWasm.CanvasHost;

/// <summary>
/// Serializes one frame of paint commands for the Canvas2D bridge
/// (wwwroot/canvas-bridge.js): one JSON array per frame, one interop call.
/// Ops: 1 = fillRect [x,y,w,h,css], 2 = roundRect [x,y,w,h,r,css],
/// 3 = fillText [text,x,y,fontCss,css,hAlign,vAlign,mnemonic],
/// 4 = fillGradientV [x,y,w,h,topCss,bottomCss],
/// 5 = strokeLine [x1,y1,x2,y2,width,css],
/// 6 = ellipse [x,y,w,h,lineWidth,outlineCss,fillCss|null].
/// 7 = drawImage [x,y,dw,dh,imgW,imgH,base64Rgba] (raw row-major RGBA).
/// 8 = defBitmap [layerId,imgW,imgH] (pixels travel out-of-band; see
/// PaintAsync: defs are uploaded first, then stripped from the JSON).
/// 9 = blitBitmap [layerId,x,y,dw,dh].
/// 10 = retainLayers [[liveIds]] (compositor drops unlisted layers).
/// 11 = patchBitmap [layerId,x,y,w,h] (pixels out-of-band, spliced into
/// the retained layer; hand-built streams without a prior upload no-op).
/// Frame/end markers carry no pixels.
/// </summary>
public static partial class CanvasBridge
{
    [JSImport("globalThis.wfwasmTexDef")]
    internal static partial string TexDef(string layerId, byte[] rgba, int width, int height);

    [JSImport("globalThis.wfwasmTexPatch")]
    internal static partial string TexPatch(string layerId, byte[] rgba, int x, int y, int width, int height);

    public sealed record BitmapUpload(string LayerId, byte[] Rgba, int Width, int Height, int X = 0, int Y = 0, bool IsPatch = false);

    public static string SerializeCommands(IReadOnlyList<DrawCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        var ops = new List<object?[]>(commands.Count);
        foreach (DrawCommand command in commands)
        {
            switch (command)
            {
                case FillRectCommand r:
                    ops.Add([1, r.X, r.Y, r.Width, r.Height, Css(r.Rgba)]);
                    break;
                case FillRoundedRectCommand r:
                    ops.Add([2, r.X, r.Y, r.Width, r.Height, r.Radius, Css(r.Rgba)]);
                    break;
                case DrawTextCommand t:
                    ops.Add([3, t.Text, t.X, t.Y, t.FontCss, Css(t.Rgba),
                        (int)t.HAlign, (int)t.VAlign, t.MnemonicIndex]);
                    break;
                case FillGradientCommand g:
                    ops.Add([4, g.X, g.Y, g.Width, g.Height, Css(g.TopRgba), Css(g.BottomRgba)]);
                    break;
                case DrawLineCommand l:
                    ops.Add([5, l.X1, l.Y1, l.X2, l.Y2, l.Width, Css(l.Rgba)]);
                    break;
                case DrawEllipseCommand e:
                    ops.Add([6, e.X, e.Y, e.Width, e.Height, e.LineWidth, Css(e.OutlineRgba),
                        e.FillRgba.HasValue ? Css(e.FillRgba.Value) : null]);
                    break;
                case DrawImageCommand i:
                    ops.Add([7, i.X, i.Y, i.DW, i.DH, i.Width, i.Height,
                        Convert.ToBase64String(i.Rgba)]);
                    break;
                case DefBitmapCommand d:
                    ops.Add([8, d.LayerId, d.Width, d.Height]);
                    break;
                case BlitBitmapCommand b:
                    ops.Add([9, b.LayerId, b.X, b.Y, b.DW, b.DH]);
                    break;
                case RetainLayersCommand r:
                    ops.Add([10, r.LiveIds]);
                    break;
                case PatchBitmapCommand p:
                    ops.Add([11, p.LayerId, p.X, p.Y, p.Width, p.Height]);
                    break;
            }
        }

        return JsonSerializer.Serialize(ops);
    }

    // Splits binary uploads out of a frame: defs and patches become
    // ordered uploads and are stripped from the JSON stream (the upload
    // call itself defines/splices the layer). Pure: unit-testable
    // without a JS runtime.
    internal static (List<DrawCommand> Json, List<BitmapUpload> Uploads) SplitUploads(
        IReadOnlyList<DrawCommand> commands)
    {
        var json = new List<DrawCommand>(commands.Count);
        var uploads = new List<BitmapUpload>();
        foreach (DrawCommand command in commands)
        {
            if (command is DefBitmapCommand d)
                uploads.Add(new BitmapUpload(d.LayerId, d.Rgba, d.Width, d.Height));
            else if (command is PatchBitmapCommand p)
                uploads.Add(new BitmapUpload(p.LayerId, p.Rgba, p.Width, p.Height, p.X, p.Y, true));
            else
                json.Add(command);
        }

        return (json, uploads);
    }


    public static string Css(uint rgba) => $"#{rgba:x8}";

    public static ValueTask<string> PaintAsync(IJSRuntime js, string canvasId, IReadOnlyList<DrawCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(js);
        var (json, uploads) = SplitUploads(commands);
        foreach (BitmapUpload u in uploads)
        {
            if (u.IsPatch)
                TexPatch(u.LayerId, u.Rgba, u.X, u.Y, u.Width, u.Height);
            else
                TexDef(u.LayerId, u.Rgba, u.Width, u.Height);
        }
        return js.InvokeAsync<string>("wfwasm.paint", canvasId, SerializeCommands(json));
    }
}
