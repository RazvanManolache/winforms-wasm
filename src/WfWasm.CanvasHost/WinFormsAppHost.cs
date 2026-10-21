using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using WfWasm.PdnCpu;
using WfWasm.Renderer;
using WfWasm.Shim;

namespace WfWasm.CanvasHost;

public sealed class WinFormsAppHost : IDisposable
{
	private sealed record ElementLayoutMembers(Func<System.Windows.Forms.Control, object?> GetTree, Func<object, bool>? NeedsLayout, Action<object> Pump);

	internal enum PointerEventKind
	{
		Pressed,
		Moved,
		Released
	}

	private static readonly MethodInfo? s_onPaintMethod = typeof(System.Windows.Forms.Control).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static readonly MethodInfo? s_onPaintBackgroundMethod = typeof(System.Windows.Forms.Control).GetMethod("OnPaintBackground", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static readonly ConcurrentDictionary<Type, bool> s_selfPaints = new ConcurrentDictionary<Type, bool>();
	private static readonly ConcurrentDictionary<Type, bool> s_nonFlatPaints = new ConcurrentDictionary<Type, bool>();

	private static readonly ConcurrentDictionary<string, byte> s_selfPaintLogged = new ConcurrentDictionary<string, byte>();
	private static readonly ConcurrentDictionary<string, byte> s_firstPaintLogged = new ConcurrentDictionary<string, byte>();

	// Durable paint signal (replaces per-paint [TEMP] spam, ~1.4k lines/run):
	// first completed paint per control type, plus Self-paint failures.
	private static void LogFirstPaint(System.Windows.Forms.Control c)
	{
		try
		{
			string text = c.GetType().FullName ?? c.GetType().Name;
			if (s_firstPaintLogged.TryAdd(text, 1))
			{
				System.Console.WriteLine("[paint] first-painted " + text);
				// Tile content lives in TPF elements (not Controls): dump
				// the tile tree once its panorama first paints (post-layout).
				if (text.Contains("Panorama", System.StringComparison.Ordinal))
					DumpTileVisualState(c);
				// Positive-control ink census for vendor text paths: grids
				// and diagrams render dense text; their sessions tell
				// whether managed ink APIs run at all (BookShelf reads zero
				// everywhere). Bounded one-liners, first paint only.
				if (text.Contains("RadGridView", System.StringComparison.Ordinal)
					|| text.Contains("RadDiagram", System.StringComparison.Ordinal))
					DumpInkCensus();
			}
			// One-shot late tile dump (ungated): panorama groups arrive
			// async (book load via ThreadPool), so the first-paint tree
			// predates the tiles. Re-dumps on the first panorama paint
			// whose groups are non-empty (covers loading repaints as each
			// arrives). Cheap presence probe per panorama paint.
			if (!s_tileLateDumpDone
				&& text.Contains("Panorama", System.StringComparison.Ordinal)
				&& PanoramaHasGroups(c))
			{
				s_tileLateDumpDone = true;
				DumpTileVisualState(c);
				MaybeEnsureTileRepaint(c);
				SubscribeTilePaintFire(c);
				ArmTileRedumpOnResize(c);
			}
		}
		catch { }
	}
	// Perf item 4: first-chance exception census (caught per-span
	// throws are invisible otherwise). Hooked only when the
	// timeline is enabled; Take drains per frame in Home.
	private static long s_firstChanceCount;
	private static long s_slowTicks;
	private static string? s_slowType;
	private static long s_slowDraws;
	private static long s_slowPx;
	internal static void TakeSlow(out long ticks, out string? type, out long draws, out long px)
	{
		ticks = System.Threading.Interlocked.Exchange(ref s_slowTicks, 0);
		type = System.Threading.Interlocked.Exchange(ref s_slowType, null);
		draws = System.Threading.Interlocked.Exchange(ref s_slowDraws, 0);
		px = System.Threading.Interlocked.Exchange(ref s_slowPx, 0);
	}
	private static int s_firstChanceHooked;

	internal static void HookFirstChance()
	{
		if (System.Threading.Interlocked.Exchange(ref s_firstChanceHooked, 1) != 0)
		    return;
		System.AppDomain.CurrentDomain.FirstChanceException += (_, _) =>
		    System.Threading.Interlocked.Increment(ref s_firstChanceCount);
	}

	internal static long TakeFirstChance() =>
		System.Threading.Interlocked.Exchange(ref s_firstChanceCount, 0);

	private static readonly ConcurrentDictionary<Type, ElementLayoutMembers?> s_layoutMembers = new ConcurrentDictionary<Type, ElementLayoutMembers>();

	private static readonly MethodInfo? s_onClickMethod = typeof(System.Windows.Forms.Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static readonly MethodInfo? s_onMouseDownMethod = typeof(System.Windows.Forms.Control).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static readonly MethodInfo? s_onMouseMoveMethod = typeof(System.Windows.Forms.Control).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static readonly MethodInfo? s_onMouseUpMethod = typeof(System.Windows.Forms.Control).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static readonly MethodInfo? s_onMouseEnterMethod = typeof(System.Windows.Forms.Control).GetMethod("OnMouseEnter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static readonly MethodInfo? s_onMouseLeaveMethod = typeof(System.Windows.Forms.Control).GetMethod("OnMouseLeave", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	private static readonly MethodInfo? s_onMouseClickMethod = typeof(System.Windows.Forms.Control).GetMethod("OnMouseClick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	private static readonly MethodInfo? s_onMouseDoubleClickMethod = typeof(System.Windows.Forms.Control).GetMethod("OnMouseDoubleClick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	private static readonly MethodInfo? s_onDoubleClickMethod = typeof(System.Windows.Forms.Control).GetMethod("OnDoubleClick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	private static readonly MethodInfo? s_onMouseHoverMethod = typeof(System.Windows.Forms.Control).GetMethod("OnMouseHover", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	private static readonly MethodInfo? s_onMouseWheelMethod = typeof(System.Windows.Forms.Control).GetMethod("OnMouseWheel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	private static readonly MethodInfo? s_onKeyDownMethod = typeof(System.Windows.Forms.Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	private static readonly MethodInfo? s_processDialogKeyMethod = typeof(System.Windows.Forms.Control).GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	private static readonly MethodInfo? s_onKeyPressMethod = typeof(System.Windows.Forms.Control).GetMethod("OnKeyPress", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	private static readonly MethodInfo? s_onKeyUpMethod = typeof(System.Windows.Forms.Control).GetMethod("OnKeyUp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static readonly MethodInfo? s_itemMouseEnterMethod = typeof(System.Windows.Forms.ToolStripItem).GetMethod("OnMouseEnter", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly MethodInfo? s_itemMouseMoveMethod = typeof(System.Windows.Forms.ToolStripItem).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly MethodInfo? s_itemMouseLeaveMethod = typeof(System.Windows.Forms.ToolStripItem).GetMethod("OnMouseLeave", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly MethodInfo? s_itemUnselectMethod = typeof(System.Windows.Forms.ToolStripItem).GetMethod("Unselect", BindingFlags.Instance | BindingFlags.NonPublic);

	private readonly Dictionary<AppScene, System.Windows.Forms.Form> _forms = new Dictionary<AppScene, System.Windows.Forms.Form>();

	private readonly HashSet<System.Windows.Forms.Form> _autoCentered = new HashSet<System.Windows.Forms.Form>();

	private readonly HashSet<System.Windows.Forms.Form> _userPlaced = new HashSet<System.Windows.Forms.Form>();

	private System.Windows.Forms.Form? _activeForm;

	private System.Windows.Forms.TextBox? _focusedTextBox;
	private System.Windows.Forms.Control? _focusedControl;

	private System.Windows.Forms.Control? _pressedButton;

	private System.Windows.Forms.Control? _mouseTarget;

	private System.Windows.Forms.MouseButtons _mouseButtons = System.Windows.Forms.MouseButtons.None;
	// Press/release click pairing (desktop: Click fires on release over the
	// pressed control, DoubleClick replaces the second Click inside the
	// SystemInformation window/rect). Tokens compare by reference, never by
	// virtual Equals (app controls may override it).
	private ClickToken? _downToken;
	private int _downX;
	private int _downY;
	private long _downMs;
	private System.Windows.Forms.MouseButtons _downButton;
	private ClickToken? _lastClickToken;
	private int _lastClickX;
	private int _lastClickY;
	private long _lastClickMs;
	private System.Windows.Forms.MouseButtons _lastClickButton;
	// Hover stillness: last move position/time; the timer fires OnMouseHover
	// once the pointer rests inside MouseHoverSize past MouseHoverTime.
	private int _hoverX = int.MinValue;
	private int _hoverY = int.MinValue;
	private long _hoverMs;
	private bool _hoverFired;
	private System.Threading.Timer? _hoverTimer;
	// Wheel fractional-notch accumulator (trackpads send sub-notch deltas).
	private double _wheelRemainder;
	// ListView virtual scroll (desktop parity). The host paints ListView rows
	// itself, so it also owns the scroll offset the native control would keep:
	// one top index per ListView. Paint is windowed from it, scrollbar on overflow,
	// wheel + scrollbar-track clicks move it. Entries die with the control.
	private sealed class ListViewScroll { public int Top; public int Anchor = -2; }
	private readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Forms.Control, ListViewScroll> _listViewScroll = new();
	internal const int ListViewHeaderH = 24;
	internal const int ListViewRowH = 22;
	internal const int ListViewScrollW = 14;
	internal static int ListViewCapacity(int height) => Math.Max(1, (height - ListViewHeaderH - 2) / ListViewRowH);
	internal static int ListViewClampTop(int top, int count, int capacity) => Math.Clamp(top, 0, Math.Max(0, count - capacity));
	internal int ResolveListViewTop(System.Windows.Forms.ListView lv)
	{
		int cap = ListViewCapacity(lv.Height);
		int count;
		try { count = lv.Items.Count; }
		catch { count = 0; }
		ListViewScroll box = _listViewScroll.GetOrCreateValue(lv);
		int top = ListViewClampTop(box.Top, count, cap);
		// Follow the anchor selection into view (native EnsureVisible for
		// keyboard/click selection changes), but only when the anchor MOVED:
		// wheel scrolling must persist instead of snapping back.
		int anchor = -1;
		try
		{
			foreach (int i in lv.SelectedIndices) { anchor = i; break; }
			if (anchor < 0 && lv.FocusedItem != null) anchor = lv.FocusedItem.Index;
		}
		catch { anchor = -1; }
		if (anchor >= 0 && anchor != box.Anchor)
		{
			if (anchor < top) top = anchor;
			if (anchor >= top + cap) top = anchor - cap + 1;
			box.Anchor = anchor;
		}
		box.Top = top;
		return top;
	}
	internal bool ScrollListView(System.Windows.Forms.ListView lv, int lines)
	{
		int cap = ListViewCapacity(lv.Height);
		int count;
		try { count = lv.Items.Count; }
		catch { return false; }
		if (count <= cap || lines == 0)
			return false;
		ListViewScroll box = _listViewScroll.GetOrCreateValue(lv);
		int top = ListViewClampTop(box.Top + lines, count, cap);
		if (top == box.Top)
			return false;
		box.Top = top;
		return true;
	}
	// Test seam: double-click/hover windows read through here so tests
	// drive timing without sleeping.
	internal Func<long> TickNow { get; set; } = static () => Environment.TickCount64;

	private bool _isDraggingTitle;

	private System.Windows.Forms.Form? _dragForm;

	private int _dragStartX;

	private int _dragStartY;

	private int _dragInitialFormX;

	private int _dragInitialFormY;

	internal Rectangle _documentPageBounds;

	private ZoomedBlit? _docZoomBlit;

	private Size _documentOriginalSize;

	private bool _isDrawingOnDocument;

	// Bounded per-session diagnostic for the composited document read
	// (live tool overlays): first 3 attempts log the reflection chain.
	private static int _compositedDiagCount;

	private System.Windows.Forms.Control? _hoverControl;

	private System.Windows.Forms.ToolStripItem? _hoverItem;

	private static readonly Dictionary<string, bool> s_d2dTypeCache = new Dictionary<string, bool>();

	private static readonly Dictionary<string, MethodInfo?> s_d2dRootCache = new Dictionary<string, MethodInfo>();

	private static readonly Dictionary<string, MethodInfo?> s_d2dInitCache = new Dictionary<string, MethodInfo>();

	private static readonly Dictionary<string, MethodInfo?> s_d2dCreatedCache = new Dictionary<string, MethodInfo>();

	private static readonly HashSet<string> s_d2dRenderFailed = new HashSet<string>();

	private static Type? s_cpuCtxType;

	private static Type? s_rectFloatType;

	private static ConstructorInfo? s_rectFloatCtor;

	private static ConstructorInfo? s_cpuCtxCtor;

	private static readonly ConditionalWeakTable<object, string> s_layerIds = new ConditionalWeakTable<object, string>();

	private static int s_layerSeq;

	private static readonly HashSet<string> s_liveLayers = new HashSet<string>();

	private static readonly Dictionary<string, ulong> s_layerHash = new Dictionary<string, ulong>();

	// Per-frame dirty state (drained from WasmRepaintGate by Paint, valid
	// only for the walk; clean bitmap layers re-blit, dirty ones re-raster).
	private static Dictionary<System.Windows.Forms.Control, System.Drawing.Rectangle?>? s_frameDirty;
	private static bool s_frameGlobal;
	private static int s_paintLogCount; // routine [Paint] lines are throttled (first 3 + every 64th).
	private static int s_layoutLogCount; // same throttling for [LayoutQueue] lines.
	private static long s_layoutFaultedSeen; // last queue fault total already logged.
	private static long s_layoutLogTick; // TickCount64 of the last [LayoutQueue] line.

	// Opt-in per-frame diagnostics (AppContext switch, read live so
	// runtime toggling works): routine frames must not pay the
	// reflection walks or console I/O of Dump*/[Paint]/[CHROME].
	internal static bool PaintDiagnosticsEnabled =>
		AppContext.TryGetSwitch("WfWasm.PaintDiagnostics", out bool paintDiagEnabled) && paintDiagEnabled;
	private static readonly Dictionary<string, (int w, int h)> s_layerSize = new Dictionary<string, (int w, int h)>();
	// Retained self-paint surfaces by layer id: region repaints run the
	// clipped OnPaint over these, then patch-upload the dirty rect.
	// Any full RecordBitmap clears the entry (a Def replaces the base
	// the next patch would splice into); EmitRetain prunes dead ones.
	private static readonly Dictionary<string, Bitmap> s_retainedPaint = new Dictionary<string, Bitmap>();

	private static readonly object s_docLayerKey = new object();

	private static bool s_d2dTypesLogged;

	private const string dbgTag = "[D2DDBG2] ";

	private const string fwName = "PaintDotNet.Framework";

	private const string coreName = "PaintDotNet.Windows.Core";

	private const string cpuCtxName = "PaintDotNet.Direct2D1.CpuDrawingContext";

	private const string asmDotGetType = "asm.GetType => ";

	private const string asmThrew = "asm.GetType threw: ";

	private const string fwTag = "fwLoaded=";

	private const string noStr = "null";

	private const string yesStr = "FOUND";

	private readonly Queue<string> _inputTrace = new Queue<string>();

	private int _inputSeq;

	private long _lastDocMoveMs;

	private const int MaxTraceEntries = 300;

	private static bool s_pdnInitialized;

	private readonly HashSet<System.Windows.Forms.Form> _placedWindows = new HashSet<System.Windows.Forms.Form>();

	private bool _modalActive;

	private readonly List<(System.Windows.Forms.ToolStripDropDown Drop, int Ox, int Oy)> _openDropdowns = new List<(System.Windows.Forms.ToolStripDropDown, int, int)>();

	private static readonly MethodInfo? s_itemMouseDownMethod = typeof(System.Windows.Forms.ToolStripItem).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly MethodInfo? s_itemMouseUpMethod = typeof(System.Windows.Forms.ToolStripItem).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic);


	private static bool s_pointerReflectionInitialized;

	private static Type? s_point2DoubleType;

	private static ConstructorInfo? s_point2DoubleCtor;

	private static Type? s_pointerPointType;

	private static ConstructorInfo? s_pointerPointCtor;

	private static Type? s_pointerTypeEnum;

	private static Type? s_pointerButtonsEnum;

	private static Type? s_pointerButtonChangeTypeEnum;

	private static Type? s_pointerPointFlagsEnum;

	private static MethodInfo? s_docPointerArgsGetMethod;

	private static MethodInfo? s_onDocPointerPressedMethod;

	private static MethodInfo? s_onDocPointerMovedMethod;

	private static MethodInfo? s_onDocPointerReleasedMethod;

	// Viewport dimensions (single-canvas app): instance properties backed
	// by statics so static diagnostic/fit paths (deferred BookShelf fit)
	// read the same settled values the instance paths write.
	private static int s_viewWidth = 1200;
	private static int s_viewHeight = 950;
	public int ViewWidth { get => s_viewWidth; set => s_viewWidth = value; }

	public int ViewHeight { get => s_viewHeight; set => s_viewHeight = value; }

	internal void SetDocumentPageBounds(int x, int y, int w, int h)
	{
		_documentPageBounds = new Rectangle(x, y, w, h);
	}

	public void RecordInput(string kind, string detail)
	{
		_inputSeq++;
		string item = $"#{_inputSeq} {DateTimeOffset.UtcNow:HH:mm:ss.fff} {kind} {detail}".TrimEnd();
		_inputTrace.Enqueue(item);
		while (_inputTrace.Count > 300)
		{
			_inputTrace.Dequeue();
		}
	}

	public string RecentInputContext(int maxLines = 30)
	{
		string[] array = _inputTrace.ToArray();
		int count = Math.Max(0, array.Length - maxLines);
		return string.Join('\n', array.Skip(count));
	}

	private static string DescribeControl(System.Windows.Forms.Control? c)
	{
		return (c == null) ? "null" : (c.GetType().Name + " '" + c.Name + "'");
	}

	private static string DescribeItem(System.Windows.Forms.ToolStripItem? item)
	{
		return (item == null) ? "null" : (item.GetType().Name + " '" + item.Name + "'");
	}

	public void Dispose()
	{
		try { _hoverTimer?.Dispose(); } catch { }
		_hoverTimer = null;
		// Test + scene lifecycle: an abandoned host leaves its forms
		// Visible in the process-global WasmDialogHost registry, where
		// TopTrackedWindowAt prefers them over later hosts' own forms
		// at the same coords (suite order-contamination: clicks land on
		// stale forms). Disposing closes the owned forms so the registry
		// prunes them (Visible/Disposed); never throws.
		// Paint.NET forms are hidden, not disposed: PDN is a genuine
		// single-instance app whose MainForm teardown disposes process
		// statics (color profiles, device contexts) that later scenes
		// reuse (ObjectDisposedException in doc creation). Hiding still
		// prunes them from routing (invisible entries drop on snapshot)
		// while keeping the shared statics alive, matching the browser
		// (one PDN app per session, never torn down mid-session).
		foreach (var form in _forms.Values.ToList())
		{
			try
			{
				string? fullName = null;
				try { fullName = form.GetType().FullName; } catch { }
				if (fullName != null && fullName.StartsWith("PaintDotNet.", StringComparison.Ordinal))
				{
					try { form.Visible = false; } catch { }
					try { System.Windows.Forms.WasmDialogHost.Untrack(form); } catch { }
				}
				else
				{
					try { form.Close(); } catch { }
					try { form.Dispose(); } catch { }
				}
			}
			catch { }
		}
		_forms.Clear();
		_activeForm = null;
		SyncForkForeground(null);
	}

	public WinFormsAppHost()
	{
		InitializeAppDomainResolver();
		ResetProcessInputSingletons();
		// Viewport statics back the instance properties: a previous
		// host's dimensions must not leak into a new host (defaults
		// 1200x950; the browser reports the real size right after).
		s_viewWidth = 1200;
		s_viewHeight = 950;
	}

	// Fresh-host isolation: mouse/capture/dialog state lives in process
	// singletons (one cursor, one capture, one dialog registry: desktop
	// parity for a single user). A previous host's leftovers (stale
	// capture, cursor buttons, abandoned tracked windows) would otherwise
	// steal the new host's input routing. The browser constructs one host
	// per session (fresh singletons: no-op); tests construct one per test
	// (leftovers cleared). Never throws.
	private static void ResetProcessInputSingletons()
	{
		try { System.Windows.Forms.WasmMouseState.Reset(); } catch { }
		try { System.Windows.Forms.WasmCaptureState.Clear(); } catch { }
		try { System.Windows.Forms.WasmDialogHost.ResetForNewHost(); } catch { }
	}

	private static void InitializeAppDomainResolver()
	{
		AppDomain.CurrentDomain.AssemblyResolve += delegate(object? sender, ResolveEventArgs args)
		{
			AssemblyName an = new AssemblyName(args.Name);
			if (an.Name == "System.Windows.Forms" || an.Name == "System.Windows.Forms.Primitives")
			{
				return typeof(System.Windows.Forms.Form).Assembly;
			}
			if (an.Name == "System.ComponentModel.Primitives")
			{
				return typeof(Component).Assembly;
			}
			return (an.Name == "System.Drawing.Common" || an.Name == "System.Drawing" || an.Name == "System.Drawing.Primitives") ? typeof(Font).Assembly : AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault((Assembly a) => a.GetName().Name == an.Name);
		};
		try
		{
			Assembly assembly = typeof(Point).Assembly;
			Type type = assembly.GetType("System.Drawing.PointConverter");
			Type type2 = assembly.GetType("System.Drawing.PointFConverter");
			Type type3 = assembly.GetType("System.Drawing.SizeConverter");
			Type type4 = assembly.GetType("System.Drawing.SizeFConverter");
			Type type5 = assembly.GetType("System.Drawing.RectangleConverter");
			Type type6 = assembly.GetType("System.Drawing.RectangleFConverter");
			Type type7 = assembly.GetType("System.Drawing.ColorConverter");
			Type type8 = assembly.GetType("System.Drawing.FontConverter");
			if ((object)type != null)
			{
				TypeDescriptor.AddAttributes(typeof(Point), new TypeConverterAttribute(type));
			}
			if ((object)type2 != null)
			{
				TypeDescriptor.AddAttributes(typeof(PointF), new TypeConverterAttribute(type2));
			}
			if ((object)type3 != null)
			{
				TypeDescriptor.AddAttributes(typeof(Size), new TypeConverterAttribute(type3));
			}
			if ((object)type4 != null)
			{
				TypeDescriptor.AddAttributes(typeof(SizeF), new TypeConverterAttribute(type4));
			}
			if ((object)type5 != null)
			{
				TypeDescriptor.AddAttributes(typeof(Rectangle), new TypeConverterAttribute(type5));
			}
			if ((object)type6 != null)
			{
				TypeDescriptor.AddAttributes(typeof(RectangleF), new TypeConverterAttribute(type6));
			}
			if ((object)type7 != null)
			{
				TypeDescriptor.AddAttributes(typeof(Color), new TypeConverterAttribute(type7));
			}
			if ((object)type8 != null)
			{
				TypeDescriptor.AddAttributes(typeof(Font), new TypeConverterAttribute(type8));
			}
		}
		catch
		{
		}
	}

	private static Exception? s_lastFirstChance;
	private static bool s_globalFirstChanceHooked;

	private static Exception RootCause(Exception ex)
	{
		Exception root = ex;
		while (root.InnerException is not null)
			root = root.InnerException;
		return root;
	}

	private static string SiteOf(Exception ex)
	{
		try
		{
			MethodBase? site = ex.TargetSite;
			if (site?.DeclaringType is null)
				return "";
			return site.DeclaringType.FullName + "." + site.Name;
		}
		catch
		{
			return "";
		}
	}

	private static string SiteChain(Exception ex)
	{
		StringBuilder sb = new();
		for (Exception? e = ex; e is not null; e = e.InnerException)
		{
			if (sb.Length > 0)
				sb.Append(" <- ");
			sb.Append(e.GetType().Name);
			string site = SiteOf(e);
			if (site.Length > 0)
				sb.Append('@').Append(site);
		}
		return sb.ToString();
	}

	public static object? FindCaretElement(object? node, int depth)
	{
		try
		{
			if (node is null || depth > 64)
				return null;
			System.Reflection.PropertyInfo? caretProp = node.GetType().GetProperty("Caret");
			object? caret = null;
			try { caret = caretProp?.GetValue(node); } catch { }
			if (caret is not null)
				return caret;
			System.Reflection.PropertyInfo? kidsProp = node.GetType().GetProperty("Children");
			object? kids = null;
			try { kids = kidsProp?.GetValue(node); } catch { }
			if (kids is System.Collections.IEnumerable seq)
			{
				foreach (object? kid in seq)
				{
					object? found = FindCaretElement(kid, depth + 1);
					if (found is not null)
						return found;
				}
			}
			return null;
		}
		catch { return null; }
	}

	public static string CaretProbeInfo(object? control, System.Drawing.Rectangle rc)
	{
		try
		{
			if (control is null)
				return "no-root-element";
			System.Reflection.PropertyInfo? rootProp = control.GetType().GetProperty("RootElement");
			if (rootProp is null)
				return "no-root-element";
			object? root = null;
			try { root = rootProp.GetValue(control); } catch { }
			if (root is null)
				return "no-root-element";
			object? caret = FindCaretElement(root, 0);
			if (caret is null)
				return "caret-not-found";
			System.Type ct = caret.GetType();
			string color = "";
			string vis = "";
			System.Drawing.PointF pos = new();
			float w = 0, h = 0;
			System.Drawing.Rectangle crect = new();
			try { color = (string?)ct.GetProperty("Color")?.GetValue(caret) ?? ""; } catch { }
			try { vis = (string?)ct.GetProperty("Visibility")?.GetValue(caret) ?? ""; } catch { }
			try { pos = (System.Drawing.PointF)(ct.GetProperty("Position")?.GetValue(caret) ?? new System.Drawing.PointF()); } catch { }
			try { w = System.Convert.ToSingle(ct.GetProperty("Width")?.GetValue(caret) ?? 0f); } catch { }
			try { h = System.Convert.ToSingle(ct.GetProperty("Height")?.GetValue(caret) ?? 0f); } catch { }
			try { crect = (System.Drawing.Rectangle)(ct.GetProperty("ControlBoundingRectangle")?.GetValue(caret) ?? new System.Drawing.Rectangle()); } catch { }
			var tick = new System.Drawing.Rectangle(crect.X + (int)pos.X - (int)w, crect.Y + (int)pos.Y - (int)h, (int)w, (int)h);
			string geom = rc.Contains(tick) ? "ok" : "miss";
			return $"caret={ct.Name} color={color} pos={pos.X:0.0},{pos.Y:0.0} visible={vis} geom={geom}";
		}
		catch { return "probe-error"; }
	}

	private static System.Windows.Forms.Form? CreateAppForm(string typeName, string assemblyName)
	{
		try
		{
			Type? type = Type.GetType(typeName + ", " + assemblyName);
			type ??= AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault((Assembly a) => string.Equals(a.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))?.GetType(typeName);
			if (type == null)
			{
				Console.WriteLine($"[WinFormsAppHost] Form type not found: {typeName} in {assemblyName}");
				type = FindSingleStartupForm(assemblyName, out string available);
				if (type is null)
				{
					BrowserErrorReporter.Report("form-create", new TypeLoadException($"Form type not found: {typeName} in {assemblyName}. Available: {available}"));
					return null;
				}
				Console.WriteLine($"[WinFormsAppHost] Form fallback engaged: {type.FullName} (requested {typeName})");
			}
			try { Console.WriteLine("[APP-LIFE] create " + type.FullName); } catch { }
			Exception? firstChance = null;
		void FirstChanceHandler(object? sender, FirstChanceExceptionEventArgs args)
		{
			try
			{
				Exception? e = args.Exception;
				if (e is not null)
				{
					string msg = "";
					try { msg = (e.Message ?? "").Split('\n')[0]; } catch { }
					try { Console.WriteLine("[CTOR-1ST] " + e.GetType().Name + ": " + msg); } catch { }
				}
			}
			catch { }
			try { firstChance ??= args.Exception; } catch { }
		}
		AppDomain.CurrentDomain.FirstChanceException += FirstChanceHandler;
		System.Windows.Forms.Form? created = null;
		try
		{
			created = (System.Windows.Forms.Form)Activator.CreateInstance(type);
				try { Console.WriteLine("[APP-LIFE] constructed " + type.FullName); } catch { }
				System.Exception? guarded = null;
				try { guarded = System.Windows.Forms.WasmCtorGuard.TakeStashed(); } catch { }
				if (guarded is not null)
				{
					try { Console.WriteLine("[WinFormsAppHost] ctor-guard stashed " + type.FullName + ": " + guarded.GetType().Name + ": " + (guarded.Message ?? "").Split('\n')[0]); } catch { }
					try { BrowserErrorReporter.Report("form-create", guarded); } catch { }
					try { created?.Dispose(); } catch { }
					return null;
				}
				return created;
		}
		finally
		{
			AppDomain.CurrentDomain.FirstChanceException -= FirstChanceHandler;
			if (firstChance is not null)
				s_lastFirstChance = firstChance;
		}
		}
		catch (Exception ex)
		{
			// Print + report BEFORE any TargetSite touch: resolving
			// hostile (COM-interop/cctor) frames aborts the browser
			// runtime uncatchably (func96 OOB trap).
			Console.WriteLine($"[WinFormsAppHost] Error creating {typeName}: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
			BrowserErrorReporter.Report("form-create", ex);
			if (s_lastFirstChance is not null && !ReferenceEquals(s_lastFirstChance, RootCause(ex)))
				Console.WriteLine($"[WinFormsAppHost] First-chance during create: {s_lastFirstChance.GetType().Name} site={SiteOf(s_lastFirstChance)} msg={(s_lastFirstChance.Message ?? "").Split('\n')[0]}");
			s_lastFirstChance = null;
			try { System.Windows.Forms.WasmCtorGuard.TakeStashed(); } catch { }
			Console.WriteLine($"[WinFormsAppHost] Error creating {typeName}: site={SiteChain(ex)}");
			return null;
		}
	}

	private static bool s_erpSeeded;

	private static void EnsureErpDataInitialized()
	{
		// Desktop Program.Main seeds MainRepository.InitializeData() before
		// Application.Run; the browser never invokes Main (forms are
		// constructed directly), so the 14 JSON caches stay null and every
		// grid ctor dies at Enumerable.Count. Invoke the same public static
		// seed Main calls (content is already restored: LoadSelectionAsync
		// awaits EnsureAppAsync before SelectScene creates the form).
		// The once-guard sets only on success so a failed attempt retries
		// on the next scene entry.
		if (s_erpSeeded)
			return;
		try
		{
			Assembly asm = AppDomain.CurrentDomain.GetAssemblies()
				.FirstOrDefault(a => string.Equals(a.GetName().Name, "ERP.Repository", StringComparison.OrdinalIgnoreCase))
				?? Assembly.Load("ERP.Repository");
			asm.GetType("ERP.Repository.MainRepository")
				?.GetMethod("InitializeData", BindingFlags.Static | BindingFlags.Public)
				?.Invoke(null, null);
			s_erpSeeded = true;
			Console.WriteLine("[WinFormsAppHost] ERP InitializeData invoked.");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[WinFormsAppHost] ERP InitializeData FAILED: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
		}
	}

	public System.Windows.Forms.Form? GetOrCreateForm(AppScene scene)
	{
		if (_forms.TryGetValue(scene, out System.Windows.Forms.Form value))
		{
			return value;
		}
		System.Windows.Forms.Form form = null;
		try
		{
			switch (scene)
			{
			case AppScene.Calculator:
			{
				Type type2 = Type.GetType("Calculator.Form1, Calculator");
				if (type2 != null)
				{
					form = (System.Windows.Forms.Form)Activator.CreateInstance(type2);
				}
				break;
			}
			case AppScene.TicTacToe:
			{
				Type type4 = Type.GetType("Tic_Tac_Toe.Form1, Tic Tac Toe");
				if (type4 != null)
				{
					form = (System.Windows.Forms.Form)Activator.CreateInstance(type4);
				}
				break;
			}
			case AppScene.Todo:
			{
				Type type5 = Type.GetType("SimpleDailyToDoList.ToDoListDashBoard_FR, SimpleDailyToDoList");
				if (type5 != null)
				{
					form = (System.Windows.Forms.Form)Activator.CreateInstance(type5);
				}
				break;
			}
			case AppScene.Calendar:
			{
				Type type3 = Type.GetType("grp2Calendar.Form1, grp2Calendar");
				if (type3 != null)
				{
					form = (System.Windows.Forms.Form)Activator.CreateInstance(type3);
				}
				break;
			}
			case AppScene.Smoke:
				form = CreateSmokeForm();
				break;
			case AppScene.Paint:
				form = CreatePaintForm();
				break;
			case AppScene.TelerikPhoto:
			{
				Type type = Type.GetType("PhotoAlbumCS.Form1, PhotoAlbum");
				if (type != null)
				{
					form = (System.Windows.Forms.Form)Activator.CreateInstance(type);
				}
				break;
			}
			case AppScene.TelerikBookshelf:
				form = CreateAppForm("BookShelf.MainForm", "BookShelf");
				break;
			case AppScene.TelerikBugtracker:
				form = CreateAppForm("BugTracker.RadRibbonForm1", "BugTracker");
				break;
			case AppScene.TelerikDemoAppsHub:
				form = CreateAppForm("DemoAppsHub.Form1", "DemoAppsHub");
				break;
			case AppScene.TelerikDiagram:
				form = CreateAppForm("DiagramFirstLook.Form1", "DiagramFirstLook");
				break;
			case AppScene.TelerikErp:
				EnsureErpDataInitialized();
				form = CreateAppForm("ERP.Client.MainForm", "ERP.Client");
				break;
			case AppScene.TelerikExportWithDpl:
				form = CreateAppForm("ExportWithDpl.GridView.Form1", "ExportWithDpl");
				break;
			case AppScene.TelerikFileExplorer:
				form = CreateAppForm("FileExplorer.RadRibbonForm1", "FileExplorer");
				break;
			case AppScene.TelerikHotelApp:
				form = CreateAppForm("HotelApp.HotelAppForm", "HotelApp");
				break;
			case AppScene.TelerikMedicalApp:
				form = CreateAppForm("MedicalAppCS.MainForm", "MedicalAppCS");
				break;
			case AppScene.TelerikRichTextEditor:
				form = CreateAppForm("RichTextEditor.FirstLook.Form1", "RichTextEditor");
				break;
			case AppScene.TelerikRssReader:
				form = CreateAppForm("RssReader.RadForm1", "RssReader");
				break;
			case AppScene.TelerikSpellCheck:
				form = CreateAppForm("SpellCheckAsYouType.Form1", "SpellCheckAsYouType");
				break;
			case AppScene.TelerikToast:
				form = CreateAppForm("ToastNotificationManagerExamples.FirstLook.Form1", "ToastNotificationManagerExamples");
				break;
			}
			if (form is null && SyncfusionDemos.Find(scene) is { } sfdemo)
				form = CreateAppForm(sfdemo.FormType, sfdemo.Assembly);
			if (form != null)
			{
				form.Visible = true;
				try
				{
					typeof(System.Windows.Forms.Form).GetMethod("OnLoad", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(form, new object[1] { EventArgs.Empty });
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[WinFormsAppHost] Error invoking OnLoad for {scene}: {ex.GetType().Name}: {ex.Message.Split('\n')[0]} site={SiteOf(ex)}");
					BrowserErrorReporter.Report("form-onload", ex);
				}
				int num = Math.Max(form.Width, form.ClientSize.Width);
				int num2 = Math.Max(form.Height, form.ClientSize.Height);
				if (form.Width <= 0 && form.ClientSize.Width > 0)
				{
					form.Width = form.ClientSize.Width;
				}
				if (form.Height <= 0 && form.ClientSize.Height > 0)
				{
					form.Height = form.ClientSize.Height;
				}
				if (scene == AppScene.Paint)
				{
					form.Width = Math.Min(1200, ViewWidth - 20);
					form.Height = Math.Min(880, ViewHeight - 40);
					form.Location = new Point(10, 10);
					form.PerformLayout();
					if (form.Controls.Count > 0)
					{
						System.Windows.Forms.Control control = form.Controls[0];
						control.Size = form.ClientSize;
						PerformLayoutRecursive(control);
					}
				}
				else
				{
					CenterInViewport(form);
					_autoCentered.Add(form);
				}
				_forms[scene] = form;
			}
			else
			{
				Console.WriteLine($"[WinFormsAppHost] No form created for scene {scene}: missing switch case or type lookup failed; canvas will stay blank.");
			}
		}
		catch (Exception ex2)
		{
			Console.WriteLine($"[WinFormsAppHost] Error creating form for {scene}: {ex2.GetType().Name}: {ex2.Message.Split('\n')[0]} site={SiteOf(ex2)}");
			Console.WriteLine($"[WinFormsAppHost] Error creating form for {scene}: root={RootCause(ex2).GetType().Name} site={SiteOf(RootCause(ex2))} msg={(RootCause(ex2).Message ?? "").Split('\n')[0]}");
			try { Console.WriteLine("[diagstack] " + string.Join(" <- ", new System.Diagnostics.StackTrace(RootCause(ex2), false).GetFrames().Take(12).Select(f => f.GetMethod()))); } catch { }
			BrowserErrorReporter.Report("form-create", ex2);
		}
		return form;
	}
	/// <summary>
	/// Fallback when the configured form type name is wrong: if the app assembly
	/// contains exactly one concrete Form subclass with a public parameterless
	/// constructor, return it; otherwise null. 'available' lists candidate names
	/// for the error report so a human can fix the mapping.
	/// </summary>
	private static Type? FindSingleStartupForm(string assemblyName, out string available)
	{
		available = "";
		try
		{
			System.Reflection.Assembly? asm = AppDomain.CurrentDomain.GetAssemblies()
				.FirstOrDefault(a => string.Equals(a.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
			if (asm is null)
			{
				return null;
			}
			Type[] forms;
			try
			{
				forms = asm.GetTypes();
			}
			catch (System.Reflection.ReflectionTypeLoadException rtle)
			{
				forms = rtle.Types.Where(tt => tt is not null).ToArray()!;
			}
			var candidates = forms
				.Where(tt => tt is not null && tt.IsClass && !tt.IsAbstract
					&& typeof(System.Windows.Forms.Form).IsAssignableFrom(tt)
					&& tt.GetConstructor(Type.EmptyTypes) is not null)
				.ToArray();
			available = string.Join(",", candidates.Select(tt => tt!.FullName));
			return candidates.Length == 1 ? candidates[0] : null;
		}
		catch
		{
			return null;
		}
	}


	private static System.Windows.Forms.Form CreateSmokeForm()
	{
		System.Windows.Forms.Form form = new System.Windows.Forms.Form
		{
			Text = "WinForms WebAssembly Smoke Test",
			Width = 500,
			Height = 350
		};
		System.Windows.Forms.Label value = new System.Windows.Forms.Label
		{
			Text = "Running real System.Windows.Forms on WebAssembly!",
			Left = 30,
			Top = 40,
			Width = 440,
			Height = 30,
			Font = new Font("Segoe UI", 12f)
		};
		System.Windows.Forms.Button button = new System.Windows.Forms.Button
		{
			Text = "Click Me",
			Left = 30,
			Top = 90,
			Width = 120,
			Height = 36
		};
		System.Windows.Forms.Label status = new System.Windows.Forms.Label
		{
			Text = "Clicks: 0",
			Left = 170,
			Top = 98,
			Width = 200,
			Height = 25
		};
		int clickCount = 0;
		button.Click += delegate
		{
			clickCount++;
			status.Text = $"Clicks: {clickCount} (Success!)";
		};
		form.Controls.Add(value);
		form.Controls.Add(button);
		form.Controls.Add(status);
		return form;
	}

	private static void EnsurePaintDotNetInitialized()
	{
		if (s_pdnInitialized)
		{
			return;
		}
		s_pdnInitialized = true;
		try
		{
			ConcurrentDictionary<int, string> concurrentDictionary = new ConcurrentDictionary<int, string>(20, 0);
			string orAdd = concurrentDictionary.GetOrAdd(1, (int _) => "x");
			Console.WriteLine("[DICTNULL] nonnull-factory ok=" + orAdd);
			string orAdd2 = concurrentDictionary.GetOrAdd(2, (int _) => (string)null);
			Console.WriteLine("[DICTNULL] null-factory returned " + ((orAdd2 == null) ? "null" : orAdd2));
		}
		catch (Exception ex)
		{
			Console.WriteLine("[DICTNULL] threw " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]);
		}
		try
		{
			Directory.CreateDirectory("C:\\PaintDotNet\\Wasm\\FileTypes");
		}
		catch
		{
		}
		if (OperatingSystem.IsBrowser())
		{
			try
			{
				Directory.CreateDirectory("/Paint.NET App Files/FileTypes");
			}
			catch
			{
			}
			try
			{
				Directory.CreateDirectory("/C:\\PaintDotNet\\Wasm\\/FileTypes");
			}
			catch
			{
			}
			try
			{
				Directory.CreateDirectory("C:/PaintDotNet/Wasm/FileTypes");
			}
			catch
			{
			}
		}
		try
		{
			Directory.SetCurrentDirectory("C:\\PaintDotNet\\Wasm");
		}
		catch
		{
		}
		TrimRoots.Keep();
		try
		{
			if (!OperatingSystem.IsBrowser())
			{
				// Testhost-only: Microsoft.NETCore.App ships a WindowsBase facade
				// without DependencyObject. If it loads first, PDN type-init dies
				// with TypeLoadException and wedges the init lock (PaintSceneTests
				// hang). Pre-load the full app-local copy so every later
				// WindowsBase resolution binds to it (first-loaded wins).
				try
				{
					string wbPreloadPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "WindowsBase.dll");
					if (System.IO.File.Exists(wbPreloadPath))
					{
						System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(wbPreloadPath);
						Console.WriteLine("[PaintDotNet] Full WindowsBase pre-loaded.");
					}
				}
				catch (Exception exWbPreload)
				{
					Console.WriteLine("[PaintDotNet] WindowsBase pre-load skipped: " + exWbPreload.GetType().Name);
				}
			}
			// (UI culture pin hoisted to SelectScene: all scenes, not just Paint.)
			PreseedWpfSystemParameters();
			AppDomain.CurrentDomain.FirstChanceException += delegate(object? s, FirstChanceExceptionEventArgs e)
			{
				if (!(e.Exception is MissingManifestResourceException))
				{
					StackFrame[] frames = new StackTrace(2, fNeedFileInfo: false).GetFrames();
					try
					{
						string stackTrace = e.Exception.StackTrace;
						Console.WriteLine("[1ST-EST] " + ((stackTrace == null) ? "null" : stackTrace.Substring(0, Math.Min(600, stackTrace.Length))).Replace('\r', ' ').Replace('\n', ' '));
					}
					catch
					{
					}
					string value3 = ((frames == null) ? "" : string.Join(" <- ", from f in frames.Take(8)
						select f.GetMethod()?.DeclaringType?.Name + "." + f.GetMethod()?.Name));
					string text = $"[1ST] {e.Exception.GetType().Name}: {e.Exception.Message.Split('\n')[0]} :: {value3}";
					for (int num = 0; num * 400 < text.Length; num++)
					{
						Console.WriteLine($"[1ST-{num}] " + text.Substring(num * 400, Math.Min(400, text.Length - num * 400)));
					}
					MethodBase methodBase = ((frames == null || frames.Length == 0) ? null : frames[0].GetMethod());
					string topFrame = ((methodBase == null) ? null : (methodBase.DeclaringType?.Name + "." + methodBase.Name));
					BrowserErrorReporter.Report("first-chance", e.Exception, topFrame);
				}
			};
			if (SynchronizationContext.Current == null)
			{
				SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
			}
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
			Assembly assembly = assemblies.FirstOrDefault((Assembly a) => a.GetName().Name == "PaintDotNet.Base") ?? Assembly.Load("PaintDotNet.Base");
			Assembly assembly2 = assemblies.FirstOrDefault((Assembly a) => a.GetName().Name == "PaintDotNet.Core") ?? Assembly.Load("PaintDotNet.Core");
			Assembly assembly3 = assemblies.FirstOrDefault((Assembly a) => a.GetName().Name == "paintdotnet") ?? Assembly.Load("paintdotnet");
			assembly.GetType("PaintDotNet.Threading.PdnSynchronizationContext")?.GetMethod("Install", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
			Type type = assembly3.GetType("PaintDotNet.AppModel.AppInfoService");
			object obj6 = type?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
			Type type2 = assembly2.GetType("PaintDotNet.AppModel.AppInstallType");
			if (type != null && type2 != null && obj6 != null)
			{
				object value = Enum.ToObject(type2, 0);
				type.GetProperty("InstallType", BindingFlags.Instance | BindingFlags.Public)?.SetValue(obj6, value);
			}
			Type type3 = assembly2.GetType("PaintDotNet.Animation.AnimationService");
			try
			{
				type3?.GetMethod("Initialize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
			}
			catch (Exception ex2)
			{
				Console.WriteLine("[PaintDotNet] AnimationService init skipped: " + ex2.GetType().Name);
			}
			Type type4 = assembly3.GetType("PaintDotNet.Settings.MemoryStorageHandler");
			if (type4 != null)
			{
				object obj7 = Activator.CreateInstance(type4, new object[1] { true });
				assembly3.GetType("PaintDotNet.Settings.App.AppSettings")?.GetMethod("Initialize", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, new object[1] { obj7 });
			}
			assembly3.GetType("PaintDotNet.AppModel.UserFilesService")?.GetMethod("Initialize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
			assembly3.GetType("PaintDotNet.AppModel.PalettesService")?.GetMethod("Initialize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
			assembly3.GetType("PaintDotNet.AppModel.ResourcesService")?.GetMethod("Initialize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
			assembly2.GetType("PaintDotNet.IndirectUI.ControlInfo")?.GetMethod("Initialize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, new object[1] { assembly3 });
			Assembly assembly4 = assemblies.FirstOrDefault((Assembly a) => a.GetName().Name == "PaintDotNet.Data") ?? Assembly.Load("PaintDotNet.Data");
			Type type5 = assembly4?.GetType("PaintDotNet.Document");
			Type type6 = assembly4?.GetType("PaintDotNet.Layer");
			type5?.GetMethod("Initialize", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, new object[1]
			{
				new Version(5, 1)
			});
			type6?.GetMethod("Initialize", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, new object[1] { "Background" });
			Console.WriteLine("[PaintDotNet] Successfully bootstrapped runtime services!");
		}
		catch (Exception value2)
		{
			Console.WriteLine($"[PaintDotNet] Bootstrap exception: {value2}");
		}
	}

	private static System.Windows.Forms.Form CreatePaintForm()
	{
		EnsurePaintDotNetInitialized();
		try
		{
			Type type = Type.GetType("PaintDotNet.Dialogs.MainForm, paintdotnet") ?? AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault((Assembly a) => a.GetName().Name == "paintdotnet")?.GetType("PaintDotNet.Dialogs.MainForm");
			if (type != null)
			{
				System.Windows.Forms.Form result = (System.Windows.Forms.Form)Activator.CreateInstance(type, new object[1] { Array.Empty<string>() });
				Console.WriteLine("[PaintDotNet] Using real MainForm as scene form.");
				return result;
			}
			Console.WriteLine("[PaintDotNet] MainForm type not found; using wrapper form.");
		}
		catch (Exception ex)
		{
			Console.WriteLine("[PaintDotNet] Real MainForm failed, falling back to wrapper: " + ex.GetType().Name + ": " + ex.Message);
		}
		System.Windows.Forms.Form form = new System.Windows.Forms.Form
		{
			Text = "Paint.NET v5.1.12 (WebAssembly)",
			Width = 1200,
			Height = 850
		};
		try
		{
			Type type2 = Type.GetType("PaintDotNet.Controls.AppWorkspace, paintdotnet") ?? AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault((Assembly a) => a.GetName().Name == "paintdotnet")?.GetType("PaintDotNet.Controls.AppWorkspace");
			if (type2 != null)
			{
				System.Windows.Forms.Control control = (System.Windows.Forms.Control)Activator.CreateInstance(type2);
				control.Dock = System.Windows.Forms.DockStyle.Fill;
				form.Controls.Add(control);
				Console.WriteLine($"[PaintDotNet] AppWorkspace added to host form: {control}");
				InitializeBlankDocument(control, type2);
				EnsureMainMenuVisible(control);
			}
		}
		catch (Exception value)
		{
			Console.WriteLine($"[PaintDotNet] Error creating AppWorkspace: {value}");
		}
		return form;
	}

	private static void EnsureMainMenuVisible(System.Windows.Forms.Control root)
	{
		try
		{
			Queue<System.Windows.Forms.Control> queue = new Queue<System.Windows.Forms.Control>();
			queue.Enqueue(root);
			while (queue.Count > 0)
			{
				System.Windows.Forms.Control control = queue.Dequeue();
				if (control is System.Windows.Forms.ToolStrip toolStrip && control.Name == "PdnMainMenu")
				{
					foreach (System.Windows.Forms.ToolStripItem item2 in toolStrip.Items)
					{
						item2.Visible = true;
					}
					toolStrip.PerformLayout();
				}
				foreach (System.Windows.Forms.Control control2 in control.Controls)
				{
					queue.Enqueue(control2);
				}
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("[PaintDotNet] EnsureMainMenuVisible failed: " + ex.GetType().Name + ": " + ex.Message);
		}
	}

	private static void InitializeBlankDocument(object ws, Type appWorkspaceType)
	{
		try
		{
			Console.WriteLine("[PaintDotNet] InitializeBlankDocument: start");
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
			Assembly assembly = assemblies.FirstOrDefault((Assembly a) => a.GetName().Name == "PaintDotNet.Core") ?? Assembly.Load("PaintDotNet.Core");
			Assembly assembly2 = assemblies.FirstOrDefault((Assembly a) => a.GetName().Name == "PaintDotNet.Primitives") ?? Assembly.Load("PaintDotNet.Primitives");
			Type type = assembly?.GetType("PaintDotNet.MeasurementUnit");
			Type type2 = assembly2?.GetType("PaintDotNet.Rendering.SizeInt32");
			MethodInfo method = appWorkspaceType.GetMethod("CreateBlankDocumentInNewWorkspace", BindingFlags.Instance | BindingFlags.Public);
			if (method != null && type2 != null && type != null)
			{
				object obj = Activator.CreateInstance(type2, 800, 600);
				object obj2 = Enum.ToObject(type, 2);
				Console.WriteLine("[PaintDotNet] Invoking CreateBlankDocumentInNewWorkspace(SizeInt32(800, 600), Inch, 96.0, true)...");
				object obj3 = method.Invoke(ws, new object[4] { obj, obj2, 96.0, true });
				Console.WriteLine($"[PaintDotNet] CreateBlankDocumentInNewWorkspace result: {obj3}");
				if (object.Equals(obj3, true))
				{
					Console.WriteLine("[PaintDotNet] Document successfully initialized via CreateBlankDocumentInNewWorkspace!");
					return;
				}
			}
		}
		catch (Exception ex)
		{
			LogExceptionChain("[PaintDotNet] CreateBlankDocumentInNewWorkspace failed:", ex);
		}
		try
		{
			Assembly[] assemblies2 = AppDomain.CurrentDomain.GetAssemblies();
			Assembly assembly3 = assemblies2.FirstOrDefault((Assembly a) => a.GetName().Name == "PaintDotNet.Data") ?? Assembly.Load("PaintDotNet.Data");
			Assembly assembly4 = assemblies2.FirstOrDefault((Assembly a) => a.GetName().Name == "PaintDotNet.Core") ?? Assembly.Load("PaintDotNet.Core");
			Type type3 = assembly3?.GetType("PaintDotNet.Document");
			Type layerType = assembly3?.GetType("PaintDotNet.Layer");
			if (type3 != null && layerType != null)
			{
				int num = 800;
				int num2 = 600;
				Console.WriteLine("[PaintDotNet] Creating Document(800, 600)...");
				object obj4 = Activator.CreateInstance(type3, num, num2);
				Type type4 = assembly4?.GetType("PaintDotNet.MeasurementUnit");
				if (type4 != null)
				{
					type3.GetProperty("DpuUnit", BindingFlags.Instance | BindingFlags.Public)?.SetValue(obj4, Enum.ToObject(type4, 2));
				}
				type3.GetProperty("DpuX", BindingFlags.Instance | BindingFlags.Public)?.SetValue(obj4, 96.0);
				type3.GetProperty("DpuY", BindingFlags.Instance | BindingFlags.Public)?.SetValue(obj4, 96.0);
				Console.WriteLine("[PaintDotNet] Document created. Creating BackgroundLayer...");
				MethodInfo methodInfo = layerType.GetMethod("CreateBackgroundLayer", BindingFlags.Static | BindingFlags.Public, new Type[3]
				{
					typeof(int),
					typeof(int),
					typeof(string)
				}) ?? layerType.GetMethod("CreateBackgroundLayer", BindingFlags.Static | BindingFlags.Public, new Type[2]
				{
					typeof(int),
					typeof(int)
				});
				object obj5 = null;
				obj5 = ((!(methodInfo != null) || methodInfo.GetParameters().Length != 3) ? methodInfo?.Invoke(null, new object[2] { num, num2 }) : methodInfo.Invoke(null, new object[3] { num, num2, "Background" }));
				Console.WriteLine($"[PaintDotNet] BackgroundLayer created: {obj5 != null}");
				object obj6 = type3.GetProperty("Layers", BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj4);
				(obj6?.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo m) => m.Name == "Add" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsAssignableFrom(layerType)))?.Invoke(obj6, new object[1] { obj5 });
				Console.WriteLine("[PaintDotNet] Layer added to doc. Calling AddNewDocumentWorkspace...");
				object obj7 = appWorkspaceType.GetMethod("AddNewDocumentWorkspace", BindingFlags.Instance | BindingFlags.Public)?.Invoke(ws, null);
				Console.WriteLine($"[PaintDotNet] AddNewDocumentWorkspace returned: {obj7 != null}");
				if (obj7 != null)
				{
					object obj8 = appWorkspaceType.GetProperty("Widgets", BindingFlags.Instance | BindingFlags.Public)?.GetValue(ws);
					object obj9 = (obj8?.GetType().GetProperty("DocumentStrip", BindingFlags.Instance | BindingFlags.Public))?.GetValue(obj8);
					MethodInfo methodInfo2 = obj9?.GetType().GetMethod("LockDocumentWorkspaceDirtyValue", BindingFlags.Instance | BindingFlags.Public);
					MethodInfo methodInfo3 = obj9?.GetType().GetMethod("UnlockDocumentWorkspaceDirtyValue", BindingFlags.Instance | BindingFlags.Public);
					methodInfo2?.Invoke(obj9, new object[2] { obj7, false });
					Type baseType = obj7.GetType().BaseType;
					Console.WriteLine("[PaintDotNet] Setting dw.Document...");
					baseType?.GetProperty("Document", BindingFlags.Instance | BindingFlags.Public)?.SetValue(obj7, obj4);
					Console.WriteLine("[PaintDotNet] Setting dw.ActiveLayer...");
					obj7.GetType().GetProperty("ActiveLayer", BindingFlags.Instance | BindingFlags.Public)?.SetValue(obj7, obj5);
					type3.GetProperty("Dirty", BindingFlags.Instance | BindingFlags.Public)?.SetValue(obj4, false);
					Console.WriteLine("[PaintDotNet] Calling set_ActiveDocumentWorkspace...");
					appWorkspaceType.GetMethod("set_ActiveDocumentWorkspace", BindingFlags.Instance | BindingFlags.Public)?.Invoke(ws, new object[1] { obj7 });
					Console.WriteLine("[PaintDotNet] set_ActiveDocumentWorkspace finished!");
					obj7.GetType().GetMethod("SetDocumentSaveOptions", BindingFlags.Instance | BindingFlags.Public)?.Invoke(obj7, new object[3]);
					object obj10 = obj7.GetType().GetProperty("History", BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj7);
					obj10?.GetType().GetMethod("ClearAll", BindingFlags.Instance | BindingFlags.Public)?.Invoke(obj10, null);
					methodInfo3?.Invoke(obj9, new object[1] { obj7 });
					((System.Windows.Forms.Control)obj7).Visible = true;
					Console.WriteLine("[PaintDotNet] Successfully attached blank document (800x600) to AppWorkspace!");
				}
			}
		}
		catch (Exception ex2)
		{
			LogExceptionChain("[PaintDotNet] Document init fallback exception:", ex2);
		}
	}

	private static void PreseedWpfSystemParameters()
	{
		try
		{
			try
			{
				Environment.SetEnvironmentVariable("windir", "C:\\Windows");
			}
			catch
			{
			}
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
			Assembly assembly = assemblies.FirstOrDefault((Assembly a) => a.GetName().Name == "PresentationFramework") ?? Assembly.Load("PresentationFramework");
			Assembly assembly2 = assemblies.FirstOrDefault((Assembly a) => a.GetName().Name == "WindowsBase") ?? Assembly.Load("WindowsBase");
			Type type = assembly2.GetType("MS.Win32.NativeMethods+NONCLIENTMETRICS") ?? assembly.GetType("MS.Win32.NativeMethods+NONCLIENTMETRICS");
			Type logFontType = assembly2.GetType("MS.Win32.NativeMethods+LOGFONT") ?? assembly.GetType("MS.Win32.NativeMethods+LOGFONT");
			Type type2 = assembly.GetType("System.Windows.SystemParameters");
			if (type == null || logFontType == null || type2 == null)
			{
				Console.WriteLine("[PaintDotNet] WPF preseed skipped: types not found");
				return;
			}
			object obj2 = Activator.CreateInstance(type);
			FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
			foreach (FieldInfo fieldInfo in fields)
			{
				if (fieldInfo.FieldType == logFontType)
				{
					fieldInfo.SetValue(obj2, MakeLogFont());
				}
			}
			type2.GetField("_ncm", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, obj2);
			if (type2.GetField("_cacheValid", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) is BitArray { Length: >39 } bitArray)
			{
				bitArray[39] = true;
			}
			Console.WriteLine("[PaintDotNet] WPF SystemParameters preseeded (Segoe UI, NCM cache).");
			object? MakeLogFont()
			{
				object obj3 = Activator.CreateInstance(logFontType);
				logFontType.GetField("lfFaceName")?.SetValue(obj3, "Segoe UI");
				logFontType.GetField("lfHeight")?.SetValue(obj3, -16);
				logFontType.GetField("lfWeight")?.SetValue(obj3, 400);
				return obj3;
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("[PaintDotNet] WPF preseed skipped: " + ex.GetType().Name + ": " + ex.Message);
		}
	}

	private static void LogExceptionChain(string prefix, Exception ex)
	{
		BrowserErrorReporter.Report(prefix, ex);
		for (Exception ex2 = ex; ex2 != null; ex2 = ex2.InnerException)
		{
			Console.WriteLine($"{prefix} [{ex2.GetType().FullName}]: {ex2.Message}");
			Console.WriteLine(prefix + " site: " + SiteOf(ex2));
		}
	}

	private static void PerformLayoutRecursive(System.Windows.Forms.Control root)
	{
		root.PerformLayout();
		foreach (System.Windows.Forms.Control control in root.Controls)
		{
			PerformLayoutRecursive(control);
		}
	}

	private static bool s_bookShelfFitDone;
	private static bool IsBookShelfForm(System.Windows.Forms.Form? form)
	{
		try { return form?.GetType().FullName?.Contains("BookShelf", System.StringComparison.Ordinal) == true; }
		catch { return false; }
	}

	private static void CenterInViewport(System.Windows.Forms.Form form)
	{
		// Fit every scene (generalizes the Paint-only clamp): shrink
		// oversized forms so the bottom never clips off-viewport.
		// BookShelf deferral: its MainForm_SizeChanged centers groups from
		// live content sizes, so a fit before the books arrive locks in a
		// padding computed from 0-height content (tiles below the fold).
		// Positioning always runs; the shrink waits for the deferred fit
		// once groups exist (MaybeEnsureTileRepaint).
		bool fit = !(IsBookShelfForm(form) && !s_bookShelfFitDone);
		if (fit)
		{
			int maxW = Math.Max(200, s_viewWidth - 20);
			int maxH = Math.Max(150, s_viewHeight - 40 - ChromeHeight(form));
			bool resized = false;
			if (form.Width > maxW) { form.Width = maxW; resized = true; }
			if (form.Height > maxH) { form.Height = maxH; resized = true; }
			if (resized)
			{
				form.PerformLayout();
			}
		}
		int num = Math.Max(form.Width, form.ClientSize.Width);
		int num2 = Math.Max(form.Height, form.ClientSize.Height);
		int num3 = Math.Max(300, (num > 0) ? num : 600);
		int num4 = Math.Max(200, (num2 > 0) ? num2 : 450);
		int x = Math.Max(10, (s_viewWidth - num3) / 2);
		int y = Math.Max(10, (s_viewHeight - num4) / 2);
		form.Location = new Point(x, y);
	}

	public void OnViewportChanged()
	{
		System.Windows.Forms.WasmRepaintGate.MarkGlobalDirty();
		foreach (var (appScene2, form2) in _forms)
		{
			if (appScene2 == AppScene.Paint)
			{
				form2.Width = Math.Min(1200, ViewWidth - 20);
				form2.Height = Math.Min(880, ViewHeight - 40);
				form2.Location = new Point(10, 10);
				form2.PerformLayout();
				if (form2.Controls.Count > 0)
				{
					System.Windows.Forms.Control control = form2.Controls[0];
					control.Size = form2.ClientSize;
					PerformLayoutRecursive(control);
				}
			}
			else if (_autoCentered.Contains(form2) && !_userPlaced.Contains(form2))
			{
				CenterInViewport(form2);
			}
		}
	}

	private static bool s_threadExHooked;

	// Thread-pump failures surface as a stock ThreadExceptionDialog with
	// no console record (first-chance may be capped or pre-hook): log the
	// type + first message line so every dialog self-attributes. Never
	// touches StackTrace/TargetSite (hostile frames abort the runtime).
	// Dynamic by necessity: ThreadExceptionEventArgs exists in both BCL
	// and the shim (CS0433 either way), so neither the handler signature
	// nor the member read can name it; the subscriber infers the event.
	// Side effect is the point: upstream shows the stock crash dialog
	// only when nobody subscribes; a subscriber replaces the modal (whose
	// Quit button would Environment.Exit the runtime) with this log line.
	internal static void LogThreadExceptionArgs(object? args)
	{
		try
		{
			Exception? ex = null;
			try
			{
				dynamic d = args!;
				ex = d.Exception as Exception;
			}
			catch
			{
			}

			Console.WriteLine($"[THREAD-EX] {(ex?.GetType().FullName ?? "?")}: {((ex?.Message ?? "").Split('\n')[0])}");
		}
		catch
		{
		}
	}

	public bool HasScene(AppScene scene)
	{
		try { return _forms.TryGetValue(scene, out System.Windows.Forms.Form? f) && f is not null && !f.IsDisposed; }
		catch { return false; }
	}

	public void SelectScene(AppScene scene)
	{
		System.Threading.Interlocked.Increment(ref System.Windows.Forms.WasmFrameCounters.SceneEpoch); // Perf item 4 (W-4)
		System.Windows.Forms.WasmRepaintGate.MarkGlobalDirty();
		// Once-per-process UI culture pin, hoisted from the Paint-only
		// init path: satellite-assembly probes (FileNotFound first-chance
		// on every ResourceManager fallback) are pure noise in the
		// browser, where only invariant resources ship. Idempotent.
		try
		{
			System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
		}
		catch
		{
		}
		foreach (var (appScene2, form2) in _forms.ToList())
		{
			if (!appScene2.Equals(scene))
			{
				try
				{
					form2.Dispose();
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[WinFormsAppHost] Error disposing form for {appScene2}: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
					BrowserErrorReporter.Report("form-dispose", ex);
				}
				_forms.Remove(appScene2);
				_autoCentered.Remove(form2);
				_userPlaced.Remove(form2);
			}
		}
		_activeForm = null;
		SyncForkForeground(null);
		_focusedTextBox = null;
		_focusedControl = null;
		_pressedButton = null;
		_mouseTarget = null;
		// FIX3/G6: hover/drag entries must not survive the scene switch -
		// their forms are disposed above, and a later move would raise
		// enter/leave/drag on dead controls. Nulled directly (no
		// SetHoverControl: leaving a dying form must not raise).
		_hoverControl = null;
		_hoverItem = null;
		_dragForm = null;
		_isDraggingTitle = false;
		_isDrawingOnDocument = false;
		_downToken = null;
		_lastClickToken = null;
		_hoverX = int.MinValue;
		_hoverY = int.MinValue;
		_hoverFired = false;
		_wheelRemainder = 0;
		if (!s_threadExHooked)
		{
			s_threadExHooked = true;
			System.Windows.Forms.Application.ThreadException += (_, args) => LogThreadExceptionArgs(args);
		}
		if (!s_globalFirstChanceHooked)
		{
			// Minimal first-chance trace: type + first message line
			// ONLY. Never Environment.StackTrace or ex.TargetSite
			// here: resolving hostile frames aborts the runtime.
			s_globalFirstChanceHooked = true;
			AppDomain.CurrentDomain.FirstChanceException += (sender, args) =>
			{
				try
				{
					Exception ex = args.Exception;
					string msg = (ex.Message ?? "").Split('\n')[0];
					if (msg.Contains("UITypeEditor") || msg.Contains("System.Diagnostics.Process"))
						return;
					if (!FirstChanceLogGate.ShouldLog(ex.GetType().FullName + "|" + msg))
						return;
					Console.WriteLine($"[1ST-GLOBAL] {ex.GetType().FullName}: {msg}");
				}
				catch
				{
				}
			};
		}
		_activeForm = GetOrCreateForm(scene);
		SyncForkForeground(_activeForm);
		Console.WriteLine($"[Scene] Selected {scene}, Form={_activeForm?.GetType().FullName}, Size={_activeForm?.Size}, ClientSize={_activeForm?.ClientSize}, Controls={_activeForm?.Controls.Count}");
		if (_activeForm is System.Windows.Forms.ContainerControl cc)
			Console.WriteLine($"[Scene] Scale: mode={cc.AutoScaleMode} dims={cc.AutoScaleDimensions} current={cc.CurrentAutoScaleDimensions} font={cc.Font?.Name} {cc.Font?.SizeInPoints:F2}pt");
		if (_activeForm != null)
		{
			DumpControlTree(_activeForm, 0);
			DumpGridState(_activeForm);

		}
	}

	// Data-aware grid dump (reflection: Telerik types are app-loaded):
	// distinguishes data/binding gaps (rows=0) from pure paint gaps
	// (rows>0 but nothing renders) for every grid on the form. Best
	// effort only; never breaks scene select.
	internal static void DumpGridState(System.Windows.Forms.Control root)
	{
		try
		{
			DumpGridStateCore(root);
		}
		catch
		{
		}
	}

	private static void DumpGridStateCore(System.Windows.Forms.Control c)
	{
		try
		{
			string full = c.GetType().FullName ?? "";
			if (full.Contains("RadGridView", System.StringComparison.Ordinal)
				|| full.Contains("DataGridView", System.StringComparison.Ordinal))
			{
				string detail = "?";
				try
				{
					object? mt = c.GetType().GetProperty("MasterTemplate")?.GetValue(c);
					mt ??= c;
					int rows = (int?)mt.GetType().GetProperty("RowCount")?.GetValue(mt) ?? -1;
					if (rows < 0)
					{
						object? rowList = mt.GetType().GetProperty("Rows")?.GetValue(mt);
						rows = (int?)rowList?.GetType().GetProperty("Count")?.GetValue(rowList) ?? -1;
					}
					object? colList = mt.GetType().GetProperty("Columns")?.GetValue(mt);
					int cols = (int?)colList?.GetType().GetProperty("Count")?.GetValue(colList) ?? -1;
					object? ds = null;
					try { ds = mt.GetType().GetProperty("DataSource")?.GetValue(mt); } catch { }
					detail = $"rows={rows} cols={cols} datasource={(ds == null ? "null" : ds.GetType().Name)}";
				}
				catch (System.Exception ex)
				{
					detail = "ERR " + ex.GetType().Name;
				}
				Console.WriteLine($"[GRID-STATE] {c.GetType().Name} '{c.Name}' Bounds={c.Bounds} Visible={c.Visible} {detail}");
				try
				{
					object? mt2 = c.GetType().GetProperty("MasterTemplate")?.GetValue(c); mt2 ??= c; object? rows2 = mt2.GetType().GetProperty("Rows")?.GetValue(mt2);
					System.Collections.IEnumerator? en2 = (rows2 as System.Collections.IEnumerable)?.GetEnumerator();
					if (en2 is not null && en2.MoveNext() && en2.Current is not null)
					{
						var sb = new System.Text.StringBuilder();
						int shown = 0;
						foreach (var prop in en2.Current.GetType().GetProperties())
						{
							if (shown >= 8) break;
							string tn = prop.PropertyType.FullName ?? "";
							if (!tn.StartsWith("System.Int32") && !tn.StartsWith("System.Single") && !tn.StartsWith("System.Boolean") && !tn.StartsWith("System.Drawing.Size")) continue;
							object? v = null;
							try { v = prop.GetValue(en2.Current); } catch { continue; }
							sb.Append(prop.Name).Append('=').Append(v).Append(';');
							shown++;
						}
						Console.WriteLine($"[GRID-ROW0] {sb}");
						var sb2 = new System.Text.StringBuilder();
						try
						{
							object? cells = en2.Current.GetType().GetProperty("Cells")?.GetValue(en2.Current);
							int cc = (int?)cells?.GetType().GetProperty("Count")?.GetValue(cells) ?? -1;
							sb2.Append("cells=").Append(cc).Append(';');
							System.Collections.IEnumerator? ce = (cells as System.Collections.IEnumerable)?.GetEnumerator();
							if (ce is not null && ce.MoveNext() && ce.Current is not null)
							{
								foreach (var prop in ce.Current.GetType().GetProperties())
								{
									string pn = prop.Name;
									if (pn != "Size" && pn != "Location" && pn != "Bounds" && pn != "Visible" && pn != "Value") continue;
									object? v2 = null;
									try { v2 = prop.GetValue(ce.Current); } catch { continue; }
									string vs = v2 is string s ? (s.Length > 24 ? s.Substring(0, 24) : s) : (v2?.ToString() ?? "null");
								sb2.Append(pn).Append('=').Append(vs).Append(';');
								}
							}
						}
						catch (System.Exception ex3)
						{
							sb2.Append("ERR ").Append(ex3.GetType().Name);
						}
						Console.WriteLine($"[GRID-CELL0] {sb2}");
					}
				}
				catch (System.Exception ex2)
				{
					Console.WriteLine($"[GRID-ROW0] ERR {ex2.GetType().Name}");
				}
			}
		}
		catch
		{
		}

		foreach (System.Windows.Forms.Control control in c.Controls)
		{
			DumpGridStateCore(control);
		}
	}

	// Paint-time visual dump: virtualization builds row/cell elements
	// during layout, so pre-paint reflection only sees data. Called on
	// the first frame; best effort, never throws out.
	internal static void DumpGridVisualState(System.Windows.Forms.Control root)
	{
		try { DumpGridVisualCore(root); } catch { }
	}

	private static void DumpGridVisualCore(System.Windows.Forms.Control c)
	{
		try
		{
			string full = c.GetType().FullName ?? "";
			if (full.Contains("RadGridView", System.StringComparison.Ordinal))
			{
				var sb = new System.Text.StringBuilder();
				try
				{
					object? table = c.GetType().GetProperty("TableElement")?.GetValue(c);
					DescribeVisual(table, "table", sb);
					object? firstRow = FirstVisualChild(table);
					DescribeVisual(firstRow, "row0", sb);
					object? firstCell = FirstVisualChild(firstRow);
					DescribeVisual(firstCell, "cell0", sb);
					DumpGridColumns(c);
					DumpGridTree(c);
					s_gateBudget = 0;
					DumpGridLayoutGate(c);
					ProbeGridPaint(c);
				}
				catch (System.Exception ex) { sb.Append("ERR ").Append(ex.GetType().Name); }
				Console.WriteLine($"[GRID-VIS] {c.GetType().Name} '{c.Name}' {sb}");
			}
		}
		catch { }
		foreach (System.Windows.Forms.Control control in c.Controls) DumpGridVisualCore(control);
	}

	// Tile-tree visual dump: panorama tiles are TPF elements (not
	// Controls), so the [Scene] control dump cannot see them. First-frame
	// dump answers "text missing vs zero-size" for tile content (e.g.
	// BookShelf cover-only tiles): Text present + Size>0 => paint gap;
	// Size=0 => layout/measure gap; Text empty => binding gap.
	// Best effort only; never breaks the frame.
	internal static void DumpInkCensus()
	{
		try { Console.WriteLine($"[INK] TEXT-CENSUS {WfWasm.Shim.TextRenderer.DumpCensus()}"); } catch { }
		try
		{
			string gdiCensus = System.Windows.Forms.WasmGdiText.DumpGdiPaintCensus();
			Console.WriteLine("[INK] GDI-CENSUS " + gdiCensus);
		}
		catch { }
		try
		{
			string dsCensus = System.Drawing.Graphics.DumpDrawStringCensus();
			Console.WriteLine("[INK] DS-CENSUS " + dsCensus);
		}
		catch { }
	}
	internal static void DumpTileVisualState(System.Windows.Forms.Control root)
	{
		try { s_tileLines = 0; DumpTileVisualCore(root); } catch { }
		try { Console.WriteLine($"[TILE-VIS] TEXT-CENSUS {WfWasm.Shim.TextRenderer.DumpCensus()}"); } catch { }
		try
		{
			string gdiCensus = System.Windows.Forms.WasmGdiText.DumpGdiPaintCensus();
			Console.WriteLine("[TILE-VIS] GDI-CENSUS " + gdiCensus);
		}
		catch { }
		try
		{
			string dsCensus = System.Drawing.Graphics.DumpDrawStringCensus();
			Console.WriteLine("[TILE-VIS] DS-CENSUS " + dsCensus);
		}
		catch { }
	}

	private static int s_tileLines;
	private static bool s_tileLateDumpDone;
	// Working-vs-broken diff experiment: a browser resize inks tile text
	// while steady-state repaints don't. Re-arm the one-shot tile
	// diagnostics on form resize (debounced) so the post-resize dump,
	// census, subs and paint trace capture the WORKING state for diffing.
	private static bool s_tileResizeArmed;
	private static long s_tileRearmTicks;
	internal static void ArmTileRedumpOnResize(System.Windows.Forms.Control c)
	{
		try
		{
			if (s_tileResizeArmed) return;
			s_tileResizeArmed = true;
			System.Windows.Forms.Form? f = c.FindForm();
			if (f is null) return;
			f.SizeChanged += (_, _) =>
			{
				try
				{
					long now = System.DateTime.UtcNow.Ticks;
					if (now - s_tileRearmTicks < 600_000_000L) return;
					s_tileRearmTicks = now;
					s_tileLateDumpDone = false;
					s_panoPaintTrace = 0;
					s_paintSubs = 0;
					s_paintFire.Clear();
					try { Console.WriteLine("[TILE-VIS] redump-armed on resize"); } catch { }
				}
				catch { }
			};
		}
		catch { }
	}

	private static void DumpTileVisualCore(System.Windows.Forms.Control c)
	{
		try
		{
			string full = c.GetType().FullName ?? "";
			if (full.Contains("Panorama", System.StringComparison.Ordinal))
			{
				Console.WriteLine($"[TILE-VIS] {c.GetType().Name} '{c.Name}' Bounds={c.Bounds} Visible={c.Visible}");
				object?[] roots = new object?[]
				{
					SafeGetObj(c, "PanoramaElement"),
					SafeGetObj(c, "RootElement"),
					SafeGetObj(c, "Groups"),
					SafeGetObj(c, "Items"),
				};
				foreach (object? r in roots)
					if (r is not null) DumpTileElement(r, 1);
			}
		}
		catch { }
		foreach (System.Windows.Forms.Control control in c.Controls) DumpTileVisualCore(control);
	}

	private static object? SafeGetObj(object? o, string name)
	{
		try { return o?.GetType().GetProperty(name)?.GetValue(o); }
		catch { return null; }
	}

	// Presence probe for the late tile re-dump: true once the panorama
	// element carries groups (books arrived). Reflection-only, no layout.
	private static bool PanoramaHasGroups(object? panoramaControl)
	{
		try
		{
			object? el = SafeGetObj(panoramaControl, "PanoramaElement");
			el ??= SafeGetObj(panoramaControl, "RootElement");
			object? groups = SafeGetObj(el, "Groups");
			if (groups is System.Collections.IEnumerable en)
			{
				foreach (object? g in en) return true;
			}
			return false;
		}
		catch { return false; }
	}

	// Tile text inks only under a full clip, but the steady-state
	// panorama repaints carry partial (cover-arrival) clips, so tile
	// text added after first paint stays blank until a full repaint
	// (resize-proven). One-shot: repaint-gate the panorama whole the
	// first time its groups are non-empty so tiles paint once with
	// settled layout. Mirrors desktop (adding children invalidates).
	// NOTE: plain Control.Invalidate is a handleless no-op headless
	// (the rewriter redirects it only in transformed assemblies, not
	// here), so this must go through WasmRepaintGate directly.
	internal static bool MaybeEnsureTileRepaint(System.Windows.Forms.Control c)
	{
		try
		{
			if (!PanoramaHasGroups(c)) return false;
			try { System.Windows.Forms.WasmRepaintGate.RequestRepaint(c, true); } catch { }
			try { Console.WriteLine("[TILE-VIS] ensure-paint invalidated " + c.GetType().Name); } catch { }
			// Deferred fit (BookShelf tile text below the fold): the app
			// centers its groups in MainForm_SizeChanged from live content
			// sizes. A host fit before the books arrive computes padding
			// from unmeasured (0-height) content and parks the tiles below
			// the fold permanently (Padding.Top=283). Creation/viewport
			// fits are therefore skipped for BookShelf until the groups
			// exist; this one-shot applies the fit now (content measurable)
			// so SizeChanged computes the correct padding, then repaints.
			try
			{
				System.Windows.Forms.Form? bf = c.FindForm();
				if (!s_bookShelfFitDone && IsBookShelfForm(bf))
				{
					s_bookShelfFitDone = true;
					CenterInViewport(bf!);
					Console.WriteLine("[TILE-VIS] deferred-fit applied BookShelf size=" + bf!.Size);
					try { System.Windows.Forms.WasmRepaintGate.RequestRepaint(c, true); } catch { }
				}
			}
			catch { }
			return true;
		}
		catch { return false; }
	}

	private static void DumpTileList(object? list, int depth)
	{
		if (list is System.Collections.IEnumerable en && list is not string)
		{
			int shown = 0;
			foreach (object? child in en)
			{
				if (child is null || shown >= 12 || s_tileLines >= 300) break;
				DumpTileElement(child, depth);
				shown++;
			}
		}
	}

	private static void DumpTileElement(object? element, int depth)
	{
		if (element is null || depth > 7 || s_tileLines >= 300) return;
		if (element is System.Collections.IEnumerable && element is not string)
		{
			DumpTileList(element, depth);
			return;
		}
		try
		{
			var sb = new System.Text.StringBuilder();
			sb.Append(' ', depth * 2);
			sb.Append('[').Append(element.GetType().Name).Append(']');
			DescribeVisual(element, "el", sb);
			sb.Append("Arrange=").Append(SafeProp(element, "IsArrangeValid")).Append(';');
			sb.Append("Measure=").Append(SafeProp(element, "IsMeasureValid")).Append(';');
			sb.Append("En=").Append(SafeProp(element, "Enabled")).Append(';');
			sb.Append("SP=").Append(SafeProp(element, "ShouldPaint")).Append(';');
			sb.Append("DT=").Append(SafeProp(element, "DrawText")).Append(';');
			sb.Append("Img=").Append(SafeGetObj(element, "Image") is null ? "null" : "set").Append(';');
			sb.Append("PR=").Append(SafeSubProp(element, "TextParams", "paintingRectangle")).Append(';');
			sb.Append("FNT=").Append(SafeProp(element, "Font")).Append(';');
			sb.Append("TPF=").Append(SafeSubProp(element, "TextParams", "font")).Append(';');
			sb.Append("CBR=").Append(SafeProp(element, "ControlBoundingRectangle")).Append(';');
			sb.Append("TT=").Append(SafeProp(element, "TotalTransform")).Append(';');
			sb.Append("SPC=").Append(SafeProp(element, "ShouldPaintChildren")).Append(';');
			sb.Append("IMPL=").Append(SafeFieldType(element, "textPrimitiveImpl")).Append(';');
			sb.Append("LAYC=").Append(SafeProp(element, "LayoutableChildrenCount")).Append(';');
			sb.Append("ZC2=").Append(SafeZOrderedCount(element)).Append(';');
			string text = SafeProp(element, "Text");
			if (text.Length > 48) text = text.Substring(0, 48);
			sb.Append("Text='").Append(text.Replace('\r', ' ').Replace('\n', ' ')).Append("';");
			Console.WriteLine($"[TILE-VIS] {sb}");
			s_tileLines++;
			object?[] kids = new object?[]
			{
				SafeGetObj(element, "Children"),
				SafeGetObj(element, "Groups"),
				SafeGetObj(element, "Items"),
			};
			int before = s_tileLines;
			foreach (object? k in kids)
				DumpTileList(k, depth + 1);
			int total = ChildCount(element);
			int shown = s_tileLines - before;
			if (total > shown) Console.WriteLine($"[TILE-VIS] {new string(' ', depth * 2)}... +{total - shown} more");
		}
		catch { }
	}


	private static string SafeSubProp(object? o, string outer, string inner)
	{
		try
		{
			object? mid = SafeGetObj(o, outer);
			if (mid is null) return "null";
			object? v = mid.GetType().GetProperty(inner)?.GetValue(mid);
			v ??= mid.GetType().GetField(inner, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(mid);
			return v?.ToString() ?? "null";
		}
		catch { return "ERR"; }
	}

	private static string SafeFieldType(object? o, string field)
	{
		try
		{
			object? v = o?.GetType().GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)?.GetValue(o);
			return v?.GetType().Name ?? "null";
		}
		catch { return "ERR"; }
	}

	// Z-ordered child count through the exact path PaintChildren uses
	// (GetChildren(ZOrdered) yield iterator, not the Children list).
	// A yield divergence headless would empty the paint walk while
	// layout (Children-based) stays healthy.
	private static string SafeZOrderedCount(object? o)
	{
		try
		{
			if (o is null) return "null";
			System.Reflection.MethodInfo? mi = null;
			for (System.Type? t = o.GetType(); t is not null && mi is null; t = t.BaseType)
				mi = t.GetMethod("GetChildren", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			if (mi is null) return "no-m";
			System.Reflection.ParameterInfo[] ps = mi.GetParameters();
			if (ps.Length != 1) return "no-1p";
			object arg = System.Enum.Parse(ps[0].ParameterType, "ZOrdered");
			object? res = mi.Invoke(o, new object?[] { arg });
			if (res is not System.Collections.IEnumerable en) return "no-en";
			int n = 0;
			foreach (object? _ in en) { n++; if (n > 20) break; }
			return n.ToString();
		}
		catch { return "ERR"; }
	}

	private static string SafeProp(object? o, string name)
	{
		try
		{
			if (o is null) return "?";
			object? v = o.GetType().GetProperty(name)?.GetValue(o);
			return v?.ToString() ?? "null";
		}
		catch { return "ERR"; }
	}

	private static int ChildCount(object? element)
	{
		try
		{
			object? kids = element?.GetType().GetProperty("Children")?.GetValue(element);
			return (int?)kids?.GetType().GetProperty("Count")?.GetValue(kids) ?? -1;
		}
		catch { return -2; }
	}

	// Column widths + template flags: answers whether BestFit/AutoSize
	// produced widths (layout ran) or columns stayed 0 (layout broken).
	private static void DumpGridColumns(System.Windows.Forms.Control c)
	{
		try
		{
			var sb = new System.Text.StringBuilder();
			object? cols = c.GetType().GetProperty("Columns")?.GetValue(c);
			int n = (int?)cols?.GetType().GetProperty("Count")?.GetValue(cols) ?? -1;
			sb.Append("cols=").Append(n).Append(';');
			System.Collections.IEnumerator? en = (cols as System.Collections.IEnumerable)?.GetEnumerator();
			int i = 0;
			while (en is not null && en.MoveNext() && i < 15)
			{
				object? col = en.Current;
				sb.Append('[').Append(i).Append(' ').Append(SafeProp(col, "Name"));
				sb.Append(" W=").Append(SafeProp(col, "Width")).Append(" Min=").Append(SafeProp(col, "MinWidth"));
				sb.Append(" Max=").Append(SafeProp(col, "MaxWidth")).Append(" Vis=").Append(SafeProp(col, "IsVisible")).Append(']');
				i++;
			}
			object? mt = c.GetType().GetProperty("MasterTemplate")?.GetValue(c);
			sb.Append("tmpl[AutoSize=").Append(SafeProp(mt ?? c, "AutoSizeColumnsMode"));
			sb.Append(" RowH=").Append(SafeProp(mt, "RowHeight")).Append(" Grp=").Append(SafeProp(mt, "EnableGrouping")).Append("];");
			Console.WriteLine($"[GRID-COLS] {c.GetType().Name} '{c.Name}' {sb}");
		}
		catch (System.Exception ex) { Console.WriteLine($"[GRID-COLS] ERR {ex.GetType().Name}"); }
	}

	private static int s_gridTreeBudget;

	// Full visual subtree with type names: identifies what FirstVisualChild
	// actually grabbed (row0 full-table is either header or container).
	private static void WalkGridTree(object? el, int depth, System.Text.StringBuilder sb)
	{
		if (el is null || depth > 5 || s_gridTreeBudget > 120) return;
		s_gridTreeBudget++;
		try
		{
			sb.Append('[').Append(depth).Append(' ').Append(el.GetType().Name);
			sb.Append(' ').Append(SafeProp(el, "Bounds")).Append(' ').Append(SafeProp(el, "Visibility"));
			sb.Append(" kids=").Append(ChildCount(el)).Append(']');
			object? kids = el.GetType().GetProperty("Children")?.GetValue(el);
			System.Collections.IEnumerator? en = (kids as System.Collections.IEnumerable)?.GetEnumerator();
			while (en is not null && en.MoveNext()) WalkGridTree(en.Current, depth + 1, sb);
		}
		catch { sb.Append("[ERR]"); }
	}

	private static void DumpGridTree(System.Windows.Forms.Control c)
	{
		try
		{
			object? table = c.GetType().GetProperty("TableElement")?.GetValue(c);
			var sb = new System.Text.StringBuilder();
			s_gridTreeBudget = 0;
			WalkGridTree(table, 0, sb);
			Console.WriteLine($"[GRID-TREE] {c.GetType().Name} '{c.Name}' {sb}");
		}
		catch (System.Exception ex) { Console.WriteLine($"[GRID-TREE] ERR {ex.GetType().Name}"); }
	}


	private static string SafeField(object? o, string name)
	{
		try
		{
			if (o is null) return "?";
			System.Reflection.FieldInfo? f = null;
			for (System.Type? t = o.GetType(); t is not null && f is null; t = t.BaseType)
				f = t.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly);
			object? v = f?.GetValue(o);
			return v?.ToString() ?? "null";
		}
		catch { return "ERR"; }
	}

	// Layout-gate dump: CanExecuteLayoutOperation = state==Loaded(4) &&
	// suspend==0. If state never reaches Loaded on WASM, the arrange
	// queue never drains (all cells X=0/Y=0).
	private static void DumpGridLayoutGate(System.Windows.Forms.Control c)
	{
		try
		{
			var sb = new System.Text.StringBuilder();
			object? table = c.GetType().GetProperty("TableElement")?.GetValue(c);
			WalkLayoutGate(table, 0, sb);
			Console.WriteLine($"[GRID-GATE] {c.GetType().Name} '{c.Name}' {sb}");
		}
		catch (System.Exception ex) { Console.WriteLine($"[GRID-GATE] ERR {ex.GetType().Name}"); }
	}

	private static int s_gateBudget;

	private static void WalkLayoutGate(object? el, int depth, System.Text.StringBuilder sb)
	{
		if (el is null || depth > 4 || s_gateBudget > 40) return;
		s_gateBudget++;
		try
		{
			sb.Append('[').Append(depth).Append(' ').Append(el.GetType().Name);
			sb.Append(" state=").Append(SafeField(el, "state")).Append(" susp=").Append(SafeField(el, "layoutSuspendCount"));
			sb.Append(" treeNull=").Append(SafeField(el, "elementTree") == "null" ? "Y" : "n");
			sb.Append(" measQ?=").Append(SafeField(el, "layoutManager") == "null" ? "noMgr" : "mgr").Append(']');
			object? kids = el.GetType().GetProperty("Children")?.GetValue(el);
			System.Collections.IEnumerator? en = (kids as System.Collections.IEnumerable)?.GetEnumerator();
			int i = 0;
			while (en is not null && en.MoveNext() && i < 6) { WalkLayoutGate(en.Current, depth + 1, sb); i++; }
		}
		catch { sb.Append("[ERR]"); }
	}


	private static readonly System.Collections.Generic.List<object> s_paintHooks = new();

	// Paint-attribution probe: records each grid element's paint inputs
	// (layoutOffset, Transform DX/DY, BoundingRectangle) and subscribes
	// ElementPainted to learn which elements actually execute paint.
	private static void ProbeGridPaint(System.Windows.Forms.Control c)
	{
		try
		{
			object? table = c.GetType().GetProperty("TableElement")?.GetValue(c);
			var sb = new System.Text.StringBuilder();
			s_gridTreeBudget = 0;
			WalkPaintProbe(table, 0, sb, c.Name ?? "?");
			Console.WriteLine($"[GRID-PAINT] {c.GetType().Name} '{c.Name}' {sb}");
		}
		catch (System.Exception ex) { Console.WriteLine($"[GRID-PAINT] ERR {ex.GetType().Name}"); }
	}

	private static void WalkPaintProbe(object? el, int depth, System.Text.StringBuilder sb, string grid)
	{
		if (el is null || depth > 6 || s_gridTreeBudget > 140) return;
		s_gridTreeBudget++;
		try
		{
			string tn = el.GetType().Name;
			sb.Append('[').Append(depth).Append(' ').Append(tn);
			sb.Append(" off=").Append(SafeField(el, "layoutOffset"));
				object? tr = null;
				try { tr = el.GetType().GetProperty("Transform")?.GetValue(el); } catch { }
				sb.Append(" dx=").Append(tr is null ? "?" : SafeProp(tr, "DX")).Append(" dy=").Append(tr is null ? "?" : SafeProp(tr, "DY"));
			sb.Append(" bbox=").Append(SafeProp(el, "BoundingRectangle"));
			sb.Append(" cbbox=").Append(SafeProp(el, "ControlBoundingRectangle"));
			sb.Append(" clip=").Append(SafeProp(el, "ClipDrawing")).Append(']');
			try
			{
				var evt = el.GetType().GetEvent("ElementPainted");
				if (evt is not null)
				{
					System.Windows.Forms.PaintEventHandler h = (s, e) =>
					{
						if (!PaintDiagnosticsEnabled) return;
					try { Console.WriteLine($"[GRID-PAINTED] {grid} {tn} clip={e.ClipRectangle}"); } catch { }
					};
					evt.AddEventHandler(el, h);
					s_paintHooks.Add(h);
				}
			}
			catch { }
			object? kids = el.GetType().GetProperty("Children")?.GetValue(el);
			System.Collections.IEnumerator? en = (kids as System.Collections.IEnumerable)?.GetEnumerator();
			int limit = depth >= 4 ? 2 : 99;
			int ci = 0;
			while (en is not null && en.MoveNext() && ci < limit)
			{
				try
				{
					var spc = el.GetType().GetMethod("ShouldPaintChild", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
					object? verdict = spc?.Invoke(el, new object?[] { en.Current });
					sb.Append("{spc:").Append(en.Current?.GetType().Name).Append('=').Append(verdict).Append('}');
				}
				catch { }
				WalkPaintProbe(en.Current, depth + 1, sb, grid);
				ci++;
			}
		}
		catch { sb.Append("[ERR]"); }
	}


	// Dock/tab text probe: distinguishes empty tab data (binding gap)
	// from present-but-unpainted text (paint gap) for the missing
	// Projects/Bugs/Features/Details/General captions.
	internal static void DumpDockTabText(System.Windows.Forms.Control root)
	{
		try
		{
			var sb = new System.Text.StringBuilder();
			WalkDockTabs(root, 0, sb);
			Console.WriteLine($"[DOCK-TABS] {sb}");
		}
		catch (System.Exception ex) { Console.WriteLine($"[DOCK-TABS] ERR {ex.GetType().Name}"); }
	}

	private static void WalkDockTabs(object? o, int depth, System.Text.StringBuilder sb)
	{
		if (o is null || depth > 6 || sb.Length > 6000) return;
		try
		{
			string tn = o.GetType().Name;
			if (tn == "DocumentTabStrip" || tn == "TabStripPanel")
			{
				try
				{
					object? se = o.GetType().GetProperty("TabStripElement")?.GetValue(o);
					var lsb = new System.Text.StringBuilder(); lsb.Append("{stripel:").Append(se?.GetType().Name);
					object? items = se?.GetType().GetProperty("Items")?.GetValue(se);
					System.Collections.IEnumerator? en = (items as System.Collections.IEnumerable)?.GetEnumerator();
					int ii = 0;
					while (en is not null && en.MoveNext() && ii < 6)
					{
						lsb.Append("[itm:").Append(SafeProp(en.Current, "Text")).Append("/sel=").Append(SafeProp(en.Current, "IsSelected")).Append("/vis=").Append(SafeProp(en.Current, "Visibility")).Append("/b=").Append(SafeProp(en.Current, "Bounds"));
						lsb.Append("/st:").Append(SafeProp(en.Current, "DrawFill")).Append(",").Append(SafeProp(en.Current, "DrawText")).Append(",").Append(SafeProp(en.Current, "DrawBorder"));
						lsb.Append("/c:").Append(SafeProp(en.Current, "BackColor")).Append(",").Append(SafeProp(en.Current, "ForeColor")).Append(",").Append(SafeProp(en.Current, "Font"));
						lsb.Append("/g:").Append(SafeProp(en.Current, "BackColor2")).Append(",").Append(SafeProp(en.Current, "BackColor3")).Append(",").Append(SafeProp(en.Current, "BackColor4")).Append(",").Append(SafeProp(en.Current, "GradientStyle")).Append(",").Append(SafeProp(en.Current, "NumberOfColors"));
						lsb.Append("/th:").Append(SafeProp(en.Current, "ThemeRole")).Append(",").Append(SafeProp(en.Current, "ThemeEffectiveType")).Append(",").Append(SafeProp(en.Current, "ElementState"));
						lsb.Append("/kids").Append(ChildCount(en.Current)).Append(':');
						try
						{
							object? ik = en.Current?.GetType().GetProperty("Children")?.GetValue(en.Current);
							System.Collections.IEnumerator? ien = (ik as System.Collections.IEnumerable)?.GetEnumerator();
							int ikn = 0;
							while (ien is not null && ien.MoveNext() && ikn < 6)
							{
								lsb.Append(ien.Current?.GetType().Name).Append('(').Append(SafeProp(ien.Current, "Visibility")).Append(',').Append(SafeProp(ien.Current, "BackColor")).Append(',').Append(SafeProp(ien.Current, "Bounds")).Append(");");
								ikn++;
							}
						}
						catch { lsb.Append("kERR;"); }
						try
						{
							object? bgs = en.Current?.GetType().GetProperty("BackgroundShape")?.GetValue(en.Current);
							lsb.Append("/bg:").Append(bgs?.GetType().Name ?? "null");
							object? bgi = bgs?.GetType().GetProperty("Image")?.GetValue(bgs);
							if (bgi is System.Drawing.Image bmi)
							{
								lsb.Append(',').Append(bmi.Width).Append('x').Append(bmi.Height);
								try { lsb.Append(',').Append((bmi as System.Drawing.Bitmap)?.GetPixel(0, 0).ToString() ?? "nopx"); }
								catch { lsb.Append(",pxERR"); }
							}
							else lsb.Append(",noimg");
							lsb.Append(',').Append(SafeProp(en.Current, "BackgroundShapePaintMode"));
						}
						catch { lsb.Append("/bg:ERR"); }
						try
						{
							Type? tt = en.Current?.GetType();
							System.Reflection.Assembly? coreAsm = null;
							while (tt != null) { if (tt.Assembly.GetName().Name == "Telerik.WinControls") { coreAsm = tt.Assembly; break; } tt = tt.BaseType; }
							lsb.Append("/theme:core=").Append(coreAsm == null ? "null" : "ok");
							Type? trs = coreAsm?.GetType("Telerik.WinControls.ThemeResolutionService");
							lsb.Append(",trs=").Append(trs == null ? "null" : "ok");
							object? theme = trs?.GetMethod("GetTheme", new Type[] { typeof(string) })?.Invoke(null, new object[] { "TelerikMetro" });
							lsb.Append(",theme=").Append(theme == null ? "null" : theme.GetType().Name);
							object? selrepo = theme?.GetType().GetMethod("FindRepository", new Type[] { typeof(string) })?.Invoke(theme, new object[] { "PageViewItemSelectedImageShape" });
							lsb.Append(",selrepo=").Append(selrepo == null ? "null" : "ok");
							object? bgset = selrepo?.GetType().GetMethod("FindSetting", new Type[] { typeof(string) })?.Invoke(selrepo, new object[] { "BackgroundShape" });
							lsb.Append(",set=").Append(bgset == null ? "null" : "ok");
							object? bgval = bgset?.GetType().GetProperty("Value")?.GetValue(bgset);
							lsb.Append(",val=").Append(bgval == null ? "null" : bgval.GetType().Name);
							if (bgval != null)
							{
								object? thimg = bgval.GetType().GetProperty("Image")?.GetValue(bgval);
								if (thimg is System.Drawing.Image tbi) { lsb.Append(",img=").Append(tbi.Width).Append('x').Append(tbi.Height); try { lsb.Append(',').Append((tbi as System.Drawing.Bitmap)?.GetPixel(0, 0).ToString() ?? "nopx"); } catch { lsb.Append(",pxERR"); } }
								else lsb.Append(",imgnull");
							}
						}
						catch (Exception tex) { lsb.Append("/theme:ERR-").Append(tex.GetType().Name); }
					try
					{
						Type? tt2 = en.Current?.GetType();
						System.Reflection.Assembly? c2 = null;
						while (tt2 != null) { if (tt2.Assembly.GetName().Name == "Telerik.WinControls") { c2 = tt2.Assembly; break; } tt2 = tt2.BaseType; }
						if (c2 != null)
						{
							lsb.Append("/gt:");
							string[] names = new string[] { "Telerik.WinControls.RadImageShape", "Telerik.WinControls.RadImageShape, Telerik.WinControls", "Telerik.WinControls.RadImageShape, Telerik.WinControls, Version=2026.3.1008.462, Culture=neutral, PublicKeyToken=5bb2a467cbec794e" };
							for (int gi = 0; gi < names.Length; gi++) { try { lsb.Append(c2.GetType(names[gi], false, false) == null ? "null;" : "ok;"); } catch (Exception gex) { lsb.Append("EX-").Append(gex.GetType().Name).Append(";"); } }
						}
						else lsb.Append("/gt:nocore");
					}
					catch (Exception gex2) { lsb.Append("/gt:ERR-").Append(gex2.GetType().Name); }
						lsb.Append("]");
						ii++;
					}
					lsb.Append("/stripkids").Append(ChildCount(se)).Append(':');
				try
				{
					object? sk = se?.GetType().GetProperty("Children")?.GetValue(se);
					System.Collections.IEnumerator? sen = (sk as System.Collections.IEnumerable)?.GetEnumerator();
					int skn = 0;
					while (sen is not null && sen.MoveNext() && skn < 10)
					{
						lsb.Append(sen.Current?.GetType().Name).Append('(').Append(SafeProp(sen.Current, "Visibility")).Append(',').Append(SafeProp(sen.Current, "BackColor")).Append(',').Append(SafeProp(sen.Current, "Bounds")).Append(");");
						skn++;
					}
				}
				catch { lsb.Append("skERR;"); }
				lsb.Append("}");
					Console.WriteLine($"[DOCK-STRIP] {lsb}");
				}
				catch (System.Exception exx3) { sb.Append("{stripelERR:").Append(exx3.GetType().Name).Append("}"); }
			}
			if (o is System.Windows.Forms.Control cc && (tn.Contains("ToolWindow") || tn.Contains("DocumentWindow") || tn == "RadDock"))
			{
				sb.Append("{win:").Append(cc.Name).Append(" h=").Append(cc.IsHandleCreated).Append(" hwnd=").Append(cc.Handle.ToInt64());
				try
				{
					var wt = cc.GetType().GetProperty("WindowText", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
					sb.Append(" wtxt=").Append(wt?.GetValue(cc) ?? "null");
					string? raw = null;
					for (System.Type? tt = cc.GetType(); tt is not null; tt = tt.BaseType)
					{
						var f = tt.GetField("_text", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
						if (f is not null) { raw = (string?)f.GetValue(cc); break; }
					}
					sb.Append(" _text=").Append(raw ?? "null");
					try
					{
						var ct = typeof(System.Windows.Forms.Control);
						var kf = ct.GetField("s_cacheTextCountProperty", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
						var props = ct.GetProperty("Properties", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(cc);
						var get = props?.GetType().GetMethod("GetValueOrDefault", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
						var gm = get?.MakeGenericMethod(typeof(int));
						object? cnt = gm?.Invoke(props, new object?[] { kf?.GetValue(null), 0 });
						sb.Append(" ctc=").Append(cnt);
						var cti = ct.GetProperty("CacheTextInternal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(cc);
						sb.Append(" cti=").Append(cti);
					}
					catch (System.Exception exx2) { sb.Append(" ctcERR=").Append(exx2.GetType().Name); }
				}
				catch (System.Exception exx) { sb.Append(" winERR=").Append(exx.GetType().Name); }
				sb.Append('}');
			}
			if (tn.Contains("TabStrip") || tn.Contains("DockWindow") || tn.Contains("SplitContainer") || tn.Contains("TabItem") || tn.Contains("DocumentTabStrip") || tn.Contains("ToolWindow") || tn == "RadDock" || tn == "TabPanel" || tn == "DockWindowCollection" || tn == "CaptionElement" || tn.Contains("Caption") || tn.Contains("PageViewStrip"))
			{
				sb.Append('[').Append(depth).Append(' ').Append(tn);
				sb.Append(" txt=").Append(SafeProp(o, "Text")).Append(" cap=").Append(SafeProp(o, "Caption"));
				sb.Append(" vis=").Append(SafeProp(o, "Visibility")).Append((char)93);
				try
				{
					foreach (var prop in o.GetType().GetProperties())
					{
						string pn = prop.Name;
						if (!pn.Contains("Text") && !pn.Contains("Title") && !pn.Contains("Caption") && !pn.Contains("Item") && !pn.Contains("Panel") && !pn.Contains("Strip") && !pn.Contains("Window") && !pn.Contains("Associated")) continue;
						if (prop.GetIndexParameters().Length > 0) continue;
						object? v = null;
						try { v = prop.GetValue(o); } catch { continue; }
						if (v is string s) { sb.Append(pn).Append('=').Append(s.Length > 40 ? s.Substring(0, 40) : s).Append(';'); }
						else if (v is System.Collections.IEnumerable e && v is not string)
						{
							int n = 0; var ie = e.GetEnumerator();
							sb.Append(pn).Append(':');
							while (n < 6 && ie.MoveNext()) { sb.Append(SafeProp(ie.Current, "Text")).Append('/').Append(SafeProp(ie.Current, "Title")).Append(','); n++; }
							sb.Append(';');
						}
						else if (v is not null) { sb.Append(pn).Append('=').Append(v.GetType().Name).Append(';'); }
					}
				}
				catch { }
			}
			if (o is System.Windows.Forms.Control c)
				foreach (System.Windows.Forms.Control k in c.Controls) WalkDockTabs(k, depth + 1, sb);
			try
			{
				object? kids = o.GetType().GetProperty("Children")?.GetValue(o);
				System.Collections.IEnumerator? en2 = (kids as System.Collections.IEnumerable)?.GetEnumerator();
				while (en2 is not null && en2.MoveNext()) WalkDockTabs(en2.Current, depth + 1, sb);
			}
			catch { }
		}
		catch { }
	}
	private static object? FirstVisualChild(object? element)
	{
		try
		{
			object? kids = element?.GetType().GetProperty("Children")?.GetValue(element);
			System.Collections.IEnumerator? en = (kids as System.Collections.IEnumerable)?.GetEnumerator();
			if (en is not null && en.MoveNext()) return en.Current;
		} catch { }
		return null;
	}

	private static void DescribeVisual(object? element, string tag, System.Text.StringBuilder sb)
	{
		if (element is null) { sb.Append(tag).Append("=null;"); return; }
		sb.Append(tag).Append('[');
		try
		{
			foreach (var prop in element.GetType().GetProperties())
			{
				string pn = prop.Name;
				if (pn != "Size" && pn != "Location" && pn != "Bounds" && pn != "Visible" && pn != "Visibility") continue;
				object? v = null;
				try { v = prop.GetValue(element); } catch { continue; }
				sb.Append(pn).Append('=').Append(v).Append(';');
			}
			object? kids2 = element.GetType().GetProperty("Children")?.GetValue(element);
			int cc = (int?)kids2?.GetType().GetProperty("Count")?.GetValue(kids2) ?? -1;
			sb.Append("kids=").Append(cc).Append(';');
		}
		catch (System.Exception ex) { sb.Append("ERR ").Append(ex.GetType().Name); }
		sb.Append("];");
	}

	private static void DumpControlTree(System.Windows.Forms.Control c, int indent)
	{
		string value = new string(' ', indent * 2);
		Console.WriteLine($"{value}- {c.GetType().Name} '{c.Name}' Bounds={c.Bounds} Visible={c.Visible} Dock={c.Dock} Children={c.Controls.Count}");
		if (c is System.Windows.Forms.ToolStrip toolStrip)
		{
			foreach (System.Windows.Forms.ToolStripItem item in toolStrip.Items)
			{
				Console.WriteLine($"{value}   [Item] {item.GetType().Name} '{item.Name}' Text='{item.Text}' Bounds={item.Bounds} Visible={item.Visible}");
			}
		}
		foreach (System.Windows.Forms.Control control in c.Controls)
		{
			DumpControlTree(control, indent + 1);
		}
	}

	// Host caption: 30px for captioned forms, none for borderless or
	// custom-chrome ones (FormBorderStyle.None, RadForm). The fork seeds
	// emulated GWL_STYLE from CreateParams at handle creation, so the
	// WS_CAPTION bit is a truthful signal next to the managed property.
	// Telerik RadForm/RadRibbonForm/ShapedForm keep WS_CAPTION in style but
	// suppress the non-client area via NCCALCSIZE (native client fills the
	// window); the style bit cannot see that, so detect the custom-chrome
	// family by base-type name (BugTracker native parity: no host caption).
	internal static int ChromeHeight(System.Windows.Forms.Form form)
	{
		if (form.FormBorderStyle == System.Windows.Forms.FormBorderStyle.None)
			return 0;
		for (System.Type? t = form.GetType(); t is not null; t = t.BaseType)
		{
			string tn = t.Name;
			if (tn.Contains("RadForm", System.StringComparison.Ordinal)
				|| tn.Contains("ShapedForm", System.StringComparison.Ordinal))
				return 0;
			if (tn == "Form")
				break;
		}
		try
		{
			int style = System.Windows.Forms.WasmWindowLongs.GetWindowStyle(form.Handle);
			if (style != 0 && (style & 0x00C00000) != 0x00C00000)
				return 0;
		}
		catch
		{
		}
		return 30;
	}

	public void Paint(ICanvas canvas)
	{
		ArgumentNullException.ThrowIfNull(canvas, "canvas");
		// Drain posted layout work first (desktop drains BeginInvoke
		// posts on the message loop before painting); layout invalidates
		// as it runs, and the drain below collects those marks for this
		// same frame.
		// Perf item 4 S5 split: time the layout drain separately so the
		// frame sample can tell layout cost (drainS) from raster cost
		// (recS minus drainS). One bool read when the timeline is off.
		bool perfDrain = System.Windows.Forms.WasmFrameCounters.PerfEnabled;
		long tDrain0 = perfDrain ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
		int layoutDrained = System.Windows.Forms.WasmLayoutQueue.Drain();
		long layoutTicks = perfDrain ? System.Diagnostics.Stopwatch.GetTimestamp() - tDrain0 : 0;
		if (layoutDrained > 0)
		{
			// Throttled: first 3 drains, then at most one line per 5 s,
			// plus on any drain where the lifetime fault count moved.
			// A layout callback faulting per frame (TPF reposts after
			// exceptions) must stay visible without flooding frames,
			// and post-boot layout activity must not go silent the way
			// a pure count throttle hid it (rev60 ZZ investigation).
			int layoutLog = System.Threading.Interlocked.Increment(ref s_layoutLogCount);
			long faulted = System.Windows.Forms.WasmLayoutQueue.TotalFaulted;
			long nowTick = Environment.TickCount64;
			if (layoutLog <= 3 || faulted != s_layoutFaultedSeen || nowTick - s_layoutLogTick >= 5000)
			{
				s_layoutFaultedSeen = faulted;
				s_layoutLogTick = nowTick;
				Console.WriteLine($"[LayoutQueue] drained={layoutDrained} posted={System.Windows.Forms.WasmLayoutQueue.TotalPosted} drainedTotal={System.Windows.Forms.WasmLayoutQueue.TotalDrained} faulted={faulted} pending={System.Windows.Forms.WasmLayoutQueue.PendingCount}");
			}
		}
		// Strict GPU mode (opt-in): fail closed before any drawing until a
		// GPU render target exists. Capability detection arrives with the
		// GPU backend; until then no target qualifies. Switch defaults off.
		if (AppContext.TryGetSwitch("WfWasm.RequireGpuRendering", out bool requireGpu) && requireGpu)
			throw new NotSupportedException("GPU rendering is required (WfWasm.RequireGpuRendering) but no GPU render target is available; the software target is rejected before painting.");
		// Dirty-gated frame: drain once; a fully-idle frame (no app
		// Invalidate, no host marking) emits nothing (RepaintAsync skips
		// the interop round trip for empty command lists).
		s_frameGlobal = System.Windows.Forms.WasmRepaintGate.DrainDirty(out var frameDirty);
		s_frameDirty = frameDirty;
		// Perf item 4 frame open (W-1): claim one fork frame seq,
		// snapshot the dirty gate + pending input range, reset the
		// per-frame counters. The empty early-return below still
		// presents a consistent (empty) frame to the pump sample.
		if (System.Windows.Forms.WasmFrameCounters.PerfEnabled)
		{
			System.Windows.Forms.WasmFrameCounters.ClaimFrame();
			System.Windows.Forms.WasmFrameCounters.ResetFrame();
			// The layout drain above runs before the frame reset, so its
			// ticks are added back here (ResetFrame zeroes DrainTicks).
			if (layoutTicks != 0)
				System.Windows.Forms.WasmFrameCounters.AddDrainTicks(layoutTicks);
			System.Windows.Forms.WasmFrameCounters.FrameGlobal = s_frameGlobal ? 1 : 0;
			System.Windows.Forms.WasmFrameCounters.FrameDirtyCount = frameDirty.Count;
			System.Windows.Forms.WasmFrameCounters.TakePending(out int perfCsFrom, out int perfCsTo);
			System.Windows.Forms.WasmFrameCounters.CsFrom = perfCsFrom;
			System.Windows.Forms.WasmFrameCounters.CsTo = perfCsTo;
			System.Windows.Forms.WasmFrameCounters.TakeMarks();
		}
		if (!s_frameGlobal && frameDirty.Count == 0)
		{
			s_frameDirty = null;
			s_frameGlobal = false;
			return;
		}
		lock (s_liveLayers)
		{
			s_liveLayers.Clear();
		}
		canvas.FillRectangle(0f, 0f, ViewWidth, ViewHeight, 505290495u);
		if (_activeForm == null)
		{
			EmitRetain(canvas);
			return;
		}
		int num = ChromeHeight(_activeForm);
		int x = _activeForm.Location.X;
		int y = _activeForm.Location.Y;
		int width = _activeForm.Width;
		int height = _activeForm.Height;
		uint value = ToRgba(_activeForm.BackColor, 4042322175u);
		int paintLog = System.Threading.Interlocked.Increment(ref s_paintLogCount);
		if (PaintDiagnosticsEnabled && paintLog == 1 && _activeForm is not null)
		{
			try
			{
				var chain = new System.Text.StringBuilder();
				for (System.Type? t = _activeForm.GetType(); t is not null; t = t.BaseType) { chain.Append(t.Name).Append('/'); if (t.Name == "Form") break; }
				Console.WriteLine($"[CHROME] type={_activeForm.GetType().FullName} chain={chain} border={_activeForm.FormBorderStyle} h={ChromeHeight(_activeForm)}");
			}
			catch { }
		}
		if (PaintDiagnosticsEnabled && (paintLog <= 3 || paintLog % 64 == 0))
			Console.WriteLine($"[Paint] Form: X={x}, Y={y}, W={width}, H={height}, BackColor={_activeForm.BackColor.Name} (0x{value:X8}), Controls={_activeForm.Controls.Count}");
		if (PaintDiagnosticsEnabled)
		{
			if (paintLog == 1 && _activeForm is not null)
			{
				DumpGridVisualState(_activeForm);
				DumpTileVisualState(_activeForm);
			}
			DumpDockTabText(_activeForm);
		}
		// Late tile re-dump lives in LogFirstPaint (per-control paints
		// keep firing while async book loads repaint; this frame path
		// idles once settled).
		canvas.FillRectangle(x + 4, y + 4, width, height + num, 64u);
		if (num > 0)
		{
		canvas.FillGradientVertical(x, y, width, num, 7919615u, 5938943u);
		canvas.DrawTextAligned(_activeForm.Text ?? "Window", x + 10, y + num / 2, "bold 13px 'Segoe UI', sans-serif", uint.MaxValue, TextHAlign.Left, TextVAlign.Middle);
		int num2 = x + width - 28;
		int num3 = y + 6;
		canvas.FillRectangle(num2, num3, 18f, 18f, 3893437439u);
		canvas.DrawTextAligned("\u00d7", num2 + 9, num3 + 9, "bold 14px sans-serif", uint.MaxValue, TextHAlign.Center, TextVAlign.Middle);
		}
		value = ToRgba(_activeForm.BackColor, 4042322175u);
		int num4 = y + num;
		canvas.FillRectangle(x, num4, width, height, value);
		RenderControls(canvas, _activeForm.Controls, x, num4);
		RenderShownWindows(canvas);
		RenderOpenDropdowns(canvas);
		RenderTrackedPopups(canvas);
		EmitRetain(canvas);
		s_frameDirty = null;
		s_frameGlobal = false;
	}

	internal static string LayerIdFor(object key)
	{
		ArgumentNullException.ThrowIfNull(key, "key");
		return s_layerIds.GetValue(key, (object _) => "L" + Interlocked.Increment(ref s_layerSeq));
	}

	// Clean-layer fast path: a control that is neither dirty, new, nor
	// resized keeps its retained pixels; re-blit without CPU re-raster
	// (no Bitmap alloc, no OnPaint invoke, no extract, no re-hash).
	// Plain (non-layer) canvases always raster (they need the bytes).
	internal static bool TryBlitCleanLayer(ICanvas canvas, System.Windows.Forms.Control c, int absX, int absY, int w, int h)
	{
		if (IsFrameDirty(c))
			return false;
		if (!(canvas is ICanvasRenderer { SupportsBitmapLayers: not false } canvasRenderer))
			return false;
		string id = LayerIdFor(c);
		lock (s_layerHash)
		{
			if (!s_layerHash.ContainsKey(id))
				return false;
			if (s_layerSize.TryGetValue(id, out var size) && (size.w != w || size.h != h))
				return false;
		}
		lock (s_liveLayers)
		{
			s_liveLayers.Add(id);
		}
		canvasRenderer.BlitBitmap(id, absX, absY, w, h);
		if (System.Windows.Forms.WasmFrameCounters.PerfEnabled)
			System.Windows.Forms.WasmFrameCounters.CountBlitClean();
		return true;
	}

	internal static bool IsFrameDirty(System.Windows.Forms.Control c)
	{
		if (s_frameGlobal)
			return true;
		var set = s_frameDirty;
		if (set is null)
			return true;
		return set.ContainsKey(c);
	}

	// Dirty region for the frame (null rect = whole control). A missing
	// entry is clean; global or undrained frames are whole-control.
	internal static bool FrameDirtyRect(System.Windows.Forms.Control c, out System.Drawing.Rectangle? rc)
	{
		if (s_frameGlobal || s_frameDirty is null)
		{
			rc = null;
			return true;
		}
		if (s_frameDirty.TryGetValue(c, out rc))
			return true;
		rc = null;
		return false;
	}

	// Change-detection hash (FNV-1a over 32-bit words with a byte tail:
	// 4x fewer multiplies than the byte loop; values are internal, only
	// determinism + single-byte sensitivity are pinned).
	internal static ulong HashBytes(byte[] data)
	{
		ulong num = 14695981039346656037uL;
		int i = 0;
		int intEnd = data.Length & ~3;
		for (; i < intEnd; i += 4)
		{
			uint word = (uint)(data[i] | (data[i + 1] << 8) | (data[i + 2] << 16) | (data[i + 3] << 24));
			num ^= word;
			num *= 1099511628211L;
		}
		for (; i < data.Length; i++)
		{
			num ^= data[i];
			num *= 1099511628211L;
		}
		return num;
	}

	internal static void RecordBitmap(object layerKey, ICanvas canvas, byte[] rgba, int w, int h, float x, float y, float dw, float dh)
	{
		if (canvas is ICanvasRenderer { SupportsBitmapLayers: not false } canvasRenderer)
		{
			string text = LayerIdFor(layerKey);
			// A full Def replaces the patch base: drop any retained
			// self-paint surface (the self-paint path re-stores below).
			lock (s_retainedPaint)
			{
				if (s_retainedPaint.TryGetValue(text, out Bitmap? stale))
				{
					s_retainedPaint.Remove(text);
					stale.Dispose();
				}
			}
			lock (s_liveLayers)
			{
				s_liveLayers.Add(text);
			}
            ulong num = HashBytes(rgba);
            if (System.Windows.Forms.WasmFrameCounters.PerfEnabled)
                System.Windows.Forms.WasmFrameCounters.AddBytesHashed(rgba.Length);
			bool flag;
			lock (s_layerHash)
			{
				flag = s_layerHash.TryGetValue(text, out var value) && value == num;
				s_layerHash[text] = num;
				s_layerSize[text] = (w, h);
			if (System.Windows.Forms.WasmFrameCounters.PerfEnabled)
				System.Windows.Forms.WasmFrameCounters.CountLayerHashHit(flag);
			}
			if (!flag)
			{
                canvasRenderer.DefBitmap(text, rgba, w, h);
			}
			canvasRenderer.BlitBitmap(text, x, y, dw, dh);
		}
		else
		{
			canvas.DrawImage(rgba, w, h, x, y, dw, dh);
		}
	}

	private static void EmitRetain(ICanvas canvas)
	{
		if (!(canvas is ICanvasRenderer { SupportsBitmapLayers: not false } canvasRenderer))
		{
			return;
		}
		string[] liveIds;
		lock (s_liveLayers)
		{
			liveIds = s_liveLayers.ToArray();
			lock (s_layerHash)
			{
				string[] array = s_layerHash.Keys.Where((string k) => !s_liveLayers.Contains(k)).ToArray();
				string[] array2 = array;
				foreach (string key in array2)
				{
					s_layerHash.Remove(key);
					s_layerSize.Remove(key);
					lock (s_retainedPaint)
					{
						if (s_retainedPaint.TryGetValue(key, out Bitmap? dead))
						{
							s_retainedPaint.Remove(key);
							dead.Dispose();
						}
					}
				}
			}
		}
		canvasRenderer.RetainLayers(liveIds);
	}

	private void RenderOpenDropdowns(ICanvas canvas)
	{
		foreach (var (toolStripDropDown, num, num2) in GetOpenDropdowns().ToList())
		{
			Console.WriteLine($"[Host] Dropdown open: {toolStripDropDown.Name} Bounds={toolStripDropDown.Bounds} Visible={toolStripDropDown.Visible}");
			int num3 = num + toolStripDropDown.Location.X;
			int num4 = num2 + toolStripDropDown.Location.Y;
			int width = toolStripDropDown.Width;
			int height = toolStripDropDown.Height;
			if (width > 0 && height > 0)
			{
				canvas.FillRectangle(num3 + 2, num4 + 2, width, height, 64u);
				canvas.FillRectangle(num3, num4, width, height, 4126603263u);
				canvas.DrawLine(num3, num4, num3 + width, num4, 1f, 2543294463u);
				canvas.DrawLine(num3, num4 + height, num3 + width, num4 + height, 1f, 2543294463u);
				canvas.DrawLine(num3, num4, num3, num4 + height, 1f, 2543294463u);
				canvas.DrawLine(num3 + width, num4, num3 + width, num4 + height, 1f, 2543294463u);
				RenderDropdownItems(canvas, toolStripDropDown, num3, num4);
			}
		}
	}

	private void RenderDropdownItems(ICanvas canvas, System.Windows.Forms.ToolStripDropDown drop, int dx, int dy)
	{
		foreach (System.Windows.Forms.ToolStripItem item in drop.Items)
		{
			if (!item.Visible || item.Width <= 0 || item.Height <= 0)
			{
				continue;
			}
			int num = dx + item.Bounds.X;
			int num2 = dy + item.Bounds.Y;
			if (item is System.Windows.Forms.ToolStripSeparator)
			{
				canvas.DrawLine(num + 28, num2 + item.Height / 2, num + item.Width - 4, num2 + item.Height / 2, 1f, 3503345919u);
				continue;
			}
			if (item.Selected || item.Pressed)
			{
				canvas.FillRectangle(num + 1, num2, item.Width - 2, item.Height, 3437557759u);
			}
			uint rgba = ToRgba(item.ForeColor, item.Enabled ? 572662527u : 2290649343u);
			string text = CleanItemText(item.Text);
			if (item is System.Windows.Forms.ToolStripMenuItem { Checked: not false })
			{
				canvas.DrawTextAligned("\u2713", num + 12, (float)num2 + (float)item.Height / 2f, "12px sans-serif", rgba, TextHAlign.Center, TextVAlign.Middle);
			}
			canvas.DrawTextAligned(text, num + 28, (float)num2 + (float)item.Height / 2f, "12px 'Segoe UI', sans-serif", rgba, TextHAlign.Left, TextVAlign.Middle);
			if (item is System.Windows.Forms.ToolStripMenuItem toolStripMenuItem2 && !string.IsNullOrEmpty(toolStripMenuItem2.ShortcutKeyDisplayString))
			{
				canvas.DrawTextAligned(toolStripMenuItem2.ShortcutKeyDisplayString, num + item.Width - 8, (float)num2 + (float)item.Height / 2f, "11px 'Segoe UI', sans-serif", 1431655935u, TextHAlign.Right, TextVAlign.Middle);
			}
			else if (item is System.Windows.Forms.ToolStripMenuItem { HasDropDownItems: not false })
			{
				canvas.DrawTextAligned("\u25b6", num + item.Width - 12, (float)num2 + (float)item.Height / 2f, "9px sans-serif", rgba, TextHAlign.Center, TextVAlign.Middle);
			}
		}
	}

	private void RenderShownWindows(ICanvas canvas)
	{
		WasmShownWindow[] array = WasmDialogHost.Snapshot();
		_placedWindows.RemoveWhere((System.Windows.Forms.Form f) => f.IsDisposed || f.Disposing || !f.Visible);
		if (array.Length == 0)
		{
			_modalActive = false;
			return;
		}
		WasmShownWindow[] array2 = array;
		foreach (WasmShownWindow wasmShownWindow in array2)
		{
			System.Windows.Forms.Form form = wasmShownWindow.Form;
			if (!form.Visible || form.IsDisposed || form.Disposing)
			{
				continue;
			}
			if (wasmShownWindow.IsModal && form.DialogResult != System.Windows.Forms.DialogResult.None)
			{
				CloseModalIfSettled(form);
				continue;
			}
			if (_forms.ContainsValue(form))
			{
				continue;
			}
			if (!_placedWindows.Contains(form))
			{
				_placedWindows.Add(form);
				// One-shot dialog tree (same [Scene]-dump style, modal +
				// modeless): answers "missing vs unpainted" for dialog
				// controls (e.g. PDN effect dialogs with no visible
				// OK/Cancel). First-placement gate: one dump per dialog.
				Console.WriteLine($"[Dialog] Opened {form.GetType().FullName} modal={wasmShownWindow.IsModal} Bounds={form.Bounds}");
				DumpControlTree(form, 0);
				if (form.Width <= 0 || form.Height <= 0)
				{
					form.Size = new Size(Math.Max(form.Width, 360), Math.Max(form.Height, 240));
				}
				if (form.Location.X == 0 && form.Location.Y == 0)
				{
					form.Location = new Point(Math.Max(0, (ViewWidth - form.Width) / 2), Math.Max(0, (ViewHeight - form.Height - ChromeHeight(form)) / 2));
				}
				try
				{
					int num2 = 0;
					int num3 = 0;
					foreach (System.Windows.Forms.Control control in form.Controls)
					{
						if (control.Visible)
						{
							num2 = Math.Max(num2, control.Right);
							num3 = Math.Max(num3, control.Bottom);
						}
					}
					int num4 = Math.Max(form.Width, num2);
					int num5 = Math.Max(form.Height, num3);
					if (num4 != form.Width || num5 != form.Height)
					{
						form.Size = new Size(num4, num5);
					}
					int num6 = form.Location.X;
					int num7 = form.Location.Y;
					if (num6 + num4 > ViewWidth)
					{
						num6 = Math.Max(0, ViewWidth - num4);
					}
					if (num7 + num5 + ChromeHeight(form) > ViewHeight)
					{
						num7 = Math.Max(0, ViewHeight - num5 - ChromeHeight(form));
					}
					if (num6 != form.Location.X || num7 != form.Location.Y)
					{
						form.Location = new Point(num6, num7);
					}
				}
				catch
				{
				}
				Console.WriteLine($"[Host] Dialog placed: {form.GetType().Name} '{form.Text}' at {form.Location} size {form.Size} modal={wasmShownWindow.IsModal}");
			}
			int x = form.Location.X;
			int y = form.Location.Y;
			int width = form.Width;
			int height = form.Height;
			int chrome = ChromeHeight(form);
			canvas.FillRectangle(x + 4, y + 4, width, height + chrome, 96u);
			if (chrome > 0)
			{
				canvas.FillGradientVertical(x, y, width, chrome, 7919615u, 5938943u);
			canvas.DrawTextAligned(form.Text ?? "Dialog", x + 10, y + chrome / 2, "bold 13px 'Segoe UI', sans-serif", uint.MaxValue, TextHAlign.Left, TextVAlign.Middle);
			int num8 = x + width - 28;
			int num9 = y + 6;
			canvas.FillRectangle(num8, num9, 18f, 18f, 3893437439u);
				canvas.DrawTextAligned("x", num8 + 9, num9 + 9, "bold 14px sans-serif", uint.MaxValue, TextHAlign.Center, TextVAlign.Middle);
			}
			uint rgba = ToRgba(form.BackColor, 4042322175u);
			canvas.FillRectangle(x, y + chrome, width, height, rgba);
			RenderControls(canvas, form.Controls, x, y + chrome);
		}
	}

	private int _lastPopupCount;

	// Top-level popups (Telerik context menus, combo dropdowns) paint above
	// everything via the same self-painted path as Telerik grid cells; a
	// count change dirties globally so the newly covered rect repaints.
	private void RenderTrackedPopups(ICanvas canvas)
	{
		System.Windows.Forms.Control[] popups = System.Windows.Forms.WasmPopupHost.Snapshot();
		if (popups.Length != _lastPopupCount)
		{
			_lastPopupCount = popups.Length;
			System.Windows.Forms.WasmRepaintGate.MarkGlobalDirty();
		}
		foreach (System.Windows.Forms.Control popup in popups)
		{
			if (!popup.Visible || popup.IsDisposed || popup.Disposing || popup.Width <= 0 || popup.Height <= 0)
			{
				continue;
			}
			int px = popup.Location.X;
			int py = popup.Location.Y;
			canvas.FillRectangle(px + 3, py + 3, popup.Width, popup.Height, 96u);
			if (!TryRenderSelfPainted(canvas, popup, px, py, popup.Width, popup.Height))
			{
				if (!popup.BackColor.IsEmpty && popup.BackColor != Color.Transparent)
				{
					canvas.FillRectangle(px, py, popup.Width, popup.Height, ToRgba(popup.BackColor, 4042322175u));
				}
				TryRenderD2DControl(canvas, popup, px, py, popup.Width, popup.Height);
			}
			if (popup.Controls.Count > 0)
			{
				RenderControls(canvas, popup.Controls, px, py);
			}
		}
	}

	private bool TopPopupAt(int x, int y, out System.Windows.Forms.Control? popup)
	{
		popup = null;
		System.Windows.Forms.Control[] popups = System.Windows.Forms.WasmPopupHost.Snapshot();
		for (int i = popups.Length - 1; i >= 0; i--)
		{
			System.Windows.Forms.Control c = popups[i];
			if (!c.Visible || c.IsDisposed || c.Disposing)
			{
				continue;
			}
			if (x >= c.Location.X && x < c.Location.X + c.Width && y >= c.Location.Y && y < c.Location.Y + c.Height)
			{
				popup = c;
				return true;
			}
		}
		return false;
	}

	// Popup-first down routing: a hit goes to the popup tree (falling back
	// to the popup itself, whose element tree dispatches internally); an
	// outside press dismisses through Telerik's own manager and is
	// swallowed, matching desktop menu behavior. Returns true when a
	// popup was open (handled either way).
	private bool RoutePopupMouseDown(int x, int y, System.Windows.Forms.MouseButtons buttons)
	{
		if (System.Windows.Forms.WasmPopupHost.Snapshot().Length == 0)
		{
			return false;
		}
		if (TopPopupAt(x, y, out System.Windows.Forms.Control? popup) && popup != null)
		{
			Console.WriteLine("[Host] OnMouseDown popup-hit=" + popup.GetType().Name + " '" + popup.Name + "'");
			RecordInput("down", $"{x},{y} {buttons} popup={popup.Name}");
			_mouseTarget = HitTestDeep(popup.Controls, popup.Location.X, popup.Location.Y, x, y) ?? popup;
			System.Windows.Forms.WasmRepaintGate.MarkDirtyChain(_mouseTarget);
			NoteFocus(_mouseTarget);
			RaiseMouseEvent(s_onMouseDownMethod, _mouseTarget, buttons, 1, x, y);
			return true;
		}
		int before = System.Windows.Forms.WasmPopupHost.Snapshot().Length;
		LogPopupState("miss");
		DismissPopupsForOutsideDown(x, y);
		if (System.Windows.Forms.WasmPopupHost.Snapshot().Length < before)
		{
			return true;
		}
		return false;
	}

	private bool RoutePopupClick(int x, int y, System.Windows.Forms.MouseButtons clickedButton, int clicks)
	{
		if (System.Windows.Forms.WasmPopupHost.Snapshot().Length == 0)
		{
			return false;
		}
		if (TopPopupAt(x, y, out System.Windows.Forms.Control? popup) && popup != null)
		{
			System.Windows.Forms.Control target = HitTestDeep(popup.Controls, popup.Location.X, popup.Location.Y, x, y) ?? popup;
			Console.WriteLine("[Host] OnClick popup-hit=" + popup.GetType().Name + " '" + popup.Name + "'");
			RecordInput("click", $"{x},{y} popup={popup.Name} target={DescribeControl(target)}");
			System.Windows.Forms.WasmRepaintGate.MarkDirtyChain(target);
			RaiseMouseEvent(s_onMouseClickMethod, target, clickedButton, clicks, x, y);
			return true;
		}
		int before = System.Windows.Forms.WasmPopupHost.Snapshot().Length;
		LogPopupState("miss");
		DismissPopupsForOutsideDown(x, y);
		if (System.Windows.Forms.WasmPopupHost.Snapshot().Length < before)
		{
			return true;
		}
		return false;
	}

	// Popup-miss diagnostic: which tracked popups were alive when a press
	// landed outside all of them (type/parent/rect each). Silent unless a
	// miss happens; failures never break input routing.
	private static void LogPopupState(string why)
	{
		try
		{
			System.Windows.Forms.Control[] tracked = System.Windows.Forms.WasmPopupHost.Snapshot();
			string line = "[POPUP] state " + why + " n=" + tracked.Length;
			foreach (System.Windows.Forms.Control control in tracked)
			{
				string parent;
				try
				{
					parent = control.Parent == null ? "null" : control.Parent.GetType().Name;
				}
				catch
				{
					parent = "THREW";
				}
				line += " [" + control.GetType().Name + " par=" + parent + " at=" + control.Location.X + "," + control.Location.Y + " sz=" + control.Width + "x" + control.Height + "]";
			}
			Console.WriteLine(line);
		}
		catch
		{
		}
	}

	// Telerik dismisses popups from RadMessageFilter-fed mouse presses; the
	// host bypasses the message loop, so drive its public manager entry
	// directly (reflection: the host must not bind Telerik statically).
	private static void DismissPopupsForOutsideDown(int x, int y)
	{
		try
		{
			foreach (System.Reflection.Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				if (asm.GetName().Name != "Telerik.WinControls.UI")
				{
					continue;
				}
				Type? pmType = asm.GetType("Telerik.WinControls.UI.PopupManager");
				object? def = pmType?.GetProperty("Default", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
				pmType?.GetMethod("CallOnMouseDown", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)?.Invoke(def, new object[] { new Point(x, y) });
				break;
			}
		}
		catch
		{
		}
		System.Windows.Forms.WasmRepaintGate.MarkGlobalDirty();
	}
	private bool TopModalWindow(out System.Windows.Forms.Form? modal)
	{
		if (WasmDialogHost.TopModal(out modal) && modal != null)
		{
			if (!_modalActive)
			{
				_modalActive = true;
				CloseOpenDropdowns();
			}
			return true;
		}
		_modalActive = false;
		modal = null;
		return false;
	}

	private bool TopTrackedWindowAt(int x, int y, out System.Windows.Forms.Form? win, out bool isModal)
	{
		win = null;
		isModal = false;
		WasmShownWindow[] array = WasmDialogHost.Snapshot();
		for (int num = array.Length - 1; num >= 0; num--)
		{
			System.Windows.Forms.Form form = array[num].Form;
			if (!_forms.ContainsValue(form))
			{
				int x2 = form.Location.X;
				int y2 = form.Location.Y;
				if (x >= x2 && x <= x2 + form.Width && y >= y2 && y <= y2 + ChromeHeight(form) + form.Height)
				{
					win = form;
					isModal = array[num].IsModal;
					return true;
				}
			}
		}
		return false;
	}

	private static bool DialogCloseHit(System.Windows.Forms.Form dlg, int x, int y)
	{
		if (ChromeHeight(dlg) == 0) return false;
		int x2 = dlg.Location.X;
		int y2 = dlg.Location.Y;
		return x >= x2 + dlg.Width - 28 && x <= x2 + dlg.Width - 10 && y >= y2 + 6 && y <= y2 + 24;
	}

	private bool BeginDialogDrag(System.Windows.Forms.Form dlg, int x, int y, string tag)
	{
		if (DialogCloseHit(dlg, x, y))
		{
			_mouseTarget = null;
			RecordInput("down", $"{x},{y} {tag}-closezone dlg={dlg.Name}");
			return true;
		}
		int x2 = dlg.Location.X;
		int y2 = dlg.Location.Y;
		if (x >= x2 && x <= x2 + dlg.Width && y >= y2 && y < y2 + ChromeHeight(dlg))
		{
			Console.WriteLine("[Host] OnMouseDown hit " + tag + " title bar dlg=" + dlg.Name);
			RecordInput("down", $"{x},{y} {tag}-titlebar dlg={dlg.Name}");
			_isDraggingTitle = true;
			_dragForm = dlg;
			_dragStartX = x;
			_dragStartY = y;
			_dragInitialFormX = x2;
			_dragInitialFormY = y2;
			_mouseTarget = null;
			return true;
		}
		return false;
	}

	private void DialogOnDown(System.Windows.Forms.Form dlg, int x, int y, System.Windows.Forms.MouseButtons buttons, string tag)
	{
		RecordInput("down", $"{x},{y} {buttons} {tag}={dlg.Name}");
		_mouseTarget = HitTestDeep(dlg.Controls, dlg.Location.X, dlg.Location.Y + ChromeHeight(dlg), x, y);
		NoteFocus(_mouseTarget);
		if (_mouseTarget != null && !(_mouseTarget is System.Windows.Forms.ToolStrip))
		{
			RaiseMouseEvent(s_onMouseDownMethod, _mouseTarget, buttons, 1, x, y);
		}
	}

	private bool DownTrackedDock(int x, int y, System.Windows.Forms.MouseButtons buttons)
	{
		if (TopTrackedWindowAt(x, y, out System.Windows.Forms.Form win, out bool isModal) && win != null && !isModal)
		{
			if (BeginDialogDrag(win, x, y, "dock"))
			{
				return true;
			}
			DialogOnDown(win, x, y, buttons, "dock");
			System.Windows.Forms.WasmRepaintGate.MarkDirtyChain(win);
			return true;
		}
		return false;
	}

	private bool ClickTrackedDock(int x, int y)
	{
		if (TopTrackedWindowAt(x, y, out System.Windows.Forms.Form win, out bool isModal) && win != null && !isModal)
		{
			CloseOpenDropdowns();
			ModalOnClick(win, x, y, "dock");
			return true;
		}
		return false;
	}

	private static void CloseModalIfSettled(System.Windows.Forms.Form modal)
	{
		// Headless pump emulation: on desktop the nested modal loop
		// exits (closing the dialog) once DialogResult is set. There is
		// no pump here, so a set result would leave the dialog open
		// forever (BookShelf's offline MessageBox survived every OK).
		try
		{
			if (modal.Modal && modal.Visible && !modal.IsDisposed && !modal.Disposing
				&& modal.DialogResult != System.Windows.Forms.DialogResult.None)
				modal.Close();
		}
		catch (Exception ex)
		{
			BrowserErrorReporter.Report("dialog-close", ex);
		}
	}

	private void ModalOnClick(System.Windows.Forms.Form modal, int x, int y, string tag = "modal", System.Windows.Forms.MouseButtons clickedButton = System.Windows.Forms.MouseButtons.Left, int clicks = 1)
	{
		int x2 = modal.Location.X;
		int y2 = modal.Location.Y;
		if (ChromeHeight(modal) > 0 && x >= x2 + modal.Width - 28 && x <= x2 + modal.Width - 10 && y >= y2 + 6 && y <= y2 + 24)
		{
			RecordInput("click", $"{x},{y} {tag}-close dlg={modal.Name}");
			try
			{
				// A user close always carries OK/Cancel at entry (desktop X
				// travels WM_CLOSE, not bare Close): None-at-entry is the
				// fork's abort signature (WasmDialogHost.DeferAbortClose)
				// and would be skipped, leaving the dialog open forever.
				if (modal.Modal && modal.DialogResult == System.Windows.Forms.DialogResult.None)
					modal.DialogResult = System.Windows.Forms.DialogResult.Cancel;
				modal.Close();
				return;
			}
			catch (Exception ex)
			{
				BrowserErrorReporter.Report("dialog-close", ex);
				return;
			}
		}
		System.Windows.Forms.Control control = HitTestDeep(modal.Controls, x2, y2 + ChromeHeight(modal), x, y);
		Console.WriteLine("[Host] OnClick modal hit=" + ((control == null) ? "null" : (control.GetType().Name + " '" + control.Name + "'")));
		if (control == null)
		{
			RecordInput("click", $"{x},{y} {tag}-swallow dlg={modal.Name}");
			return;
		}
		if (control is System.Windows.Forms.Button button)
		{
			RecordInput("click", $"{x},{y} {tag}-button={button.Name}");
			try
			{
				// Desktop order: the press moves focus before the button
				// activates, so edits commit (Leave/validation) before the
				// click observes them (Levels output-low: typed 100,
				// applied 0 without this).
				NoteFocus(button);
				button.PerformClick();
				RaiseMouseEvent(s_onMouseClickMethod, button, clickedButton, clicks, x, y);
				CloseModalIfSettled(modal);
				return;
			}
			catch (Exception ex2)
			{
				Console.WriteLine($"[WinFormsAppHost] Modal button click error: {ex2.GetType().Name}: {ex2.Message.Split('\n')[0]} site={SiteOf(ex2)}");
				BrowserErrorReporter.Report("button-click", ex2);
				return;
			}
		}
		if (control is System.Windows.Forms.IButtonControl pushed)
		{
			// Custom dialog buttons (PDN PdnPushButton family derives from
			// GdiBufferedAnimationControl, not Button): activate through the
			// interface and settle like a Button click so a chosen
			// OK/Cancel result closes the modal headless (no pump).
			RecordInput("click", $"{x},{y} {tag}-pushbutton={control.Name}");
			try
			{
				// Same focus-first order as real buttons above.
				NoteFocus(control);
				pushed.PerformClick();
				RaiseMouseEvent(s_onMouseClickMethod, control, clickedButton, clicks, x, y);
				CloseModalIfSettled(modal);
				return;
			}
			catch (Exception exPush)
			{
				Console.WriteLine($"[WinFormsAppHost] Modal pushbutton click error: {exPush.GetType().Name}: {exPush.Message.Split('\n')[0]} site={SiteOf(exPush)}");
				BrowserErrorReporter.Report("button-click", exPush);
				return;
			}
		}
		if (control is System.Windows.Forms.TextBox textBox)
		{
			NoteFocus(textBox);
			RecordInput("click", $"{x},{y} {tag}-textbox={textBox.Name}");
			return;
		}
		if (control is System.Windows.Forms.ToolStrip toolStrip)
		{
			int controlAbsX = GetControlAbsX(toolStrip, x2);
			int controlAbsY = GetControlAbsY(toolStrip, y2 + ChromeHeight(modal));
			System.Windows.Forms.ToolStripItem itemAt = toolStrip.GetItemAt(x - controlAbsX, y - controlAbsY);
			if (itemAt != null)
			{
				RecordInput("click", $"{x},{y} {tag}-item={DescribeItem(itemAt)}");
				ClickToolStripItem(toolStrip, itemAt, controlAbsX, controlAbsY, x - controlAbsX, y - controlAbsY);
			}
			return;
		}
		RecordInput("click", $"{x},{y} {tag}-control={DescribeControl(control)}");
		try
		{
			s_onClickMethod?.Invoke(control, new object[1] { EventArgs.Empty });
			RaiseMouseEvent(s_onMouseClickMethod, control, clickedButton, clicks, x, y);
		}
		catch (Exception ex3)
		{
			Console.WriteLine("[WinFormsAppHost] Modal control click error: " + ex3.GetType().Name + ": " + ex3.Message.Split('\n')[0]);
			BrowserErrorReporter.Report("control-click", ex3);
		}
	}

	private List<(System.Windows.Forms.ToolStripDropDown Drop, int Ox, int Oy)> GetOpenDropdowns()
	{
		_openDropdowns.RemoveAll(((System.Windows.Forms.ToolStripDropDown Drop, int Ox, int Oy) d) => !d.Drop.Visible);
		return _openDropdowns;
	}

	internal void TrackDropdown(System.Windows.Forms.ToolStripDropDown drop, int ox, int oy)
	{
		_openDropdowns.RemoveAll(((System.Windows.Forms.ToolStripDropDown Drop, int Ox, int Oy) d) => d.Drop == drop || !d.Drop.Visible);
		if (drop.Visible)
		{
			_openDropdowns.Add((drop, ox, oy));
		}
	}

	private void RenderControls(ICanvas canvas, System.Windows.Forms.Control.ControlCollection controls, int parentAbsX, int parentAbsY)
	{
		for (int i = 0; i < controls.Count; i++)
		{
			System.Windows.Forms.Control control = controls[i];
			if (!control.Visible || control.Width <= 0 || control.Height <= 0)
			{
				continue;
			}
			int num = parentAbsX + control.Left;
			int num2 = parentAbsY + control.Top;
			int width = control.Width;
			int height = control.Height;
			if (control is System.Windows.Forms.Button button)
			{
				uint rgba = ((button == _pressedButton) ? 3437557759u : ToRgba(button.BackColor, 3789677055u));
				canvas.FillRoundedRectangle(num, num2, width, height, 3f, rgba);
				canvas.DrawLine(num, num2, num + width, num2, 1f, 2913840639u);
				canvas.DrawLine(num, num2 + height, num + width, num2 + height, 1f, 2913840639u);
				canvas.DrawLine(num, num2, num, num2 + height, 1f, 2913840639u);
				canvas.DrawLine(num + width, num2, num + width, num2 + height, 1f, 2913840639u);
				uint rgba2 = ToRgba(button.ForeColor, 255u);
				string fontCss = GetFontCss(button.Font);
				canvas.DrawTextAligned(button.Text, (float)num + (float)width / 2f, (float)num2 + (float)height / 2f, fontCss, rgba2, TextHAlign.Center, TextVAlign.Middle);
				if (button.Image is Bitmap bmp)
				{
					PaintCenteredBitmap(canvas, bmp, num, num2, width, height, button);
				}
			}
			else if (control is System.Windows.Forms.TextBox textBox)
			{
				uint rgba3 = ToRgba(textBox.BackColor, uint.MaxValue);
				canvas.FillRectangle(num, num2, width, height, rgba3);
				bool flag = textBox == _focusedTextBox;
				uint rgba4 = (flag ? 7919615u : 2141043199u);
				float width2 = ((!flag) ? 1 : 2);
				canvas.DrawLine(num, num2, num + width, num2, width2, rgba4);
				canvas.DrawLine(num, num2 + height, num + width, num2 + height, width2, rgba4);
				canvas.DrawLine(num, num2, num, num2 + height, width2, rgba4);
				canvas.DrawLine(num + width, num2, num + width, num2 + height, width2, rgba4);
				uint rgba5 = ToRgba(textBox.ForeColor, 255u);
				string fontCss2 = GetFontCss(textBox.Font);
				string text = textBox.Text ?? "";
				if (flag)
				{
					text += "|";
				}
				if (textBox.Multiline)
				{
					// Native multiline EDIT stacks lines from the top;
					// the single centered run washed content to the
					// middle (spellcheck boxes: browser mid-box vs
					// native top-left). One Top-anchored run per line.
					float lineH = System.Math.Max(1f, textBox.Font.GetHeight());
					string[] lines = text.Split(new[] { "\r\n", "\n" }, System.StringSplitOptions.None);
					for (int li = 0; li < lines.Length; li++)
					{
						canvas.DrawTextAligned(lines[li], num + 5, num2 + 2 + (li * lineH), fontCss2, rgba5, TextHAlign.Left, TextVAlign.Top);
					}
				}
				else
				{
					canvas.DrawTextAligned(text, num + 5, (float)num2 + (float)height / 2f, fontCss2, rgba5, TextHAlign.Left, TextVAlign.Middle);
				}
			}
			else if (control is System.Windows.Forms.Label label)
			{
				if (label.BackColor != Color.Transparent && !label.BackColor.IsEmpty)
				{
					canvas.FillRectangle(num, num2, width, height, ToRgba(label.BackColor, 4042322175u));
				}
				uint rgba6 = ToRgba(label.ForeColor, 255u);
				string fontCss3 = GetFontCss(label.Font);
				canvas.DrawTextAligned(label.Text, num + 2, (float)num2 + (float)height / 2f, fontCss3, rgba6, TextHAlign.Left, TextVAlign.Middle);
			}
			else if (control is System.Windows.Forms.ListView listView)
			{
				canvas.FillRectangle(num, num2, width, height, ToRgba(listView.BackColor, uint.MaxValue));
				canvas.DrawLine(num, num2, num + width, num2, 1f, 2141043199u);
				canvas.DrawLine(num, num2 + height, num + width, num2 + height, 1f, 2141043199u);
				canvas.DrawLine(num, num2, num, num2 + height, 1f, 2141043199u);
				canvas.DrawLine(num + width, num2, num + width, num2 + height, 1f, 2141043199u);
				int num3 = 24;
				canvas.FillRectangle(num, num2, width, num3, 4008636159u);
				canvas.DrawLine(num, num2 + num3, num + width, num2 + num3, 1f, 3435973887u);
				int num4 = num + 6;
				for (int j = 0; j < listView.Columns.Count; j++)
				{
					System.Windows.Forms.ColumnHeader columnHeader = listView.Columns[j];
					canvas.DrawTextAligned(columnHeader.Text, num4, (float)num2 + (float)num3 / 2f, "bold 12px sans-serif", 858993663u, TextHAlign.Left, TextVAlign.Middle);
					num4 += columnHeader.Width;
				}
				int num5 = num2 + num3 + 2;
				int num6 = 22;
				// Windowed paint: rows [top, top+cap) map to the visible slots
				// so overflow scrolls (native parity) instead of dropping
				// silently. Geometry for top=0 is unchanged (oracles pin it).
				int cap = ListViewCapacity(listView.Height);
				int top = ResolveListViewTop(listView);
				int count = listView.Items.Count;
				bool scroll = count > cap;
				int rowW = scroll ? width - ListViewScrollW : width;
				for (int k = top; k < count && k - top < cap; k++)
				{
					int ry = num5 + (k - top) * num6;
					System.Windows.Forms.ListViewItem listViewItem = listView.Items[k];
					if (listViewItem.Selected)
					{
						canvas.FillRectangle(num + 1, ry, rowW - 2, num6, 865730559u);
						canvas.DrawTextAligned(listViewItem.Text, num + 6, (float)ry + (float)num6 / 2f, "12px sans-serif", uint.MaxValue, TextHAlign.Left, TextVAlign.Middle);
					}
					else
					{
						canvas.DrawTextAligned(listViewItem.Text, num + 6, (float)ry + (float)num6 / 2f, "12px sans-serif", 286331391u, TextHAlign.Left, TextVAlign.Middle);
					}
				}
				if (scroll)
				{
					// Native-style scrollbar: track + proportional thumb.
					int trackX = num + width - ListViewScrollW;
					int trackY = num2 + num3;
					int trackH = height - num3;
					canvas.FillRectangle(trackX, trackY, ListViewScrollW, trackH, 0xE4E4E4FFu);
					canvas.DrawLine(trackX, trackY, trackX, trackY + trackH, 1f, 0xBDBDBDFFu);
					int thumbH = Math.Max(20, trackH * cap / count);
					int thumbY = trackY + (count - cap <= 0 ? 0 : (trackH - thumbH) * top / (count - cap));
					canvas.FillRectangle(trackX + 2, thumbY, ListViewScrollW - 4, thumbH, 0x9E9E9EFFu);
				}
			}
			else if (control is System.Windows.Forms.ToolStrip toolStrip)
			{
				uint rgba7 = ToRgba(toolStrip.BackColor, 4126603263u);
				canvas.FillRectangle(num, num2, width, height, rgba7);
				canvas.DrawLine(num, num2 + height, num + width, num2 + height, 1f, 3755991039u);
				for (int l = 0; l < toolStrip.Items.Count; l++)
				{
					System.Windows.Forms.ToolStripItem toolStripItem = toolStrip.Items[l];
					if (!toolStripItem.Visible || toolStripItem.Width <= 0 || toolStripItem.Height <= 0 || toolStripItem.IsOnOverflow || toolStripItem.Bounds.Y >= toolStrip.Height || toolStripItem.Bounds.X >= toolStrip.Width)
					{
						continue;
					}
					int num7 = num + toolStripItem.Bounds.X;
					int num8 = num2 + toolStripItem.Bounds.Y;
					int width3 = toolStripItem.Width;
					int height2 = toolStripItem.Height;
					if (toolStripItem is System.Windows.Forms.ToolStripSeparator)
					{
						canvas.DrawLine(num7 + width3 / 2, num8 + 3, num7 + width3 / 2, num8 + height2 - 3, 1f, 3503345919u);
						continue;
					}
					bool flag2 = toolStripItem is System.Windows.Forms.ToolStripButton toolStripButton && toolStripButton.Checked;
					if (toolStripItem.Selected || toolStripItem.Pressed || flag2)
					{
						uint rgba8 = ((toolStripItem.Pressed || flag2) ? 3151557375u : 3437557759u);
						canvas.FillRoundedRectangle(num7, num8, width3, height2, 2f, rgba8);
						canvas.DrawLine(num7, num8, num7 + width3, num8, 1f, 7919615u);
						canvas.DrawLine(num7, num8 + height2, num7 + width3, num8 + height2, 1f, 7919615u);
						canvas.DrawLine(num7, num8, num7, num8 + height2, 1f, 7919615u);
						canvas.DrawLine(num7 + width3, num8, num7 + width3, num8 + height2, 1f, 7919615u);
					}
					int num9 = width3;
					if (toolStripItem is System.Windows.Forms.ToolStripSplitButton || toolStripItem is System.Windows.Forms.ToolStripDropDownButton)
					{
						num9 = Math.Max(16, width3 - 12);
						canvas.DrawLine(num7 + num9, num8 + 3, num7 + num9, num8 + height2 - 3, 1f, 3503345919u);
						canvas.DrawTextAligned("\u25be", num7 + width3 - 6, (float)num8 + (float)height2 / 2f, "9px sans-serif", 1145324799u, TextHAlign.Center, TextVAlign.Middle);
					}
					System.Windows.Forms.ToolStripItemDisplayStyle displayStyle = toolStripItem.DisplayStyle;
					bool flag3 = toolStripItem.Image is Bitmap { Width: >0 } bitmap && bitmap.Height > 0;
					string text2 = CleanItemText(toolStripItem.Text);
					bool flag4 = !string.IsNullOrEmpty(text2);
					switch (displayStyle)
					{
					case System.Windows.Forms.ToolStripItemDisplayStyle.Image:
					{
						if (flag3)
						{
							PaintCenteredBitmap(canvas, (Bitmap)toolStripItem.Image, num7, num8, num9, height2, toolStrip);
							break;
						}
						string itemGlyph = GetItemGlyph(toolStripItem.Name, l);
						canvas.DrawTextAligned(itemGlyph, (float)num7 + (float)num9 / 2f, (float)num8 + (float)height2 / 2f, "11px sans-serif", 1145324799u, TextHAlign.Center, TextVAlign.Middle);
						break;
					}
					case System.Windows.Forms.ToolStripItemDisplayStyle.Text:
						if (flag4)
						{
							uint rgba12 = ToRgba(toolStripItem.ForeColor, 572662527u);
							canvas.DrawTextAligned(text2, (float)num7 + (float)num9 / 2f, (float)num8 + (float)height2 / 2f, "12px 'Segoe UI', sans-serif", rgba12, TextHAlign.Center, TextVAlign.Middle);
						}
						break;
					case System.Windows.Forms.ToolStripItemDisplayStyle.ImageAndText:
						if (flag3 && flag4 && num9 >= ((Bitmap)toolStripItem.Image).Width + 24)
						{
							Bitmap bitmap2 = (Bitmap)toolStripItem.Image;
							PaintCenteredBitmap(canvas, bitmap2, num7 + 2, num8, bitmap2.Width, height2, toolStrip);
							uint rgba10 = ToRgba(toolStripItem.ForeColor, 572662527u);
							canvas.DrawTextAligned(text2, num7 + bitmap2.Width + 4, (float)num8 + (float)height2 / 2f, "12px 'Segoe UI', sans-serif", rgba10, TextHAlign.Left, TextVAlign.Middle);
						}
						else if (flag3)
						{
							PaintCenteredBitmap(canvas, (Bitmap)toolStripItem.Image, num7, num8, num9, height2, toolStrip);
						}
						else if (flag4)
						{
							uint rgba11 = ToRgba(toolStripItem.ForeColor, 572662527u);
							canvas.DrawTextAligned(text2, (float)num7 + (float)num9 / 2f, (float)num8 + (float)height2 / 2f, "12px 'Segoe UI', sans-serif", rgba11, TextHAlign.Center, TextVAlign.Middle);
						}
						break;
					default:
						if (flag3)
						{
							PaintCenteredBitmap(canvas, (Bitmap)toolStripItem.Image, num7, num8, num9, height2, toolStrip);
						}
						else if (flag4)
						{
							uint rgba9 = ToRgba(toolStripItem.ForeColor, 572662527u);
							canvas.DrawTextAligned(text2, (float)num7 + (float)num9 / 2f, (float)num8 + (float)height2 / 2f, "12px 'Segoe UI', sans-serif", rgba9, TextHAlign.Center, TextVAlign.Middle);
						}
						break;
					case System.Windows.Forms.ToolStripItemDisplayStyle.None:
						break;
					}
				}
				if (control.Controls.Count > 0)
				{
					RenderControls(canvas, control.Controls, num, num2);
				}
			}
			else if (control.GetType().Name.Contains("DocumentWorkspacesPanel"))
			{
				canvas.FillRectangle(num, num2, width, height, 1852731135u);
				object activeDocumentWorkspace = GetActiveDocumentWorkspace(control);
				int docW;
				int docH;
				object activeDocumentSurface = GetActiveDocumentSurface(activeDocumentWorkspace, out docW, out docH);
				if (docW <= 0)
				{
					docW = 800;
				}
				if (docH <= 0)
				{
					docH = 600;
				}
				double zoom = ReadWorkspaceZoom(activeDocumentWorkspace);
				ReadViewportOffset(activeDocumentWorkspace, out var offX, out var offY);
				ZoomedBlit zoomedBlit = DocumentZoom.ComputeZoomedBlit(num, num2, width, height, docW, docH, zoom, offX, offY);
				_docZoomBlit = ((zoomedBlit.SrcW > 0 && zoomedBlit.SrcH > 0) ? zoomedBlit : null);
				if (zoomedBlit.DestW > 100f && zoomedBlit.DestH > 100f)
				{
					int destX = zoomedBlit.DestX;
					int destY = zoomedBlit.DestY;
					float destW = zoomedBlit.DestW;
					float destH = zoomedBlit.DestH;
					_documentPageBounds = new Rectangle(destX, destY, (int)destW, (int)destH);
					_documentOriginalSize = new Size(docW, docH);
					canvas.FillRectangle(destX + 4, destY + 4, destW, destH, 64u);
					canvas.FillRectangle(destX, destY, destW, destH, uint.MaxValue);
					if (activeDocumentSurface != null)
					{
						// Live tool previews (brush dabs) render into a
						// layer OVERLAY that the raw surface below does
						// not contain: mid-drag reads came back blank and
						// the stroke only appeared at commit. While a
						// stroke is in flight, read the vendor compositor
						// (layers + overlays) instead. Any failure falls
						// back to the raw read; the working path is never
						// at risk. (Per-frame cost only during drags.)
						byte[]? array = null;
						if (_isDrawingOnDocument)
						{
							array = ExtractCompositedRgba(activeDocumentWorkspace, docW, docH);
						}
						array = array ?? ExtractSurfaceRgba(activeDocumentSurface, docW, docH);
						if (array != null && array.Length != 0)
						{
							if (zoomedBlit.SrcX != 0 || zoomedBlit.SrcY != 0 || zoomedBlit.SrcW != docW || zoomedBlit.SrcH != docH)
							{
								array = DocumentZoom.CropRgba(array, docW, docH, zoomedBlit.SrcX, zoomedBlit.SrcY, zoomedBlit.SrcW, zoomedBlit.SrcH);
							}
							RecordBitmap(s_docLayerKey, canvas, array, zoomedBlit.SrcW, zoomedBlit.SrcH, destX, destY, destW, destH);
						}
					}
					canvas.DrawLine(destX, destY, (float)destX + destW, destY, 1f, 2576980479u);
					canvas.DrawLine(destX, (float)destY + destH, (float)destX + destW, (float)destY + destH, 1f, 2576980479u);
					canvas.DrawLine(destX, destY, destX, (float)destY + destH, 1f, 2576980479u);
					canvas.DrawLine((float)destX + destW, destY, (float)destX + destW, (float)destY + destH, 1f, 2576980479u);
				}
				if (control.Controls.Count > 0)
				{
					RenderControls(canvas, control.Controls, num, num2);
				}
			}
			else if (control.GetType().Name.Contains("StatusBar") || control is System.Windows.Forms.StatusStrip)
			{
				canvas.FillRectangle(num, num2, width, height, 3756189183u);
				canvas.DrawLine(num, num2, num + width, num2, 1f, 2964699903u);
				canvas.DrawTextAligned("Ready", num + 12, (float)num2 + (float)height / 2f, "11px 'Segoe UI', sans-serif", 858993663u, TextHAlign.Left, TextVAlign.Middle);
				if (control.Controls.Count > 0)
				{
					RenderControls(canvas, control.Controls, num, num2);
				}
			}
			else if (control.GetType().Name.Contains("SliderImpl"))
			{
				int num10 = num2 + height / 2;
				int num11 = Math.Max(20, width - 45);
				canvas.DrawLine(num, num10, num + num11, num10, 2f, 2576980479u);
				canvas.DrawEllipse(num + num11 / 2 - 4, num10 - 4, 8f, 8f, 1f, 7919615u, 7919615u);
				canvas.DrawTextAligned("100%", num + num11 + 6, num10, "11px 'Segoe UI', sans-serif", 572662527u, TextHAlign.Left, TextVAlign.Middle);
			}
			else if (control.GetType().Name.Contains("CaptionControl") || control.GetType().Name.Contains("DocumentWorkspace") || control.GetType().Name.Contains("ScrollableCanvasControl") || control.GetType().Name.Contains("CanvasControl"))
			{
				if (control.Controls.Count > 0)
				{
					RenderControls(canvas, control.Controls, num, num2);
				}
			}
			else
			{
				bool flag5 = TryRenderSelfPainted(canvas, control, num, num2, width, height);
				if (!flag5 && !control.BackColor.IsEmpty && control.BackColor != Color.Transparent)
				{
					canvas.FillRectangle(num, num2, width, height, ToRgba(control.BackColor, 4042322175u));
				}
				if (!flag5 && !string.IsNullOrEmpty(control.Text) && control.Controls.Count == 0)
				{
					canvas.DrawTextAligned(control.Text, num + 4, num2 + 12, GetFontCss(control.Font), ToRgba(control.ForeColor, 255u), TextHAlign.Left, TextVAlign.Middle);
				}
				if (!flag5)
				{
					TryRenderD2DControl(canvas, control, num, num2, width, height);
				}
				if (control.Controls.Count > 0)
				{
					RenderControls(canvas, control.Controls, num, num2);
				}
			}
			BlitOverlay(canvas, control, num, num2);
		}
	}

	private static void BlitOverlay(ICanvas canvas, System.Windows.Forms.Control control, int absX, int absY)
	{
		try
		{
			if (IsFrameDirty(control))
			System.Windows.Forms.NativeWindow.WasmNotifyPaint(control.Handle);
			if (System.Windows.Forms.WasmOverlayBridge.TryGetOverlay(control.Handle, control.ClientSize, out Bitmap? overlay) && overlay != null)
			{
				byte[]? rgba = ExtractBitmapRgba(overlay);
				if (rgba != null)
					canvas.DrawImage(rgba, overlay.Width, overlay.Height, absX, absY, overlay.Width, overlay.Height);
			}
		}
		catch
		{
		}
	}

	public void OnMouseDown(int x, int y, System.Windows.Forms.MouseButtons buttons = System.Windows.Forms.MouseButtons.Left, bool shift = false, bool ctrl = false, bool alt = false)
	{
		WasmDialogHost.AdvanceTurn();
		Console.WriteLine($"[Host] OnMouseDown(x={x}, y={y}, buttons={buttons} mod={ToKeys(shift, ctrl, alt)})");
        FeedInput(x, y, _mouseButtons | buttons, ToKeys(shift, ctrl, alt));
		if (_activeForm == null)
		{
			return;
		}
		// Chorded presses accumulate (desktop MouseButtons flags); release clears.
		_mouseButtons |= buttons;
		System.Windows.Forms.WasmMouseState.SetModifiers(ToKeys(shift, ctrl, alt));
		FeedInput(x, y, _mouseButtons, ToKeys(shift, ctrl, alt));
		NotePress(x, y, buttons);
		if (RoutePopupMouseDown(x, y, buttons))
		{
			return;
		}
		if (TopModalWindow(out System.Windows.Forms.Form modal) && modal != null)
		{
			if (!BeginDialogDrag(modal, x, y, "modal"))
			{
				RecordInput("down", $"{x},{y} {buttons} modal={modal.Name}");
				_mouseTarget = HitTestDeep(modal.Controls, modal.Location.X, modal.Location.Y + ChromeHeight(modal), x, y);
				System.Windows.Forms.WasmRepaintGate.MarkDirtyChain(_mouseTarget);
				NoteFocus(_mouseTarget);
				if (_mouseTarget != null && !(_mouseTarget is System.Windows.Forms.ToolStrip))
				{
					RaiseMouseEvent(s_onMouseDownMethod, _mouseTarget, buttons, 1, x, y);
				}
			}
			return;
		}
		if (TryHitDropdown(x, y, out System.Windows.Forms.ToolStripDropDown _, out System.Windows.Forms.ToolStripItem _, out int _, out int _))
		{
			RecordInput("down", $"{x},{y} {buttons} dropdown");
			return;
		}
		if (_documentPageBounds.Width > 0 && _documentPageBounds.Contains(x, y))
		{
			if (!DownTrackedDock(x, y, buttons))
			{
				Console.WriteLine($"[Host] OnMouseDown hit document canvas: {_documentPageBounds}");
				RecordInput("down", $"{x},{y} {buttons} doc tool={GetActiveDocToolName()}");
				_isDrawingOnDocument = true;
				System.Windows.Forms.WasmRepaintGate.MarkGlobalDirty();
				Console.WriteLine("[Host] OnMouseDown doc tool=" + GetActiveDocToolName());
				ForwardDocumentPointer(x, y, buttons, PointerEventKind.Pressed);
			}
			return;
		}
		int x2 = _activeForm.Location.X;
		int y2 = _activeForm.Location.Y;
		int width = _activeForm.Width;
		int num = ChromeHeight(_activeForm);
		if (x >= x2 && x <= x2 + width && y >= y2 && y < y2 + num)
		{
			Console.WriteLine("[Host] OnMouseDown hit title bar");
			RecordInput("down", $"{x},{y} {buttons} titlebar");
			_isDraggingTitle = true;
			_dragStartX = x;
			_dragStartY = y;
			_dragForm = _activeForm;
			_dragInitialFormX = x2;
			_dragInitialFormY = y2;
			_mouseTarget = null;
			return;
		}
		int parentAbsY = y2 + num;
		System.Windows.Forms.Control control = HitTestDeep(_activeForm.Controls, x2, parentAbsY, x, y);
		if (!DownTrackedDock(x, y, buttons))
		{
			Console.WriteLine("[Host] OnMouseDown hit=" + ((control == null) ? "null" : (control.GetType().Name + " '" + control.Name + "'")));
			Console.WriteLine($"[Host] form loc={_activeForm.Location} size={_activeForm.Width}x{_activeForm.Height} kids={_activeForm.Controls.Count} chrome={ChromeHeight(_activeForm)} view={ViewWidth}x{ViewHeight}");
			Console.WriteLine("[Host] kids=" + DescribeKids(_activeForm.Controls, 0));
			if (control is System.Windows.Forms.Button pressedButton)
			{
				_pressedButton = pressedButton;
			}
			NoteFocus(control);
			_mouseTarget = control;
			if (_mouseTarget is not null)
				MarkCursorNeighborhood(_mouseTarget, x, y, 128);
			RecordInput("down", $"{x},{y} {buttons} target={DescribeControl(control)}");
			if (control != null && !(control is System.Windows.Forms.ToolStrip))
			{
				Console.WriteLine("[Host] Invoking RaiseMouseEvent OnMouseDown on " + control.Name);
				RaiseMouseEvent(s_onMouseDownMethod, control, buttons, 1, x, y);
				Console.WriteLine("[Host] RaiseMouseEvent OnMouseDown done");
			}
		}
	}

	public void OnMouseUp(int x, int y, System.Windows.Forms.MouseButtons buttons = System.Windows.Forms.MouseButtons.Left, bool shift = false, bool ctrl = false, bool alt = false)
	{
		WasmDialogHost.AdvanceTurn();
		Console.WriteLine($"[Host] OnMouseUp(x={x}, y={y}, buttons={buttons} mod={ToKeys(shift, ctrl, alt)})");
		_dragForm = null;
		bool isDrawingOnDocument = _isDrawingOnDocument;
		if (_isDrawingOnDocument)
		{
			ForwardDocumentPointer(x, y, buttons, PointerEventKind.Released);
			System.Windows.Forms.WasmRepaintGate.MarkGlobalDirty();
			_isDrawingOnDocument = false;
			Console.WriteLine("[Host] PostStroke surface=" + DescribeActiveDocSurface());
		}
		_isDraggingTitle = false;
		_pressedButton = null;
		_mouseButtons &= ~buttons;
		System.Windows.Forms.WasmMouseState.SetModifiers(ToKeys(shift, ctrl, alt));
		if (_mouseTarget != null)
		{
			System.Windows.Forms.WasmRepaintGate.MarkDirtyChain(_mouseTarget);
			RaiseMouseEvent(s_onMouseUpMethod, _mouseTarget, buttons, 1, x, y);
			_mouseTarget = null;
		}
		FeedInput(x, y, _mouseButtons, ToKeys(shift, ctrl, alt));
		PairClick(x, y, buttons);
        RecordInput("up", isDrawingOnDocument ? $"{x},{y} {buttons} doc" : $"{x},{y} {buttons}");
		UpdateHover(x, y);
	}

	public void OnMouseMove(int x, int y, bool shift = false, bool ctrl = false, bool alt = false)
	{
		WasmDialogHost.AdvanceTurn();
		System.Windows.Forms.WasmMouseState.SetModifiers(ToKeys(shift, ctrl, alt));
		NoteMoveForHover(x, y);
		if (_isDrawingOnDocument)
		{
			FeedInput(x, y, _mouseButtons, ToKeys(shift, ctrl, alt));
            ForwardDocumentPointer(x, y, _mouseButtons, PointerEventKind.Moved);
            System.Windows.Forms.WasmRepaintGate.MarkGlobalDirty();
			long tickCount = Environment.TickCount64;
			if (tickCount - _lastDocMoveMs >= 100)
			{
				_lastDocMoveMs = tickCount;
				RecordInput("move", $"{x},{y} doc");
			}
		}
		else
		{
			FeedInput(x, y, _mouseButtons, ToKeys(shift, ctrl, alt));
            System.Windows.Forms.Form form = _dragForm ?? _activeForm;
			if (_isDraggingTitle && form != null)
			{
				int num = x - _dragStartX;
				int num2 = y - _dragStartY;
				form.Location = new Point(_dragInitialFormX + num, _dragInitialFormY + num2);
				_userPlaced.Add(form);
				System.Windows.Forms.WasmRepaintGate.MarkDirtyChain(form);
			}
			else if (_mouseTarget != null)
			{
				MarkCursorNeighborhood(_mouseTarget, x, y);
				RaiseMouseEvent(s_onMouseMoveMethod, _mouseTarget, _mouseButtons, 0, x, y);
			}
			else
			{
				UpdateHover(x, y);
			}
		}
	}
	// Cursor-neighborhood backstop mark (press + drag moves): the app invalidates
	// its own drag damage (tight rects); the host only needs a cursor-neighborhood backstop
	// for visuals no app Invalidate covers (press/hover state). Small
	// targets keep the full chain mark (cheap); large self-painted
	// targets get a cursor rect so region repaint stays engaged. A
	// cursor outside the target falls back to the full chain (rare,
	// and a superset is always safe).
	private void MarkCursorNeighborhood(System.Windows.Forms.Control target, int x, int y, int half = 48)
	{
		if (_activeForm is null || (long)target.Width * target.Height <= 65536)
		{
			System.Windows.Forms.WasmRepaintGate.MarkDirtyChain(target);
			return;
		}
		int ax = GetControlAbsX(target, _activeForm.Location.X);
		int ay = GetControlAbsY(target, _activeForm.Location.Y + ChromeHeight(_activeForm));
		var rc = new Rectangle(x - ax - half, y - ay - half, half * 2, half * 2);
		rc = Rectangle.Intersect(rc, new Rectangle(0, 0, target.Width, target.Height));
		if (rc.Width <= 0 || rc.Height <= 0)
		{
			System.Windows.Forms.WasmRepaintGate.MarkDirtyChain(target);
			return;
		}
		System.Windows.Forms.WasmRepaintGate.MarkDirtyChainRect(target, rc);
	}

	private void UpdateHover(int x, int y)
	{
		if (_activeForm == null)
		{
			return;
		}
		int x2 = _activeForm.Location.X;
		int y2 = _activeForm.Location.Y;
		int num = y2 + ChromeHeight(_activeForm);
		if (TopPopupAt(x, y, out System.Windows.Forms.Control? popupHover) && popupHover != null)
		{
			System.Windows.Forms.Control hoverTarget = HitTestDeep(popupHover.Controls, popupHover.Location.X, popupHover.Location.Y, x, y) ?? popupHover;
			SetHoverControl(hoverTarget);
			RaiseMouseEvent(s_onMouseMoveMethod, hoverTarget, _mouseButtons, 0, x, y);
			return;
		}
		if (TryHitDropdown(x, y, out System.Windows.Forms.ToolStripDropDown drop, out System.Windows.Forms.ToolStripItem item, out int dx, out int dy))
		{
			SetHoverControl(null);
			SetHoverItem(item);
			if (item != null)
			{
				MaybeSweepDropMenu(drop, item, dx, dy);
				RaiseItemMove(item, x - dx, y - dy);
			}
			return;
		}
		SetHoverItem(null);
		if (TopTrackedWindowAt(x, y, out System.Windows.Forms.Form win, out bool isModal) && win != null && !isModal)
		{
			System.Windows.Forms.Control control = HitTestDeep(win.Controls, win.Location.X, win.Location.Y + ChromeHeight(win), x, y);
			if (control is System.Windows.Forms.ToolStrip toolStrip)
			{
				int controlAbsX = GetControlAbsX(toolStrip, win.Location.X);
				int controlAbsY = GetControlAbsY(toolStrip, win.Location.Y + ChromeHeight(win));
				System.Windows.Forms.ToolStripItem itemAt = toolStrip.GetItemAt(x - controlAbsX, y - controlAbsY);
				SetHoverControl(toolStrip);
				SetHoverItem(itemAt);
				if (itemAt != null)
				{
					RaiseItemMove(itemAt, x - controlAbsX, y - controlAbsY);
				}
			}
			else
			{
				SetHoverControl(control);
				if (control != null)
				{
					RaiseMouseEvent(s_onMouseMoveMethod, control, _mouseButtons, 0, x, y);
				}
			}
			return;
		}
		if (TopModalWindow(out System.Windows.Forms.Form modal) && modal != null)
		{
			System.Windows.Forms.Control control2 = HitTestDeep(modal.Controls, modal.Location.X, modal.Location.Y + ChromeHeight(modal), x, y);
			if (control2 is System.Windows.Forms.ToolStrip toolStrip2)
			{
				int controlAbsX2 = GetControlAbsX(toolStrip2, modal.Location.X);
				int controlAbsY2 = GetControlAbsY(toolStrip2, modal.Location.Y + ChromeHeight(modal));
				System.Windows.Forms.ToolStripItem itemAt2 = toolStrip2.GetItemAt(x - controlAbsX2, y - controlAbsY2);
				SetHoverControl(toolStrip2);
				SetHoverItem(itemAt2);
				if (itemAt2 != null)
				{
					RaiseItemMove(itemAt2, x - controlAbsX2, y - controlAbsY2);
				}
			}
			else
			{
				SetHoverControl(control2);
				if (control2 != null)
				{
					RaiseMouseEvent(s_onMouseMoveMethod, control2, _mouseButtons, 0, x, y);
				}
			}
			return;
		}
		if (_documentPageBounds.Width > 0 && _documentPageBounds.Contains(x, y))
		{
			SetHoverControl(null);
			return;
		}
		System.Windows.Forms.Control control3 = HitTestDeep(_activeForm.Controls, x2, num, x, y);
		if (control3 is System.Windows.Forms.ToolStrip toolStrip3)
		{
			int controlAbsX3 = GetControlAbsX(toolStrip3, x2);
			int controlAbsY3 = GetControlAbsY(toolStrip3, num);
			System.Windows.Forms.ToolStripItem itemAt3 = toolStrip3.GetItemAt(x - controlAbsX3, y - controlAbsY3);
			SetHoverControl(toolStrip3);
			SetHoverItem(itemAt3);
			if (itemAt3 != null)
			{
				MaybeSweepMenu(toolStrip3, itemAt3, controlAbsX3, controlAbsY3);
				RaiseItemMove(itemAt3, x - controlAbsX3, y - controlAbsY3);
			}
		}
		else
		{
			SetHoverControl(control3);
			if (control3 != null)
			{
				RaiseMouseEvent(s_onMouseMoveMethod, control3, _mouseButtons, 0, x, y);
			}
		}
	}

	/// <summary>Host-side focus: last control the user pressed. The fork
	/// has no HWND focus to read, so clicks are the focus signal; key
	/// delivery follows it (FIX2). ActiveControl is kept in sync so app
	/// code reading it sees the same control.</summary>
	private void NoteFocus(System.Windows.Forms.Control? control)
	{
		_focusedControl = control;
		_focusedTextBox = control as System.Windows.Forms.TextBox;
		if (control == null)
		{
			return;
		}
		try
		{
			for (System.Windows.Forms.Control? current = control; current != null; current = current.Parent)
			{
				if (current is System.Windows.Forms.Form form)
				{
					form.ActiveControl = control;
					break;
				}
			}
		}
		catch
		{
		}
	}
	// The fork reads Form.ActiveForm / GetForegroundWindow from the OS
	// foreground window, which never updates headless (see WasmForeground);
	// publish the host's active form there on every switch/teardown.
	// Never throws: foreground state must not break scene select.
	private static void SyncForkForeground(System.Windows.Forms.Form? form)
	{
		try
		{
			if (form == null || form.IsDisposed)
			{
				System.Windows.Forms.WasmForeground.Clear();
				return;
			}
			try
			{
				System.Windows.Forms.WasmForeground.Set((nint)form.Handle);
			}
			catch
			{
				System.Windows.Forms.WasmForeground.Clear();
			}
		}
		catch
		{
		}
	}
	private void SetHoverControl(System.Windows.Forms.Control? next)
	{
		if (_hoverControl != next)
		{
			System.Windows.Forms.Control hoverControl = _hoverControl;
			_hoverControl = next;
			System.Windows.Forms.WasmRepaintGate.MarkDirtyChain(next);
			System.Windows.Forms.WasmRepaintGate.MarkDirtyChain(hoverControl);
			if (hoverControl != null)
			{
				RaiseSimpleEvent(s_onMouseLeaveMethod, hoverControl);
			}
			if (next != null)
			{
				RaiseSimpleEvent(s_onMouseEnterMethod, next);
			}
			if (next != hoverControl)
			{
				RecordInput("hover", "control=" + DescribeControl(next));
			}
		}
	}

	private void SetHoverItem(System.Windows.Forms.ToolStripItem? next)
	{
		if (_hoverItem == next)
		{
			return;
		}
		System.Windows.Forms.ToolStripItem hoverItem = _hoverItem;
		_hoverItem = next;
		System.Windows.Forms.WasmRepaintGate.MarkDirtyChain(next?.Owner);
		System.Windows.Forms.WasmRepaintGate.MarkDirtyChain(hoverItem?.Owner);
		if (hoverItem != null)
		{
			try
			{
				s_itemUnselectMethod?.Invoke(hoverItem, Array.Empty<object>());
				s_itemMouseLeaveMethod?.Invoke(hoverItem, new object[1] { EventArgs.Empty });
			}
			catch (Exception ex)
			{
				Console.WriteLine("[WinFormsAppHost] Item leave error: " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]);
			}
		}
		if (next == null)
		{
			return;
		}
		try
		{
			next.Select();
			s_itemMouseEnterMethod?.Invoke(next, new object[1] { EventArgs.Empty });
			Console.WriteLine($"[Host] Hover item: {next.GetType().Name} '{next.Name}' selected={next.Selected}");
			RecordInput("hover", "item=" + DescribeItem(next));
		}
		catch (Exception ex2)
		{
			Console.WriteLine("[WinFormsAppHost] Item enter error: " + ex2.GetType().Name + ": " + ex2.Message.Split('\n')[0]);
		}
	}

	private void RaiseSimpleEvent(MethodInfo? method, System.Windows.Forms.Control target)
	{
		if (method == null || _activeForm == null)
		{
			return;
		}
		// FIX3/G6: entering/leaving a disposed control raises into torn-down
		// state (Telerik RadControl.OnMouseLeave NRE); desktop never delivers
		// events to disposed controls, so skip.
		if (target.IsDisposed)
		{
			return;
		}
		try
		{
			method.Invoke(target, new object[1] { EventArgs.Empty });
		}
		catch (Exception ex)
		{
			Console.WriteLine("[WinFormsAppHost] Mouse enter/leave error: " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]);
			Console.WriteLine("[WinFormsAppHost] Mouse enter/leave error: root=" + RootCause(ex).GetType().Name + " site=" + SiteOf(RootCause(ex)) + " msg=" + (RootCause(ex).Message ?? "").Split('\n')[0]);
			try { Console.WriteLine("[diagstack] " + string.Join(" <- ", new System.Diagnostics.StackTrace(RootCause(ex), false).GetFrames().Take(12).Select(f => f.GetMethod()))); } catch { }
			BrowserErrorReporter.Report("hover", ex, SiteOf(RootCause(ex)));
		}
	}

	private void RaiseItemMove(System.Windows.Forms.ToolStripItem item, int lx, int ly)
	{
		try
		{
			System.Windows.Forms.MouseEventArgs e = new System.Windows.Forms.MouseEventArgs(_mouseButtons, 0, lx, ly, 0);
			s_itemMouseMoveMethod?.Invoke(item, new object[1] { e });
		}
		catch (Exception ex)
		{
			Console.WriteLine("[WinFormsAppHost] Item move error: " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]);
		}
	}

	private void MaybeSweepMenu(System.Windows.Forms.ToolStrip ts, System.Windows.Forms.ToolStripItem item, int ox, int oy)
	{
		System.Windows.Forms.ToolStripMenuItem mi = item as System.Windows.Forms.ToolStripMenuItem;
		if (mi == null || !mi.HasDropDownItems)
		{
			return;
		}
		List<(System.Windows.Forms.ToolStripDropDown, int, int)> openDropdowns = GetOpenDropdowns();
		if (openDropdowns.Count == 0 || openDropdowns.Any<(System.Windows.Forms.ToolStripDropDown, int, int)>(((System.Windows.Forms.ToolStripDropDown Drop, int Ox, int Oy) d) => d.Drop == mi.DropDown))
		{
			return;
		}
		foreach (var item3 in openDropdowns.ToList())
		{
			System.Windows.Forms.ToolStripDropDown item2 = item3.Item1;
			try
			{
				item2.Visible = false;
			}
			catch
			{
			}
		}
		_openDropdowns.Clear();
		OpenMenuDropdown(ts, mi, ox, oy);
	}

	private void MaybeSweepDropMenu(System.Windows.Forms.ToolStripDropDown parentDrop, System.Windows.Forms.ToolStripItem item, int dx, int dy)
	{
		foreach (var item3 in GetOpenDropdowns().ToList())
		{
			System.Windows.Forms.ToolStripDropDown item2 = item3.Drop;
			if (item2 != parentDrop && IsOwnedBy(item2, parentDrop) && (!(item is System.Windows.Forms.ToolStripMenuItem { HasDropDownItems: not false } toolStripMenuItem) || item2 != toolStripMenuItem.DropDown))
			{
				try
				{
					item2.Visible = false;
				}
				catch
				{
				}
			}
		}
		System.Windows.Forms.ToolStripMenuItem mi = item as System.Windows.Forms.ToolStripMenuItem;
		if (mi != null && mi.HasDropDownItems && !GetOpenDropdowns().Any<(System.Windows.Forms.ToolStripDropDown, int, int)>(((System.Windows.Forms.ToolStripDropDown Drop, int Ox, int Oy) d) => d.Drop == mi.DropDown))
		{
			try
			{
				mi.ShowDropDown();
			}
			catch (Exception ex)
			{
				Console.WriteLine("[WinFormsAppHost] Submenu open error: " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]);
				BrowserErrorReporter.Report("dropdown-open", ex);
			}
			TrackDropdown(mi.DropDown, dx, dy);
			Console.WriteLine($"[Host] Submenu opened: '{mi.Name}' drop={mi.DropDown.Bounds} visible={mi.DropDown.Visible}");
		}
	}

	private static bool IsOwnedBy(System.Windows.Forms.ToolStripDropDown drop, System.Windows.Forms.ToolStripDropDown ancestor)
	{
		System.Windows.Forms.ToolStripDropDown toolStripDropDown = drop;
		while (toolStripDropDown != null)
		{
			if (toolStripDropDown == ancestor)
			{
				return true;
			}
			System.Windows.Forms.ToolStripItem ownerItem;
			try
			{
				ownerItem = toolStripDropDown.OwnerItem;
			}
			catch
			{
				return false;
			}
			toolStripDropDown = ownerItem?.Owner as System.Windows.Forms.ToolStripDropDown;
		}
		return false;
	}

	private void RaiseMouseEvent(MethodInfo? method, System.Windows.Forms.Control target, System.Windows.Forms.MouseButtons buttons, int clicks, int x, int y, int delta = 0)
	{
		if (method == null || _activeForm == null)
		{
			return;
		}
		// Upstream WmMouseDown/WmMouseUp own the capture transitions around
		// the handlers (Capture=true before OnMouseDown, released exiting
		// MouseUp); the host bypasses WndProc, so it performs them here.
		// ToolStrip maintains its own capture mode upstream: leave it alone.
		bool isDown = method == s_onMouseDownMethod;
		bool isUp = method == s_onMouseUpMethod;
		if (isDown && !(target is System.Windows.Forms.ToolStrip))
		{
			try { target.Capture = true; } catch { }
		}
		try
		{
			int controlAbsX = GetControlAbsX(target, _activeForm.Location.X);
			int controlAbsY = GetControlAbsY(target, _activeForm.Location.Y + ChromeHeight(_activeForm));
			System.Windows.Forms.MouseEventArgs e = new System.Windows.Forms.MouseEventArgs(buttons, clicks, x - controlAbsX, y - controlAbsY, delta);
			method.Invoke(target, new object[1] { e });
		}
		catch (Exception ex)
		{
			Console.WriteLine("[WinFormsAppHost] Mouse event error: " + ex.GetType().Name + ": " + (ex.Message ?? "").Split('\n')[0] + " site=" + SiteOf(ex));
			Console.WriteLine("[WinFormsAppHost] Mouse event error: root=" + RootCause(ex).GetType().Name + " site=" + SiteOf(RootCause(ex)) + " msg=" + ((RootCause(ex).Message ?? "").Split('\n')[0]));
			try { Console.WriteLine("[diagstack] " + string.Join(" <- ", new System.Diagnostics.StackTrace(RootCause(ex), false).GetFrames().Take(12).Select(f => f.GetMethod()))); } catch { }
		}
		if (isUp)
		{
			// Only the holder releases (host hit-tests per event; desktop
			// delivers the up to the captor, which is the same guard).
			try
			{
				if (target.Capture)
				{
					target.Capture = false;
				}
			}
			catch { }
		}
	}

	// Activation runs on a qualified release (see PairClick), never on press:
	// drags must not click, and the second press of a double-click must not
	// re-activate (it raises DoubleClick instead).
	// Click/hover/wheel state machine (desktop parity). Tokens identify the
	// pressed/released target by reference across the modal/dropdown/doc/
	// title/client dispatch branches.
	private sealed class ClickToken : IEquatable<ClickToken>
	{
		public ClickToken(string kind, object? target) { Kind = kind; Target = target; }
		public string Kind { get; }
		public object? Target { get; }
		public bool Equals(ClickToken? other) => other is not null && Kind == other.Kind && ReferenceEquals(Target, other.Target);
		public override bool Equals(object? obj) => Equals(obj as ClickToken);
		public override int GetHashCode() => HashCode.Combine(Kind, System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Target));
	}

	internal static System.Windows.Forms.MouseButtons MapDomButton(long button) => button switch
	{
		1 => System.Windows.Forms.MouseButtons.Middle,
		2 => System.Windows.Forms.MouseButtons.Right,
		3 => System.Windows.Forms.MouseButtons.XButton1,
		4 => System.Windows.Forms.MouseButtons.XButton2,
		_ => System.Windows.Forms.MouseButtons.Left,
	};

	private static System.Windows.Forms.Keys ToKeys(bool shift, bool ctrl, bool alt) =>
		(shift ? System.Windows.Forms.Keys.Shift : 0) | (ctrl ? System.Windows.Forms.Keys.Control : 0) | (alt ? System.Windows.Forms.Keys.Alt : 0);

	// Feeds every headless input static from one call site: the fork mouse
	// state (WasmMouseState, read by GetCursorPos/GetKeyState stubs) and the
	// Control statics read it back through the stubs (no setters upstream).
	private static void FeedInput(int x, int y, System.Windows.Forms.MouseButtons buttons, System.Windows.Forms.Keys modifiers)
	{
		// Single source: the fork statics (Control.ModifierKeys/MouseButtons/
		// MousePosition) read the GetCursorPos/GetKeyState stubs, which read this.
		System.Windows.Forms.WasmMouseState.Update(x, y, buttons, modifiers);
	}

	// Side-effect-free twin of the down/up dispatch branches: resolves which
	// target a press/release belongs to. Keep the branch order in sync with
	// OnMouseDown/OnClick (modal, dropdown, document, title bar, client).
	private ClickToken ResolveClickToken(int x, int y)
	{
		if (_activeForm == null)
			return new ClickToken("none", null);
		if (TopModalWindow(out System.Windows.Forms.Form modal) && modal != null)
			return new ClickToken("modal", HitTestDeep(modal.Controls, modal.Location.X, modal.Location.Y + ChromeHeight(modal), x, y));
		if (TryHitDropdown(x, y, out System.Windows.Forms.ToolStripDropDown _, out System.Windows.Forms.ToolStripItem? item, out int _, out int _))
			return new ClickToken("dropdown", item);
		if (_documentPageBounds.Width > 0 && _documentPageBounds.Contains(x, y))
			return new ClickToken("doc", null);
		int fx = _activeForm.Location.X;
		int fy = _activeForm.Location.Y;
		int ch = ChromeHeight(_activeForm);
		if (x >= fx && x <= fx + _activeForm.Width && y >= fy && y < fy + ch)
			return new ClickToken("title", null);
		return new ClickToken("client", HitTestDeep(_activeForm.Controls, fx, fy + ch, x, y));
	}

	private void NotePress(int x, int y, System.Windows.Forms.MouseButtons buttons)
	{
		_downToken = ResolveClickToken(x, y);
		_downX = x;
		_downY = y;
		_downMs = TickNow();
		_downButton = buttons;
	}

	// Release pairing: a Click needs the same target + button as the press
	// with no drag past DragSize in between. A second qualifying Click
	// inside the double-click window/rect becomes a DoubleClick (no second
	// activation); anything else resets the chain.
	private void PairClick(int x, int y, System.Windows.Forms.MouseButtons buttons)
	{
		if (_activeForm == null || _downToken == null)
			return;
		ClickToken up = ResolveClickToken(x, y);
		System.Drawing.Size drag = System.Windows.Forms.SystemInformation.DragSize;
		bool sameTarget = up.Equals(_downToken);
		bool sameButton = buttons == _downButton;
		bool noDrag = Math.Abs(x - _downX) <= drag.Width && Math.Abs(y - _downY) <= drag.Height;
		_downToken = null;
		if (!sameTarget || !sameButton || !noDrag)
			return;
		long now = TickNow();
		System.Drawing.Size spread = System.Windows.Forms.SystemInformation.DoubleClickSize;
		bool isDouble = _lastClickToken is not null && up.Equals(_lastClickToken)
			&& buttons == _lastClickButton
			&& now - _lastClickMs <= System.Windows.Forms.SystemInformation.DoubleClickTime
			&& Math.Abs(x - _lastClickX) <= spread.Width && Math.Abs(y - _lastClickY) <= spread.Height;
		if (isDouble)
		{
			_lastClickToken = null;
			OnDoubleClickAt(up, buttons, x, y);
			return;
		}
		_lastClickToken = up;
		_lastClickX = x;
		_lastClickY = y;
		_lastClickMs = now;
		_lastClickButton = buttons;
		Console.WriteLine($"[Host] OnClick(x={x}, y={y}) paired button={buttons}");
		OnClick(x, y, buttons, 1);
	}

	// Double-click: control targets raise DoubleClick/MouseDoubleClick with
		// no second activation; chrome tokens (dropdown/title/doc) repeat the
		// normal activation (menus re-resolve, title/doc dispatch no-ops).
	private void OnDoubleClickAt(ClickToken token, System.Windows.Forms.MouseButtons buttons, int x, int y)
	{
		WasmDialogHost.AdvanceTurn();
		System.Windows.Forms.WasmRepaintGate.MarkGlobalDirty();
		if (token.Target is System.Windows.Forms.Control control)
		{
			Console.WriteLine($"[Host] OnDoubleClick target={control.GetType().Name} button={buttons}");
			RecordInput("doubleclick", $"{x},{y} {buttons} control={DescribeControl(control)}");
			RaiseMouseEvent(s_onDoubleClickMethod, control, buttons, 2, x, y);
			RaiseMouseEvent(s_onMouseDoubleClickMethod, control, buttons, 2, x, y);
		}
		else
		{
			Console.WriteLine($"[Host] OnDoubleClick chrome token={token.Kind}, repeated activation");
			OnClick(x, y, buttons, 1);
		}
	}

	// Raw DOM wheel (pixels/lines per deltaMode): accumulate fractional
		// notches (trackpads) and emit whole 120-unit steps, sign-flipped
		// (DOM down-positive, WinForms down-negative). DeltaX has no WinForms
		// event (HWHEEL is WndProc-only upstream) and stays ignored.
	public void OnMouseWheelRaw(int x, int y, double deltaX, double deltaY, int deltaMode, bool shift = false, bool ctrl = false, bool alt = false)
	{
		_ = deltaX;
		FeedInput(x, y, _mouseButtons, ToKeys(shift, ctrl, alt));
		if (_activeForm == null)
			return;
		double notches = deltaMode == 1 ? deltaY / 3.0 : deltaY / 100.0;
		_wheelRemainder += notches;
		int units = 0;
		while (_wheelRemainder >= 1.0) { units++; _wheelRemainder -= 1.0; }
		while (_wheelRemainder <= -1.0) { units--; _wheelRemainder += 1.0; }
		if (units == 0)
			return;
		int delta120 = -units * 120;
		System.Windows.Forms.Control? target = _hoverControl;
		target ??= HitTestDeep(_activeForm.Controls, _activeForm.Location.X, _activeForm.Location.Y + ChromeHeight(_activeForm), x, y);
		target ??= _activeForm;
		Console.WriteLine($"[Host] OnMouseWheel(x={x}, y={y}, delta={delta120})");
		RecordInput("wheel", $"{x},{y} delta={delta120}");
		// Native ListViews consume the wheel for scrolling (SystemInformation
		// MouseWheelScrollLines per notch); the scrolled rows repaint.
		// Win10+ parity (inactive scroll): the wheel scrolls the scrollable
		// control UNDER THE CURSOR, so hit-test the fresh position instead
		// of the stale hover/focus target.
		System.Windows.Forms.Control? wheelHit = HitTestDeep(_activeForm.Controls, _activeForm.Location.X, _activeForm.Location.Y + ChromeHeight(_activeForm), x, y);
		if (wheelHit is System.Windows.Forms.ListView wheelList && units != 0)
		{
			if (ScrollListView(wheelList, units * System.Windows.Forms.SystemInformation.MouseWheelScrollLines))
				System.Windows.Forms.WasmRepaintGate.MarkGlobalDirty();
		}
		RaiseMouseEvent(s_onMouseWheelMethod, target, _mouseButtons, 0, x, y, delta120);
	}

	// Hover stillness tracking: any move outside the hover rect restarts the
		// clock; target swaps restart via SetHoverControl. The timer below
		// fires the event; tests drive CheckHover directly (deterministic).
	private void NoteMoveForHover(int x, int y)
	{
		System.Drawing.Size hover = System.Windows.Forms.SystemInformation.MouseHoverSize;
		if (Math.Abs(x - _hoverX) > hover.Width || Math.Abs(y - _hoverY) > hover.Height)
		{
			_hoverX = x;
			_hoverY = y;
			_hoverMs = TickNow();
			_hoverFired = false;
		}
		if (_hoverTimer == null)
		{
			try { _hoverTimer = new System.Threading.Timer(static state => ((WinFormsAppHost)state!).FireHoverIfStill(), this, 100, 100); }
			catch { }
		}
	}

	private void FireHoverIfStill()
	{
		try { WinFormsExecution.Run(() => CheckHover(TickNow())); }
		catch { }
	}

	// Fires OnMouseHover once per rest (no buttons held, still inside the
		// hover rect past MouseHoverTime). Internal for deterministic tests.
	internal void CheckHover(long nowMs)
	{
		System.Windows.Forms.Control? hovered = _hoverControl;
		if (_hoverFired || _mouseButtons != System.Windows.Forms.MouseButtons.None || hovered == null)
			return;
		if (nowMs - _hoverMs < System.Windows.Forms.SystemInformation.MouseHoverTime)
			return;
		_hoverFired = true;
		Console.WriteLine($"[Host] OnMouseHover control={DescribeControl(hovered)}");
		RaiseSimpleEvent(s_onMouseHoverMethod, hovered);
	}
	public void OnClick(int x, int y, System.Windows.Forms.MouseButtons clickedButton = System.Windows.Forms.MouseButtons.Left, int clicks = 1)
	{
		WasmDialogHost.AdvanceTurn();
		Console.WriteLine($"[Host] OnClick(x={x}, y={y})");
		System.Windows.Forms.WasmRepaintGate.MarkGlobalDirty();
		if (_activeForm == null || ClickOpenDropdown(x, y))
		{
			return;
		}
		if (RoutePopupClick(x, y, clickedButton, clicks))
		{
			return;
		}
		if (TopModalWindow(out System.Windows.Forms.Form modal) && modal != null)
		{
			ModalOnClick(modal, x, y, "modal", clickedButton, clicks);
			return;
		}
		if (_documentPageBounds.Width > 0 && _documentPageBounds.Contains(x, y))
		{
			if (!ClickTrackedDock(x, y))
			{
				Console.WriteLine("[Host] OnClick skipped on document canvas");
			}
			return;
		}
		int x2 = _activeForm.Location.X;
		int y2 = _activeForm.Location.Y;
		int num = ChromeHeight(_activeForm);
		int num2 = y2 + num;
		CloseOpenDropdowns();
		System.Windows.Forms.Control control = HitTestDeep(_activeForm.Controls, x2, num2, x, y);
		if (ClickTrackedDock(x, y))
		{
			return;
		}
		Console.WriteLine("[Host] OnClick hit=" + ((control == null) ? "null" : (control.GetType().Name + " '" + control.Name + "'")));
		if (control == null)
		{
			return;
		}
		if (control is System.Windows.Forms.Button button)
		{
			RecordInput("click", $"{x},{y} button={button.Name}");
			try
			{
				button.PerformClick();
				RaiseMouseEvent(s_onMouseClickMethod, button, clickedButton, clicks, x, y);
				return;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[WinFormsAppHost] Button click error: {ex.GetType().Name}: {ex.Message.Split('\n')[0]} site={SiteOf(ex)}");
				BrowserErrorReporter.Report("button-click", ex);
				return;
			}
		}
		if (control is System.Windows.Forms.ListView listView)
		{
			int num3 = 24;
			int num4 = 22;
			int cap = ListViewCapacity(listView.Height);
			int top = ResolveListViewTop(listView);
			int count = listView.Items.Count;
			// Scrollbar strip: page up/down like the native control.
			if (count > cap && x - GetControlAbsX(listView, x2) >= listView.Width - ListViewScrollW)
			{
				int dir = (y - GetControlAbsY(listView, num2)) < listView.Height / 2 ? -cap : cap;
				if (ScrollListView(listView, dir))
				{
					RecordInput("click", $"{x},{y} listview={listView.Name} page={dir}");
					System.Windows.Forms.WasmRepaintGate.MarkGlobalDirty();
				}
				return;
			}
			int num5 = y - (GetControlAbsY(listView, num2) + num3);
			if (num5 >= 0)
			{
				int num6 = num5 / num4 + top;
				if (num6 >= top && num6 < count)
				{
					System.Windows.Forms.ListViewItem listViewItem = listView.Items[num6];
					RecordInput("click", $"{x},{y} listview={listView.Name} row={num6}");
					listViewItem.Selected = !listViewItem.Selected;
					RaiseMouseEvent(s_onMouseClickMethod, listView, clickedButton, clicks, x, y);
				}
			}
			return;
		}
		if (control is System.Windows.Forms.ToolStrip toolStrip)
		{
			int controlAbsX = GetControlAbsX(toolStrip, x2);
			int controlAbsY = GetControlAbsY(toolStrip, num2);
			System.Windows.Forms.ToolStripItem itemAt = toolStrip.GetItemAt(x - controlAbsX, y - controlAbsY);
			Console.WriteLine("[Host] OnClick ToolStrip item=" + ((itemAt == null) ? "null" : (itemAt.GetType().Name + " '" + itemAt.Name + "'")));
			if (itemAt != null)
			{
				RecordInput("click", $"{x},{y} item={DescribeItem(itemAt)}");
				ClickToolStripItem(toolStrip, itemAt, controlAbsX, controlAbsY, x - controlAbsX, y - controlAbsY);
			}
			return;
		}
		RecordInput("click", $"{x},{y} control={DescribeControl(control)}");
		try
		{
			// Desktop delivers the mouse position with Click (ButtonUp forwards
			// its MouseEventArgs); EventArgs.Empty NREs Telerik click behavior
			// (MouseButtonEventArgs..ctor reads Button/X/Y) and aborts
			// selection/caret. Same client-coord math as RaiseMouseEvent.
			int clickAbsX = GetControlAbsX(control, _activeForm.Location.X);
			int clickAbsY = GetControlAbsY(control, _activeForm.Location.Y + ChromeHeight(_activeForm));
			System.Windows.Forms.MouseEventArgs clickArgs = new System.Windows.Forms.MouseEventArgs(clickedButton, clicks, x - clickAbsX, y - clickAbsY, 0);
			s_onClickMethod?.Invoke(control, new object[1] { clickArgs });
			RaiseMouseEvent(s_onMouseClickMethod, control, clickedButton, clicks, x, y);
		}
		catch (Exception ex2)
		{
			Console.WriteLine("[WinFormsAppHost] Control click error: " + ex2.GetType().Name + ": " + ex2.Message.Split('\n')[0]);
			try { Console.WriteLine("[diagstack] " + string.Join(" <- ", new System.Diagnostics.StackTrace(RootCause(ex2), false).GetFrames().Take(12).Select(f => f.GetMethod()))); } catch { }
			BrowserErrorReporter.Report("control-click", ex2);
		}
	}

	private void ClickToolStripItem(System.Windows.Forms.ToolStrip ts, System.Windows.Forms.ToolStripItem item, int ox, int oy, int lx, int ly)
	{
		if (item.Name == "chooseToolButton")
		{
			Console.WriteLine("[Host] Calling CycleNextTool()...");
			CycleNextTool();
			Console.WriteLine("[Host] CycleNextTool() completed");
		}
		else if (item is System.Windows.Forms.ToolStripMenuItem { HasDropDownItems: not false } toolStripMenuItem)
		{
			OpenMenuDropdown(ts, toolStripMenuItem, ox, oy);
		}
		else if (item is System.Windows.Forms.ToolStripSplitButton toolStripSplitButton)
		{
			Rectangle value = new Rectangle(toolStripSplitButton.Bounds.Right - 12, toolStripSplitButton.Bounds.Y, 12, toolStripSplitButton.Bounds.Height);
			Console.WriteLine($"[Host] Split click: lx={lx} ly={ly} bounds={toolStripSplitButton.Bounds} arrow={value} hasDrop={toolStripSplitButton.HasDropDownItems}");
			if (value.Contains(lx, ly))
			{
				ShowItemDropdown(toolStripSplitButton, ox, oy);
			}
			else
			{
				PerformItemClick(toolStripSplitButton);
			}
		}
		else if (item is System.Windows.Forms.ToolStripDropDownButton item2)
		{
			ShowItemDropdown(item2, ox, oy);
		}
		else
		{
			PerformItemClick(item);
		}
	}

	private static void PerformItemClick(System.Windows.Forms.ToolStripItem item)
	{
		// Enter/exit timestamps: effect-apply (e.g. PDN Auto-Level)
		// wedges the page with zero console output; the exit line (in
		// finally) proves whether PerformClick returns or the hang is
		// inside it. Covers mouse and keyboard activation paths.
		Console.WriteLine($"[Host] ItemClick enter: '{item.Name}' at {System.DateTime.UtcNow:HH:mm:ss.fff}");
		try
		{
			item.PerformClick();
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[WinFormsAppHost] ToolStripItem click: {ex.GetType().Name}: {ex.Message.Split('\n')[0]} site={SiteOf(ex)}");
			BrowserErrorReporter.Report("toolstrip-click", ex);
		}
		finally
		{
			Console.WriteLine($"[Host] ItemClick exit: '{item.Name}' at {System.DateTime.UtcNow:HH:mm:ss.fff}");
		}
	}

	// Keyboard menu-loop emulation for host-tracked open dropdowns.
	// Returns true when the key is a menu-navigation key (handled whether
	// or not an item ends up selected: an open menu captures these keys).
	private bool DropdownKeyNav(System.Windows.Forms.Keys keyData)
	{
		System.Windows.Forms.Keys code = keyData & System.Windows.Forms.Keys.KeyCode;
		if (code != System.Windows.Forms.Keys.Down && code != System.Windows.Forms.Keys.Up
			&& code != System.Windows.Forms.Keys.Enter && code != System.Windows.Forms.Keys.Home
			&& code != System.Windows.Forms.Keys.End)
		{
			return false;
		}
		var open = GetOpenDropdowns();
		if (open.Count == 0)
		{
			return false;
		}
		System.Windows.Forms.ToolStripDropDown drop = open[open.Count - 1].Drop;
		var items = new System.Collections.Generic.List<System.Windows.Forms.ToolStripItem>();
		foreach (System.Windows.Forms.ToolStripItem candidate in drop.Items)
		{
			if (candidate.Available && candidate.Enabled && !(candidate is System.Windows.Forms.ToolStripSeparator))
			{
				items.Add(candidate);
			}
		}
		if (items.Count == 0)
		{
			return true;
		}
		int current = items.FindIndex(i => i.Selected);
		try
		{
			if (code == System.Windows.Forms.Keys.Enter)
			{
				System.Windows.Forms.ToolStripItem active = current >= 0 ? items[current] : items[0];
				Console.WriteLine($"[Host] Dropdown keyboard activate: '{active.Name}'");
				// Dismiss first, execute after (desktop + mouse-path
				// order): running the command with the menu open risks
				// vendor re-entrancy between teardown and apply.
				CloseOpenDropdowns();
				PerformItemClick(active);
			}
			else
			{
				int next = code switch
				{
					System.Windows.Forms.Keys.Home => 0,
					System.Windows.Forms.Keys.End => items.Count - 1,
					System.Windows.Forms.Keys.Up => current <= 0 ? items.Count - 1 : current - 1,
					_ => current < 0 || current >= items.Count - 1 ? 0 : current + 1,
				};
				items[next].Select();
				Console.WriteLine($"[Host] Dropdown keyboard select: '{items[next].Name}' ({next + 1}/{items.Count})");
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[WinFormsAppHost] Dropdown key error: {ex.GetType().Name}: {(ex.Message ?? "").Split('\n')[0]}");
			BrowserErrorReporter.Report("dropdown-key", ex);
		}
		return true;
	}

	private void ShowItemDropdown(System.Windows.Forms.ToolStripDropDownItem item, int ox, int oy)
	{
		try
		{
			item.ShowDropDown();
		}
		catch (Exception ex)
		{
			Console.WriteLine("[WinFormsAppHost] Dropdown open error: " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]);
			BrowserErrorReporter.Report("dropdown-open", ex);
		}
		TrackDropdown(item.DropDown, ox, oy);
		Console.WriteLine($"[Host] Dropdown opened: {item.GetType().Name} '{item.Name}' drop={item.DropDown.Bounds} visible={item.DropDown.Visible}");
	}

	private void OpenMenuDropdown(System.Windows.Forms.ToolStrip ts, System.Windows.Forms.ToolStripMenuItem mi, int ox, int oy)
	{
		try
		{
			System.Windows.Forms.MouseEventArgs e = new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, 0, 0, 0);
			s_itemMouseDownMethod?.Invoke(mi, new object[1] { e });
			s_itemMouseUpMethod?.Invoke(mi, new object[1] { e });
		}
		catch (Exception ex)
		{
			Console.WriteLine("[WinFormsAppHost] Menu press error: " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]);
			BrowserErrorReporter.Report("menu-press", ex);
		}
		TrackDropdown(mi.DropDown, ox, oy);
		Console.WriteLine($"[Host] Menu opened: {mi.Name} drop={mi.DropDown.Bounds} visible={mi.DropDown.Visible}");
	}

	private bool TryHitDropdown(int x, int y, out System.Windows.Forms.ToolStripDropDown drop, out System.Windows.Forms.ToolStripItem? item, out int dx, out int dy)
	{
		List<(System.Windows.Forms.ToolStripDropDown, int, int)> openDropdowns = GetOpenDropdowns();
		for (int num = openDropdowns.Count - 1; num >= 0; num--)
		{
			(System.Windows.Forms.ToolStripDropDown, int, int) tuple = openDropdowns[num];
			System.Windows.Forms.ToolStripDropDown item2 = tuple.Item1;
			int item3 = tuple.Item2;
			int item4 = tuple.Item3;
			int num2 = item3 + item2.Location.X;
			int num3 = item4 + item2.Location.Y;
			if (x >= num2 && x < num2 + item2.Width && y >= num3 && y < num3 + item2.Height)
			{
				drop = item2;
				item = item2.GetItemAt(new Point(x - num2, y - num3));
				dx = num2;
				dy = num3;
				return true;
			}
		}
		drop = null;
		item = null;
		dx = (dy = 0);
		return false;
	}

	private bool ClickOpenDropdown(int x, int y)
	{
		if (!TryHitDropdown(x, y, out System.Windows.Forms.ToolStripDropDown drop, out System.Windows.Forms.ToolStripItem item, out int dx, out int dy))
		{
			return false;
		}
		Console.WriteLine("[Host] OnClick dropdown item=" + ((item == null) ? "null" : (item.GetType().Name + " '" + item.Name + "'")));
		if (item != null)
		{
			RecordInput("click", $"{x},{y} dropitem={DescribeItem(item)}");
			int count = GetOpenDropdowns().Count;
			ClickToolStripItem(drop, item, dx, dy, x - dx, y - dy);
			if (GetOpenDropdowns().Count <= count)
			{
				CloseOpenDropdowns();
			}
		}
		return true;
	}

	private void CloseOpenDropdowns()
	{
		// Snapshot both collections: unselecting/hiding re-enters tracking
		// and item mutation (PDN menu teardown mutates live collections),
		// which threw "Collection was modified" out of Escape and killed
		// the Blazor session (paint Adjustments host-fatal, stack-proven).
		foreach (var openDropdown in GetOpenDropdowns().ToList())
		{
			System.Windows.Forms.ToolStripDropDown item = openDropdown.Drop;
			try
			{
				foreach (System.Windows.Forms.ToolStripItem item2 in item.Items.OfType<System.Windows.Forms.ToolStripItem>().ToList())
				{
					try
					{
						if (item2.Selected)
						{
							s_itemUnselectMethod?.Invoke(item2, Array.Empty<object>());
						}
					}
					catch
					{
					}
				}
				item.Visible = false;
			}
			catch
			{
			}
		}
		_openDropdowns.Clear();
		SetHoverItem(null);
	}

	public void OnKeyDown(string key, bool shift = false, bool ctrl = false, bool alt = false)
	{
		WasmDialogHost.AdvanceTurn();
		System.Windows.Forms.WasmRepaintGate.MarkGlobalDirty();
		var (keyData, keyChar) = MapKey(key, shift, ctrl, alt);
		System.Windows.Forms.Control? target = KeyTarget();
		RecordInput("key", key + Mods(shift, ctrl, alt) + " target=" + DescribeControl(target));
		Console.WriteLine("[Host] OnKeyDown(key=" + key + Mods(shift, ctrl, alt) + " target=" + DescribeControl(target) + ")");
		if (target == null)
		{
			return;
		}
		var args = new System.Windows.Forms.KeyEventArgs(keyData);
		System.Windows.Forms.WasmMouseState.SetModifiers(ToKeys(shift, ctrl, alt));
		// Open-menu navigation runs BEFORE message filters (desktop modal
		// menu loop preempts them): with a dropdown open, arrows/Enter
		// belong to the menu even if a filter (including a vendor menu
		// filter left over from an earlier menu) claims the keystroke.
		if (GetOpenDropdowns().Count > 0 && DropdownKeyNav(keyData))
		{
			// An open menu captures navigation keys (desktop menu loop):
			// arrows move selection, Enter activates. The fork runs no
			// modal menu loop headless, so the host drives vendor
			// selection (painted highlight reads item.Selected) and
			// PerformClick directly. Left/Right menu-bar travel is open.
			return;
		}
		bool filtered = PumpKeyFilters(target, alt ? WM_SYSKEYDOWN : WM_KEYDOWN, VkForPump(keyData, shift, ctrl, alt), alt ? 0x20000000 : 1);
		if (filtered)
		{
			// A message filter (menu/shortcut manager) consumed the keystroke;
			// desktop never delivers it to the control.
			args.Handled = true;
			args.SuppressKeyPress = true;
		}
		if (!filtered && (ctrl || alt) && PumpCommandKey(target, keyData, alt, shift, ctrl))
		{
			// A command key (menu shortcut) consumed the chord; desktop
			// never delivers KeyDown/KeyPress for it.
			return;
		}
		if (!filtered && (keyData & ~System.Windows.Forms.Keys.Shift) == (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.A)
			&& target is System.Windows.Forms.TextBoxBase textBox)
		{
			// The fork has no native edit control to select all (the real
			// TextBox relies on its native window for this); the host owns
			// textbox edit emulation (Backspace/Return/append below), so it
			// selects before vendor handlers run (desktop order: the native
			// control updates first, handlers observe the selection).
			// KeyPress is still suppressed below like desktop.
			textBox.SelectAll();
			args.SuppressKeyPress = true;
		}
		System.Windows.Forms.Keys keyCodeOnly = keyData & System.Windows.Forms.Keys.KeyCode;
		bool isDialogKey = !filtered && (keyCodeOnly == System.Windows.Forms.Keys.Escape || keyCodeOnly == System.Windows.Forms.Keys.Return);
		if (isDialogKey && TopModalWindow(out System.Windows.Forms.Form? dkModal) && dkModal != null)
		{
			// Focus-first like a real activation: the Accept/Cancel button
			// takes focus before ProcessDialogKey clicks it, so edits
			// commit (Leave) before the result is observed (Levels
			// output-low: Enter-OK applied the pre-edit value).
			System.Windows.Forms.Control? dkBtn =
				(keyCodeOnly == System.Windows.Forms.Keys.Escape ? dkModal.CancelButton : dkModal.AcceptButton)
				as System.Windows.Forms.Control;
			if (dkBtn != null)
				NoteFocus(dkBtn);
		}
		if (isDialogKey
			&& EmulateProcessDialogKey(target, keyData))
		{
			// Desktop PreProcessMessage order: dialog keys (Accept/Cancel
			// buttons) preempt control KeyDown delivery. Without this, a
			// focused slider swallows Escape and modal effect dialogs
			// never dismiss (Paint.NET Highlights/Shadows repro). The
			// existing post-delivery fallbacks below stay untouched for
			// dialogs without Accept/Cancel buttons.
			// Headless has no nested modal loop to observe a freshly set
			// DialogResult, so settle it like ModalOnClick does.
			if (TopModalWindow(out System.Windows.Forms.Form? escModal) && escModal != null)
				CloseModalIfSettled(escModal);
			args.Handled = true;
			args.SuppressKeyPress = true;
			return;
		}
		RaiseKey(s_onKeyDownMethod, target, args, "down");
		if (args.Handled || args.SuppressKeyPress)
		{
			return;
		}
		switch (keyData & System.Windows.Forms.Keys.KeyCode)
		{
		case System.Windows.Forms.Keys.Escape:
			if (GetOpenDropdowns().Count > 0)
			{
				CloseOpenDropdowns();
				return;
			}
			if (TopModalWindow(out System.Windows.Forms.Form modal2) && modal2 != null)
			{
				try
				{
					// Same user-close contract as the X box above: carry
					// Cancel so the fork does not read this as PDN's posted
					// abort-Close (None-at-entry stays open by design).
					if (modal2.Modal && modal2.DialogResult == System.Windows.Forms.DialogResult.None)
						modal2.DialogResult = System.Windows.Forms.DialogResult.Cancel;
					modal2.Close();
				}
				catch (Exception ex)
				{
					BrowserErrorReporter.Report("dialog-close", ex);
				}
			}
			return;
		case System.Windows.Forms.Keys.Return:
			if (_focusedTextBox is { Multiline: true, AcceptsReturn: true })
			{
				_focusedTextBox.Text += "\r\n";
				return;
			}
			if (TopModalWindow(out System.Windows.Forms.Form modal) && modal != null)
			{
				if (modal.AcceptButton is System.Windows.Forms.Button button)
				{
					button.PerformClick();
					CloseModalIfSettled(modal);
				}
			}
			else if (_activeForm?.AcceptButton is System.Windows.Forms.Button button2)
			{
				button2.PerformClick();
			}
			return;
		case System.Windows.Forms.Keys.Back:
			if (_focusedTextBox != null && _focusedTextBox.Text.Length > 0)
			{
				System.Windows.Forms.TextBox? focusedTextBox = _focusedTextBox;
				string text = _focusedTextBox.Text;
				int bsStart = System.Math.Max(0, System.Math.Min(focusedTextBox.SelectionStart, text.Length));
				int bsLen = System.Math.Max(0, System.Math.Min(focusedTextBox.SelectionLength, text.Length - bsStart));
				if (bsLen > 0)
				{
					focusedTextBox.Text = text.Substring(0, bsStart) + text.Substring(bsStart + bsLen);
					focusedTextBox.SelectionStart = bsStart;
				}
				else if (bsStart > 0)
				{
					focusedTextBox.Text = text.Substring(0, bsStart - 1) + text.Substring(bsStart);
					focusedTextBox.SelectionStart = bsStart - 1;
				}
				else
				{
					focusedTextBox.Text = text.Substring(0, text.Length - 1);
				}
			}
			return;
		}
		if (alt && keyChar is null && key.Length == 1)
		{
			// Desktop still posts WM_SYSCHAR for Alt+letter (menu mnemonics
			// consume it there); no KeyPress event follows, but filters run.
			bool sysFiltered = PumpKeyFilters(target, WM_SYSCHAR, key[0], 0x20000000);
			if (sysFiltered)
			{
				return;
			}
			if (PumpMenuMnemonic(key[0]))
			{
				return;
			}
		}		if (keyChar is char c)
		{
			if (PumpKeyFilters(target, alt ? WM_SYSCHAR : WM_CHAR, c, alt ? 0x20000000 : 1))
			{
				// Consumed (e.g., menu mnemonic); desktop never delivers it.
				return;
			}
			RaiseKey(s_onKeyPressMethod, target, new System.Windows.Forms.KeyPressEventArgs(c), "press");
			// The fork has no native edit control to consume the char;
			// the host applies the edit (desktop KeyPress default).
			if (target == _focusedTextBox && _focusedTextBox != null)
			{
				if (c >= ' ')
				{
					string text = _focusedTextBox.Text;
					int start = System.Math.Max(0, System.Math.Min(_focusedTextBox.SelectionStart, text.Length));
					int len = System.Math.Max(0, System.Math.Min(_focusedTextBox.SelectionLength, text.Length - start));
					_focusedTextBox.Text = text.Substring(0, start) + c + text.Substring(start + len);
					_focusedTextBox.SelectionStart = start + 1;
				}
			}
		}
	}
	public void OnKeyUp(string key, bool shift = false, bool ctrl = false, bool alt = false)
	{
		WasmDialogHost.AdvanceTurn();
		System.Windows.Forms.WasmRepaintGate.MarkGlobalDirty();
		var (keyData, _) = MapKey(key, shift, ctrl, alt);
		System.Windows.Forms.Control? target = KeyTarget();
		RecordInput("keyup", key + Mods(shift, ctrl, alt) + " target=" + DescribeControl(target));
		if (target == null)
		{
			return;
		}
		System.Windows.Forms.WasmMouseState.SetModifiers(ToKeys(shift, ctrl, alt));
		if (PumpKeyFilters(target, alt ? WM_SYSKEYUP : WM_KEYUP, VkForPump(keyData, shift, ctrl, alt), alt ? 0x20000000 : 0))
		{
			return;
		}		RaiseKey(s_onKeyUpMethod, target, new System.Windows.Forms.KeyEventArgs(keyData), "up");
	}

	// Desktop PreProcessMessage emulation for dialog keys: walk the
	// focused control's ancestor chain calling ProcessDialogKey (Form
	// override clicks Accept/CancelButton). Returns true when consumed.
	// A throwing vendor override must not kill input: contain per level.
	private static bool EmulateProcessDialogKey(System.Windows.Forms.Control? target, System.Windows.Forms.Keys keyData)
	{
		try
		{
			if (s_processDialogKeyMethod is null) return false;
			for (System.Windows.Forms.Control? c = target; c != null; c = c.Parent)
			{
				try
				{
					if (s_processDialogKeyMethod.Invoke(c, new object[] { keyData }) is true)
						return true;
				}
				catch { }
			}
		}
		catch { }
		return false;
	}

	private System.Windows.Forms.Control? KeyTarget()
	{
		if (TopModalWindow(out System.Windows.Forms.Form modal) && modal != null)
		{
			if (_focusedControl != null && !_focusedControl.IsDisposed && IsInForm(_focusedControl, modal))
			{
				return _focusedControl;
			}
			return modal.ActiveControl ?? modal;
		}
		if (_focusedControl != null && !_focusedControl.IsDisposed)
		{
			return _focusedControl;
		}
		if (_activeForm != null)
		{
			return _activeForm.ActiveControl ?? _activeForm;
		}
		return null;
	}
	private static bool IsInForm(System.Windows.Forms.Control control, System.Windows.Forms.Form form)
	{
		for (System.Windows.Forms.Control? current = control; current != null; current = current.Parent)
		{
			if (current == form)
			{
				return true;
			}
		}
		return false;
	}
	private const int WM_KEYDOWN = 0x0100;
	private const int WM_KEYUP = 0x0101;
	private const int WM_CHAR = 0x0102;
	private const int WM_SYSKEYDOWN = 0x0104;
	private const int WM_SYSKEYUP = 0x0105;
	private const int WM_SYSCHAR = 0x0106;
	// Virtual-key code for the pump: bare-modifier presses carry no KeyCode,
	// desktop still reports their VK (menu filters match WParam == 18 for Alt).
	private static int VkForPump(System.Windows.Forms.Keys keyData, bool shift, bool ctrl, bool alt)
	{
		int vk = (int)(keyData & System.Windows.Forms.Keys.KeyCode);
		if (vk == 0)
			 vk = alt ? 18 : ctrl ? 17 : shift ? 16 : 0;
		return vk;
	}
	// OS menu-activation service: the browser has no native menus, so no
	// DefWindowProc turns Alt+letter SYSCHAR into a menu activation. Vendor
	// menu bars (Syncfusion bars, ...) expose a public ProcessShortcut(char)
	// entry point for exactly this; the host drives the first visible one
	// that accepts the key. Duck-typed (no vendor references) and cached per
	// runtime type; one control's failure never blocks the others or typing.
	private static readonly System.Collections.Generic.Dictionary<string, System.Reflection.MethodInfo?> s_mnemonicMethods = new();
	private static readonly System.Collections.Generic.Dictionary<string, System.Reflection.MethodInfo?> s_commandKeyMethods = new();
	// Desktop PreProcessMessage order for command keys: the focused
	// control first, then each parent up to the form (menu shortcuts live
	// on the form/menu strip). ProcessCmdKey is protected, so reflection
	// per level; each level best-effort, never throws.
	private bool PumpCommandKey(System.Windows.Forms.Control target, System.Windows.Forms.Keys keyData, bool alt, bool shift, bool ctrl)
	{
		try
		{
			System.IntPtr hwnd = System.IntPtr.Zero;
			try { hwnd = target.Handle; } catch { }
			int vk = VkForPump(keyData, shift, ctrl, alt);
			System.Windows.Forms.Message msg = System.Windows.Forms.Message.Create(hwnd,
				alt ? WM_SYSKEYDOWN : WM_KEYDOWN, (System.IntPtr)vk, (System.IntPtr)(alt ? 0x20000000 : 1));
			for (System.Windows.Forms.Control? c = target; c != null; c = c.Parent)
			{
				System.Reflection.MethodInfo? mi;
				string typeKey = c.GetType().AssemblyQualifiedName ?? c.GetType().FullName ?? "?";
				lock (s_commandKeyMethods)
				{
					if (!s_commandKeyMethods.TryGetValue(typeKey, out mi))
					{
						try
						{
							var msgByRef = typeof(System.Windows.Forms.Message).MakeByRefType();
							mi = c.GetType().GetMethod("ProcessCmdKey",
								System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
								null, new[] { msgByRef, typeof(System.Windows.Forms.Keys) }, null);
							if (mi != null && mi.ReturnType != typeof(bool)) mi = null;
						}
						catch
						{
							mi = null;
						}
						s_commandKeyMethods[typeKey] = mi;
					}
				}
				if (mi == null) continue;
				try
				{
					object[] pm = new object[] { msg, keyData };
					bool consumed = (bool)(mi.Invoke(c, pm) ?? false);
					msg = (System.Windows.Forms.Message)pm[0];
					if (consumed)
					{
						return true;
					}
				}
				catch
				{
					// A level that cannot run headless simply does not
					// consume; the walk continues to the next parent.
				}
			}
		}
		catch
		{
		}
		return false;
	}
	private bool PumpMenuMnemonic(char c)
	{
		try
		{
			System.Windows.Forms.Control? root = _activeForm;
			if (root == null)
			{
				return false;
			}
			var stack = new System.Collections.Generic.Stack<System.Windows.Forms.Control>();
			stack.Push(root);
			var barControls = new System.Collections.Generic.List<System.Windows.Forms.Control>();
			while (stack.Count > 0)
			{
				System.Windows.Forms.Control ctl = stack.Pop();
				System.Reflection.MethodInfo? mi;
				string typeKey = ctl.GetType().AssemblyQualifiedName ?? ctl.GetType().FullName ?? "?";
				lock (s_mnemonicMethods)
				{
					if (!s_mnemonicMethods.TryGetValue(typeKey, out mi))
					{
						try
						{
							var cand = ctl.GetType().GetMethod("ProcessShortcut",
								System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
								null, new[] { typeof(char) }, null);
							mi = (cand != null && cand.ReturnType == typeof(bool)) ? cand : null;
						}
						catch
						{
							mi = null;
						}
						s_mnemonicMethods[typeKey] = mi;
					}
				}
				if (mi != null && ctl.Visible && ctl.Enabled)
				{
					barControls.Add(ctl);
				}
				try
				{
					foreach (System.Windows.Forms.Control kid in ctl.Controls)
					{
						stack.Push(kid);
					}
				}
				catch
				{
				}
			}
			foreach (System.Windows.Forms.Control ctl in barControls)
			{
				System.Reflection.MethodInfo mi2 = null;
				string typeKey2 = ctl.GetType().AssemblyQualifiedName ?? ctl.GetType().FullName ?? "?";
				lock (s_mnemonicMethods) { s_mnemonicMethods.TryGetValue(typeKey2, out mi2); }
				if (mi2 != null)
				{
					try
					{
						bool opened = (bool)(mi2.Invoke(ctl, new object[] { c }) ?? false);
						if (opened)
						{
							return true;
						}
					}
					catch
					{
						// A bar that cannot run headless simply does not open.
					}
				}
			}
		}
		catch
		{
		}
		return false;
	}
	// Desktop pumps app message filters before dispatching key messages;
	// vendors' menu/shortcut managers (Syncfusion MainFrameBarManager,
	// TabbedMDIManager, ribbon/docking filters) live there. The host owns
	// no message loop, so it synthesizes the message explicitly. HWnd is
	// the target's (minted) handle; LParam carries only the Alt context
	// bit (scan codes are unknowable headless). Swallowed (true) means
	// desktop never delivers the keystroke. Never throws: a faulty
	// filter must not break typing.
	private bool PumpKeyFilters(System.Windows.Forms.Control target, int msg, int wParam, int lParam)
	{
		try
		{
			System.IntPtr hwnd = System.IntPtr.Zero;
			try { hwnd = target.Handle; } catch { }
			var message = System.Windows.Forms.Message.Create(hwnd, msg, (System.IntPtr)wParam, (System.IntPtr)lParam);
			return System.Windows.Forms.Application.FilterMessage(ref message);
		}
		catch (Exception ex)
		{
			Console.WriteLine("[WinFormsAppHost] Key filter error: " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0] + " site=" + SiteOf(ex));
			return false;
		}
	}	private void RaiseKey(MethodInfo? method, System.Windows.Forms.Control target, EventArgs args, string kind)
	{
		if (method == null || target.IsDisposed)
		{
			return;
		}
		try
		{
			method.Invoke(target, new object[1] { args });
		}
		catch (Exception ex)
		{
			Console.WriteLine("[WinFormsAppHost] Key " + kind + " error: " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0] + " site=" + SiteOf(ex));
			Console.WriteLine("[WinFormsAppHost] Key " + kind + " error: root=" + RootCause(ex).GetType().Name + " site=" + SiteOf(RootCause(ex)) + " msg=" + (RootCause(ex).Message ?? "").Split('\n')[0]);
			try { Console.WriteLine("[diagstack] " + string.Join(" <- ", new System.Diagnostics.StackTrace(RootCause(ex), false).GetFrames().Take(12).Select(f => f.GetMethod()))); } catch { }
			BrowserErrorReporter.Report("key", ex, SiteOf(RootCause(ex)));
		}
	}
	private static string Mods(bool shift, bool ctrl, bool alt) =>
		(shift ? "+shift" : "") + (ctrl ? "+ctrl" : "") + (alt ? "+alt" : "");
	private static (System.Windows.Forms.Keys KeyData, char? KeyChar) MapKey(string key, bool shift, bool ctrl, bool alt)
	{
		System.Windows.Forms.Keys code;
		char? ch = null;
		switch (key)
		{
		case "Enter": code = System.Windows.Forms.Keys.Return; ch = '\r'; break;
		case "Escape": code = System.Windows.Forms.Keys.Escape; break;
		case "Backspace": code = System.Windows.Forms.Keys.Back; break;
		case "Tab": code = System.Windows.Forms.Keys.Tab; break;
		case "Delete": code = System.Windows.Forms.Keys.Delete; break;
		case "Insert": code = System.Windows.Forms.Keys.Insert; break;
		case "Home": code = System.Windows.Forms.Keys.Home; break;
		case "End": code = System.Windows.Forms.Keys.End; break;
		case "PageUp": code = System.Windows.Forms.Keys.Prior; break;
		case "PageDown": code = System.Windows.Forms.Keys.Next; break;
		case "ArrowLeft": code = System.Windows.Forms.Keys.Left; break;
		case "ArrowUp": code = System.Windows.Forms.Keys.Up; break;
		case "ArrowRight": code = System.Windows.Forms.Keys.Right; break;
		case "ArrowDown": code = System.Windows.Forms.Keys.Down; break;
		case " ": code = System.Windows.Forms.Keys.Space; ch = ' '; break;
		case "CapsLock": code = System.Windows.Forms.Keys.Capital; break;
		case "Shift": code = System.Windows.Forms.Keys.ShiftKey; break;
		case "Control": code = System.Windows.Forms.Keys.ControlKey; break;
		case "Alt": code = System.Windows.Forms.Keys.Menu; break;
		case "Meta": code = System.Windows.Forms.Keys.LWin; break;
		default:
			if (key.Length > 1 && key[0] == 'F' && int.TryParse(key.AsSpan(1), out int fn) && fn >= 1 && fn <= 24)
			{
				code = System.Windows.Forms.Keys.F1 + (fn - 1);
			}
			else if (key.Length == 1)
			{
				ch = key[0];
				code = (System.Windows.Forms.Keys)char.ToUpperInvariant(key[0]);
			}
			else
			{
				code = System.Windows.Forms.Keys.None;
			}
			break;
		}
		System.Windows.Forms.Keys data = code;
		if (shift)
		{
			data |= System.Windows.Forms.Keys.Shift;
		}
		if (ctrl)
		{
			data |= System.Windows.Forms.Keys.Control;
		}
		if (alt)
		{
			data |= System.Windows.Forms.Keys.Alt;
		}
		if ((ctrl || alt) && ch.HasValue)
		{
			// Desktop parity: Ctrl/Alt+key never yields a text char. A
			// Ctrl+letter KeyPress carries its control code (ignored by edit
			// controls); anything else produces no KeyPress at all. Without
			// this the host appended a literal 'a' for Ctrl+A.
			if (ctrl && char.IsLetter(ch.Value))
				ch = (char)(char.ToUpperInvariant(ch.Value) - 'A' + 1);
			else
				ch = null;
		}
		return (data, ch);
	}
	private static string DescribeKids(System.Windows.Forms.Control.ControlCollection controls, int depth)
	{
		System.Text.StringBuilder sb = new System.Text.StringBuilder();
		foreach (System.Windows.Forms.Control k in controls)
		{
			if (sb.Length > 0)
				sb.Append(";");
			sb.Append(k.GetType().Name + " v=" + k.Visible + " e=" + k.Enabled + " b=" + k.Left + "," + k.Top + "," + k.Width + "x" + k.Height);
			if (depth < 2 && k.Controls.Count > 0)
				sb.Append("[" + DescribeKids(k.Controls, depth + 1) + "]");
		}
		return sb.ToString();
	}

	private System.Windows.Forms.Control? HitTestDeep(System.Windows.Forms.Control.ControlCollection controls, int parentAbsX, int parentAbsY, int x, int y)
	{
		for (int num = controls.Count - 1; num >= 0; num--)
		{
			System.Windows.Forms.Control control = controls[num];
			if (control.Visible && control.Width > 0 && control.Height > 0 && !(control.GetType().Name == "CaptionControl"))
			{
				int num2 = parentAbsX + control.Left;
				int num3 = parentAbsY + control.Top;
				if (x >= num2 && x <= num2 + control.Width && y >= num3 && y <= num3 + control.Height)
				{
					if (control.Controls.Count > 0)
					{
						System.Windows.Forms.Control control2 = HitTestDeep(control.Controls, num2, num3, x, y);
						if (control2 != null)
						{
							return control2;
						}
					}
					return control;
				}
			}
		}
		return null;
	}

	private static int GetControlAbsX(System.Windows.Forms.Control c, int formClientX)
	{
		int num = formClientX;
		System.Windows.Forms.Control control = c;
		while (control != null && !(control is System.Windows.Forms.Form))
		{
			num += control.Left;
			control = control.Parent;
		}
		return num;
	}

	private static int GetControlAbsY(System.Windows.Forms.Control c, int formClientY)
	{
		int num = formClientY;
		System.Windows.Forms.Control control = c;
		while (control != null && !(control is System.Windows.Forms.Form))
		{
			num += control.Top;
			control = control.Parent;
		}
		return num;
	}

	private static string CleanItemText(string? raw)
	{
		if (string.IsNullOrEmpty(raw))
		{
			return string.Empty;
		}
		string text = raw.Replace("&", "");
		int num = text.IndexOf('\n');
		if (num >= 0)
		{
			text = text.Substring(0, num);
		}
		return text.Trim();
	}

	private static string GetItemGlyph(string? name, int index)
	{
		if (string.IsNullOrEmpty(name))
		{
			return "\u2022";
		}
		string text = name.ToLowerInvariant();
		if (text.Contains("new"))
		{
			return "\ud83d\udcc4";
		}
		if (text.Contains("open"))
		{
			return "\ud83d\udcc1";
		}
		if (text.Contains("save"))
		{
			return "\ud83d\udcbe";
		}
		if (text.Contains("print"))
		{
			return "\ud83d\udda8";
		}
		if (text.Contains("cut"))
		{
			return "\u2702";
		}
		if (text.Contains("copy"))
		{
			return "\ud83d\udccb";
		}
		if (text.Contains("paste"))
		{
			return "\ud83d\udce5";
		}
		if (text.Contains("crop"))
		{
			return "\u2702";
		}
		if (text.Contains("deselect"))
		{
			return "\u2715";
		}
		if (text.Contains("delete"))
		{
			return "\ud83d\uddd1";
		}
		if (text.Contains("undo"))
		{
			return "\u21b6";
		}
		if (text.Contains("redo"))
		{
			return "\u21b7";
		}
		if (text.Contains("tool"))
		{
			return "\ud83d\udd8c";
		}
		if (text.Contains("color"))
		{
			return "\ud83c\udfa8";
		}
		if (text.Contains("history"))
		{
			return "\ud83d\udd52";
		}
		if (text.Contains("layer"))
		{
			return "\ud83d\uddc2";
		}
		if (text.Contains("setting"))
		{
			return "\u2699";
		}
		if (text.Contains("help"))
		{
			// Original glyph unrecoverable (mojibake, see GlyphLiteralTests): "?" renders everywhere.
			return "?";
		}
		return "\u25aa";
	}

	private static string GetFontCss(Font? font)
	{
		if (font == null)
		{
			return "12px 'Segoe UI', sans-serif";
		}
		string value = (font.Bold ? "bold " : "");
		string value2 = (font.Italic ? "italic " : "");
		int value3 = Math.Max(9, (int)Math.Round(font.SizeInPoints * 96.0 / 72.0));
		return $"{value2}{value}{value3}px '{font.FontFamily.Name}', sans-serif";
	}

	// Region repaint: the drain gave a sub-rect for a control whose
	// retained surface matches the current size. The clipped OnPaint
	// runs over the retained pixels (every primitive clips except
	// Clear, which the PaintVersion stamp catches), the rect uploads
	// as a patch, and the layer re-blits. PaintEventArgs carries the
	// dirty rect as ClipRectangle, as desktop does. Any precondition
	// miss (whole-control mark, no retained surface, resized, a Clear
	// during paint, a near-full rect) returns false and the caller
	// runs the full path.

	private static bool TryPaintRegion(ICanvas canvas, System.Windows.Forms.Control c, int absX, int absY, int w, int h, bool clearFlag)
	{
		if (!FrameDirtyRect(c, out System.Drawing.Rectangle? dirty) || dirty is null)
			return false;
		string id = LayerIdFor(c);
		Bitmap? retained;
		lock (s_retainedPaint)
		{
			s_retainedPaint.TryGetValue(id, out retained);
		}
		if (retained is null || retained.Width != w || retained.Height != h)
		{
			return false;
		}
		System.Drawing.Rectangle rc = System.Drawing.Rectangle.Intersect(dirty.Value, new System.Drawing.Rectangle(0, 0, w, h));
		if (rc.Width <= 0 || rc.Height <= 0)
			return BlitKnownLayer(canvas, id, absX, absY, w, h);
		if ((long)rc.Width * rc.Height * 10 >= (long)w * h * 9)
		{
			return false;
		}
		if (!(canvas is ICanvasRenderer { SupportsBitmapLayers: not false }))
		{
			// Plain canvas: the clipped raster still saves the OnPaint,
			// then the full surface uploads (no retained layer to patch).
			if (!PaintRegionPixels(c, retained, rc, clearFlag))
				return false;
			byte[]? full = ExtractBitmapRgba(retained);
			if (full is null)
				return false;
			canvas.DrawImage(full, w, h, absX, absY, w, h);
		if (System.Windows.Forms.WasmFrameCounters.PerfEnabled)
			System.Windows.Forms.WasmFrameCounters.CountRasterFull((long)w * h);
			return true;
		}
		bool tmpOk = PaintRegionPixels(c, retained, rc, clearFlag);
		if (!tmpOk)
		{
			return false;
		}
		// No identical-bytes skip: a pre-paint snapshot costs a full rect
		// swizzle on every frame, while identical region repaints are small
		// (hover backstops) and cheap to upload. Full paints keep hash-skip.
		byte[] after = retained.ToRgbaBytes(rc);
		if (System.Windows.Forms.WasmFrameCounters.PerfEnabled && after != null)
			System.Windows.Forms.WasmFrameCounters.AddBytesExtracted(after.Length);
		RecordBitmapPatch(canvas, id, after, rc, absX, absY, w, h);
		if (System.Windows.Forms.WasmFrameCounters.PerfEnabled)
			System.Windows.Forms.WasmFrameCounters.CountRasterRegion((long)rc.Width * rc.Height);
		return true;
	}

	private static bool PaintRegionPixels(System.Windows.Forms.Control c, Bitmap retained, System.Drawing.Rectangle rc, bool clearFlag)
	{
		if (s_onPaintMethod is null || s_onPaintBackgroundMethod is null)
			return false;
		try
		{
			int stamp = retained.ClearVersion;
			using (Graphics graphics = Graphics.FromImage(retained))
			{
				if (clearFlag)
				{
					// Clear-equivalent restricted to the rect: the flag dance
					// replaced BackColor with an opaque color, so a solid fill
					// writes identical bytes to Clear on the rect.
					using SolidBrush brush = new SolidBrush(c.BackColor);
					graphics.FillRectangle(brush, rc);
				}
				graphics.SetClip(rc);
				using System.Windows.Forms.PaintEventArgs e = new System.Windows.Forms.PaintEventArgs(graphics, rc);
				LogFirstPaint(c);
				s_onPaintBackgroundMethod.Invoke(c, new object[1] { e });
				s_onPaintMethod.Invoke(c, new object[1] { e });
			}
			// Clear ignores the clip (desktop parity): a Clear during the
			// paint may have touched pixels outside the rect, so fall back
			// to a full upload rather than splice a stale surround.
			return retained.ClearVersion == stamp;
		}
		catch
		{
			return false;
		}
	}

	private static bool BlitKnownLayer(ICanvas canvas, string id, int absX, int absY, int w, int h)
	{
		if (!(canvas is ICanvasRenderer { SupportsBitmapLayers: not false } rr))
			return false;
		lock (s_liveLayers)
		{
			s_liveLayers.Add(id);
		}
		rr.BlitBitmap(id, absX, absY, w, h);
		return true;
	}

	private static void RecordBitmapPatch(ICanvas canvas, string id, byte[] rgba, System.Drawing.Rectangle rc, int absX, int absY, int w, int h)
	{
		var rr = (ICanvasRenderer)canvas;
		lock (s_liveLayers)
		{
			s_liveLayers.Add(id);
		}
		// The full-content hash stays stale by design: the next full
		// raster re-hashes and re-Defs (a mismatch only costs one
		// upload, it can never serve stale pixels).
		lock (s_layerHash)
		{
			s_layerSize[id] = (w, h);
		}
		rr.PatchBitmap(id, rc.X, rc.Y, rc.Width, rc.Height, rgba);
		rr.BlitBitmap(id, absX, absY, w, h);
	}

	private static void StoreRetainedPaint(object layerKey, Bitmap bitmap)
	{
		string id = LayerIdFor(layerKey);
		Bitmap? old;
		lock (s_retainedPaint)
		{
			s_retainedPaint.TryGetValue(id, out old);
			s_retainedPaint[id] = (Bitmap)bitmap.Clone();
			if (System.Windows.Forms.WasmFrameCounters.PerfEnabled)
				System.Windows.Forms.WasmFrameCounters.AddBytesCloned((long)bitmap.Width * bitmap.Height * 4);
		}
		old?.Dispose();
	}

	// Paint-completion observer (tile-text diagnosis): subscribes the
	// ElementPainted event (fired at the end of RadElement.Paint) on
	// tile-subtree elements once groups arrive, then reports per-type
	// fire counts on subsequent panorama paints. Tells exactly which
	// levels the walk reaches. Bounded (500 subscriptions/session).
	private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> s_paintFire = new();
	private static int s_paintSubs;
	private static void OnTilePainted(object? sender, System.Windows.Forms.PaintEventArgs e)
	{
		// Key per paint site: type + clip rect + text prefix. The clip comes
		// from the fired args (per-element clip), telling whether the walk
		// narrows the clip before reaching tile text.
		try
		{
			string clip = "?";
			try { clip = e.ClipRectangle.ToString(); } catch { }
			string tp = "?";
			try
			{
				object? t = sender?.GetType().GetProperty("Text")?.GetValue(sender);
				string? ts = t?.ToString();
				if (!string.IsNullOrEmpty(ts)) tp = ts.Length <= 18 ? ts : ts.Substring(0, 18);
			}
			catch { }
			string key = (sender?.GetType().Name ?? "?") + "|" + clip + "|" + tp;
			// For tile parents also record the paint-time z-order yield
			// (same iterator PaintChildren uses). A dump-time/pain-time
			// divergence here (3 vs fewer) locates the walk skip exactly.
			try
			{
				string? tn = sender?.GetType().Name;
				if (tn == "DockLayoutPanel" || tn == "VerticalTileElement")
					key += "|zc=" + SafeZOrderedCount(sender);
			}
			catch { }
			s_paintFire.AddOrUpdate(key, 1, (_, v) => v + 1);
		}
		catch { }
	}
	internal static System.Collections.Generic.Dictionary<string, int> TakePaintFire()
	{
		return new System.Collections.Generic.Dictionary<string, int>(s_paintFire);
	}
	internal static int SubscribeTilePaintFire(object? root)
	{
		int before = s_paintSubs;
		try
		{
			System.Reflection.EventInfo? evt = root?.GetType().GetEvent("ElementPainted");
			if (evt is not null && s_paintSubs < 500)
			{
				try
				{
					// Direct method-group reference (not GetMethod): the trimmer
					// removes private methods reachable only via reflection,
					// which silently zeroed subscriptions in WASM builds.
					System.Windows.Forms.PaintEventHandler direct = OnTilePainted;
					if (evt.EventHandlerType == typeof(System.Windows.Forms.PaintEventHandler))
						evt.AddEventHandler(root, direct);
					else
					{
						var m = typeof(WinFormsAppHost).GetMethod("OnTilePainted", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
						evt.AddEventHandler(root, System.Delegate.CreateDelegate(evt.EventHandlerType!, m));
					}
					s_paintSubs++;
				}
				catch { }
			}
			if (root is System.Collections.IEnumerable en && root is not string)
			{
				foreach (object? child in en)
				{
					if (child is null) continue;
					if (s_paintSubs >= 500) break;
					SubscribeTilePaintFire(child);
				}
				return s_paintSubs - before;
			}
			object?[] kids = new object?[]
			{
				SafeGetObj(root, "Children"),
				SafeGetObj(root, "Groups"),
				SafeGetObj(root, "Items"),
				SafeGetObj(root, "PanoramaElement"),
			};
			foreach (object? k in kids)
			{
				if (k is null) continue;
				if (s_paintSubs >= 500) break;
				SubscribeTilePaintFire(k);
			}
			if (root is System.Windows.Forms.Control c)
				foreach (System.Windows.Forms.Control ch in c.Controls)
				{
					if (s_paintSubs >= 500) break;
					SubscribeTilePaintFire(ch);
				}
		}
		catch { }
		if (before == 0)
			try { Console.WriteLine($"[TILE-VIS] paint-subs {s_paintSubs}"); } catch { }
		return s_paintSubs - before;
	}

	private static int s_panoPaintTrace;
	// One-shot panorama repaint trace (tile-text diagnosis): after the
	// late tile dump, log how the next few panorama paints are served
	// (clean blit vs region patch vs full) and with what dirty state.
	// Bounded (6 lines/session); GRID-PAINTED precedent for paint logs.
	private static void TracePanoPaint(System.Windows.Forms.Control c)
	{
		try
		{
			if (!s_tileLateDumpDone || s_panoPaintTrace >= 6) return;
			if (!(c.GetType().FullName?.Contains("Panorama", System.StringComparison.Ordinal) == true)) return;
			s_panoPaintTrace++;
			bool fd = IsFrameDirty(c);
			FrameDirtyRect(c, out System.Drawing.Rectangle? drc);
			System.Text.StringBuilder fb = new();
			try
			{
				foreach (var kv in TakePaintFire())
				{
					if (fb.Length > 0) fb.Append(',');
					fb.Append(kv.Key).Append('=').Append(kv.Value);
					if (fb.Length > 2000) break;
				}
			}
			catch { }
			Console.WriteLine($"[TILE-VIS] pano-paint dirty={fd} rect={(drc?.ToString() ?? "whole")} fires=[{fb}]");
			try { Console.WriteLine("[TILE-VIS] " + FormatPanoScrollState(c)); } catch { }
			try { Console.WriteLine("[TILE-VIS] " + FormatLayoutOffset(c)); } catch { }
		}
		catch { }
	}

	// Scroll/maximize diagnosis (BookShelf tile text below the fold):
	// one reflection-only line with the host form geometry plus the
	// panorama scrollbar range, so a headless probe can tell "no scroll
	// range" apart from "wheel ignored" apart from "form too small".
	// Pure formatter (no console); the TracePanoPaint cap bounds it.

	// Layout-offset probe: returns the groups-layout panel's live
	// PositionOffset plus the panorama's zoomedIn flag, to tell a stuck
	// scroll/animation offset apart from an arrange-placed position.
	// Reflection-only, never throws.
	internal static string FormatLayoutOffset(object? panoramaControl)
	{
		try
		{
			object? el = SafeGetObj(panoramaControl, "PanoramaElement");
			if (el is null) return "layoff=n/a";
			string zoom = "zoom=?";
			try
			{
				System.Reflection.FieldInfo? zf = null;
				for (System.Type? t = el.GetType(); t is not null && zf is null; t = t.BaseType)
					zf = t.GetField("zoomedIn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
				if (zf is not null) zoom = "zoom=" + zf.GetValue(el);
			}
			catch { }
			if (SafeGetObj(el, "Children") is System.Collections.IEnumerable kids)
			{
				foreach (object? k in kids)
				{
					if (k is null) continue;
					string tn = k.GetType().Name;
					if (!tn.Contains("LayoutPanel", System.StringComparison.Ordinal)
						&& !tn.Contains("Layout", System.StringComparison.Ordinal)) continue;
					string pos = SafeProp(k, "PositionOffset");
					string bnd = SafeProp(k, "Bounds");
					string align = SafeProp(el, "ScrollBarAlignment");
					string thick = SafeProp(el, "ScrollBarThickness");
					string pad = SafeProp(el, "Padding");
					return $"layoff panel={tn} pos={pos} bounds={bnd} {zoom} align={align} thick={thick} pad={pad}";
				}
			}
			return "layoff=nopanel " + zoom;
		}
		catch { return "layoff=error"; }
	}

	internal static string FormatPanoScrollState(object? panoramaControl)
	{
		try
		{
			if (panoramaControl is not System.Windows.Forms.Control c) return "pano-scroll n/a";
			System.Windows.Forms.Form? f = c.FindForm();
			string form = f is null ? "form=null"
				: $"form={f.GetType().Name} bounds={f.Bounds} client={f.ClientSize} ws={f.WindowState} loc={f.Location}";
			object? el = SafeGetObj(c, "PanoramaElement");
			string sb = el is null ? "sb=n/a"
				: $"sb=V{SafeSubProp(el, "ScrollBar", "Value")}/M{SafeSubProp(el, "ScrollBar", "Maximum")}/L{SafeSubProp(el, "ScrollBar", "LargeChange")}/vis{SafeSubProp(el, "ScrollBar", "Visibility")}";
			string bars = $"auto={SafeProp(c, "AutoScroll")} vvis={SafeSubProp(c, "VerticalScroll", "Visible")} vval={SafeSubProp(c, "VerticalScroll", "Value")}/{SafeSubProp(c, "VerticalScroll", "Maximum")}";
			System.Text.StringBuilder strips = new();
			try
			{
				if (f is not null) foreach (System.Windows.Forms.Control kid in f.Controls)
				{
					if (strips.Length > 0) strips.Append(' ');
					strips.Append(kid.Name).Append('=').Append(kid.Bounds).Append(kid.Visible ? "" : "?hid");
					if (strips.Length > 600) break;
				}
			}
			catch { }
			string lq = $"lq={System.Windows.Forms.WasmLayoutQueue.TotalPosted}/{System.Windows.Forms.WasmLayoutQueue.TotalDrained}/{System.Windows.Forms.WasmLayoutQueue.TotalFaulted}";
			return $"pano-scroll {form} pano={c.Name} bounds={c.Bounds} {sb} {bars} strips=[{strips}] {lq}";
		}
		catch { return "pano-scroll error"; }
	}

	private static bool TryRenderSelfPainted(ICanvas canvas, System.Windows.Forms.Control c, int absX, int absY, int w, int h)
	{
		TracePanoPaint(c);
		if (w <= 0 || h <= 0 || w > 2048 || h > 2048)
		{
			return false;
		}
		Type type = c.GetType();
		if (!s_selfPaints.GetOrAdd(type, delegate(Type type2)
		{
			MethodInfo method = type2.GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if ((object)method != null && method.DeclaringType != typeof(System.Windows.Forms.Control))
			{
				return true;
			}
			MethodInfo method2 = type2.GetMethod("OnPaintBackground", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			return (object)method2 != null && method2.DeclaringType != typeof(System.Windows.Forms.Control);
		}) && c.BackgroundImage == null)
		{
			return false;
		}
		if ((object)s_onPaintMethod == null || (object)s_onPaintBackgroundMethod == null)
		{
			return false;
		}
		if (TryBlitCleanLayer(canvas, c, absX, absY, w, h))
		{
			return true;
		}
		Color backColor = Color.Empty;
		bool flag = false;
		if (c.BackColor.A < byte.MaxValue && !HasPaintedAncestor(c))
		{
			backColor = c.BackColor;
			c.BackColor = EffectiveBackColor(c);
			flag = true;
		}
		try
		{
            // Perf item 4 S5 split (render side): time EnsureElementLayout
            // + OnPaint invokes; extract/record/store is the remainder.
            bool perfPaint = System.Windows.Forms.WasmFrameCounters.PerfEnabled;
            long tPaint0 = perfPaint ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            // Perf item 4: TPF layout-inside-paint split (see paintS).
            long tLayout0 = System.Windows.Forms.WasmFrameCounters.PerfEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            EnsureElementLayout(c);
            if (tLayout0 != 0)
                System.Windows.Forms.WasmFrameCounters.AddEnsureLayoutTicks(System.Diagnostics.Stopwatch.GetTimestamp() - tLayout0);
            if (TryPaintRegion(canvas, c, absX, absY, w, h, flag))
            {
                return true;
            }
            using Bitmap bitmap = new Bitmap(w, h);
			using (Graphics graphics = Graphics.FromImage(bitmap))
			{
				if (flag)
				{
					graphics.Clear(c.BackColor);
				}
				using System.Windows.Forms.PaintEventArgs e = new System.Windows.Forms.PaintEventArgs(graphics, new Rectangle(0, 0, w, h));
				// Perf item 4: invoke split (Telerik render vs alloc/Clear).
				long tInv0 = System.Windows.Forms.WasmFrameCounters.PerfEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
				// Per-raster draw attribution: Peek diffs around the invokes
				// (Peek never resets; Home's Take keeps frame totals).
				global::System.Drawing.TextRasterCounters.Snapshot preDraw = default;
				if (tInv0 != 0) preDraw = global::System.Drawing.TextRasterCounters.Peek();
				LogFirstPaint(c);
				s_onPaintBackgroundMethod.Invoke(c, new object[1] { e });
				if (tInv0 != 0)
				    System.Windows.Forms.WasmFrameCounters.AddBgInvokeTicks(System.Diagnostics.Stopwatch.GetTimestamp() - tInv0);
				s_onPaintMethod.Invoke(c, new object[1] { e });
				if (tInv0 != 0)
				{
								long dtInv = System.Diagnostics.Stopwatch.GetTimestamp() - tInv0;
								System.Windows.Forms.WasmFrameCounters.AddPaintInvokeTicks(dtInv);
								// Slowest-layer tracking (names the control owning the frame).
								long prevSlow;
								do
								{
												prevSlow = s_slowTicks;
												if (dtInv <= prevSlow) break;
								} while (System.Threading.Interlocked.CompareExchange(ref s_slowTicks, dtInv, prevSlow) != prevSlow);
								if (dtInv > prevSlow)
								{
												s_slowType = c.GetType().Name;
												var postDraw = global::System.Drawing.TextRasterCounters.Peek();
												s_slowDraws = postDraw.DrawCalls - preDraw.DrawCalls;
												s_slowPx = postDraw.DrawPx - preDraw.DrawPx;
								}
				}
			}
            if (perfPaint)
                System.Windows.Forms.WasmFrameCounters.AddOnPaintTicks(System.Diagnostics.Stopwatch.GetTimestamp() - tPaint0);
            byte[] array = ExtractBitmapRgba(bitmap);
			if (array == null)
			{
				return false;
			}
			RecordBitmap(c, canvas, array, w, h, absX, absY, w, h);
		if (System.Windows.Forms.WasmFrameCounters.PerfEnabled)
			System.Windows.Forms.WasmFrameCounters.CountRasterFull((long)w * h);
			StoreRetainedPaint(c, bitmap);
			return true;
		}
		catch (Exception ex)
		{
			string text = type.FullName ?? type.Name;
			if (s_selfPaintLogged.TryAdd(text, 1))
			{
				Console.WriteLine($"[WinFormsAppHost] Self-paint failed for {text}: TYPE={ex.GetType().FullName} MSG={ex.Message} site={SiteOf(ex)} INNER={ex.InnerException?.GetType().FullName}:{ex.InnerException?.Message}");
			}
			return false;
		}
		finally
		{
			if (flag)
			{
				try
				{
					c.BackColor = backColor;
				}
				catch
				{
				}
			}
		}
	}

	private static void EnsureElementLayout(System.Windows.Forms.Control c)
	{
		Type type = c.GetType();
		ElementLayoutMembers orAdd = s_layoutMembers.GetOrAdd(type, delegate(Type type2)
		{
			PropertyInfo tree = type2.GetProperty("ElementTree", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			MethodInfo pump = tree?.PropertyType.GetMethod("PerformLayout", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
			if ((object)tree != null && (object)pump != null)
			{
				return new ElementLayoutMembers((System.Windows.Forms.Control obj3) => tree.GetValue(obj3), null, delegate(object obj3)
				{
					pump.Invoke(obj3, null);
				});
			}
			PropertyInfo root = type2.GetProperty("RootElement", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			Type type3 = root?.PropertyType;
			PropertyInfo valid = type3?.GetProperty("IsArrangeValid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			PropertyInfo never = type3?.GetProperty("NeverArranged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			MethodInfo update = type3?.GetMethod("UpdateLayout", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			return ((object)root == null || (object)valid == null || (object)never == null || (object)update == null) ? null : new ElementLayoutMembers((System.Windows.Forms.Control obj3) => root.GetValue(obj3), (object obj3) => !(bool)(valid.GetValue(obj3) ?? ((object)true)) || (bool)(never.GetValue(obj3) ?? ((object)false)), delegate(object obj3)
			{
				update.Invoke(obj3, null);
			});
		});
		if ((object)orAdd == null)
		{
			return;
		}
		try
		{
			object obj = orAdd.GetTree(c);
			if (obj != null && (orAdd.NeedsLayout == null || orAdd.NeedsLayout(obj)))
			{
				orAdd.Pump(obj);
			}
		}
		catch
		{
		}
	}

	// True when a transparent-BackColor control sits over pixels a flat color cannot
	// represent (ancestor BackgroundImage or ancestor owner-paint): the isolated
	// self-paint bitmap must stay transparent so the layer blit (drawImage,
	// source-over) composites the real beneath-pixels. Clearing with
	// EffectiveBackColor here paints a wrong flat rect (e.g. black Telerik
	// labels over BookShelf wood theme).
	internal static bool HasPaintedAncestor(System.Windows.Forms.Control c)
	{
		for (System.Windows.Forms.Control parent = c.Parent; parent != null; parent = parent.Parent)
		{
			if (parent.BackgroundImage != null)
			{
				return true;
			}
			if (s_nonFlatPaints.GetOrAdd(parent.GetType(), PaintsNonFlat))
			{
				return true;
			}
		}
		return false;
	}

	private static bool PaintsNonFlat(Type type)
	{
		return OverridesOutsideFramework(type, "OnPaint") || OverridesOutsideFramework(type, "OnPaintBackground");
	}

	private static bool OverridesOutsideFramework(Type type, string name)
	{
		MethodInfo method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if ((object)method == null || method.DeclaringType == typeof(System.Windows.Forms.Control))
		{
			return false;
		}
		// Stock framework overrides (Panel, ScrollableControl, ...) repaint the flat
		// BackColor; only foreign assemblies (Telerik, user controls) can paint
		// something a flat Clear cannot represent.
		return method.DeclaringType.Assembly != typeof(System.Windows.Forms.Control).Assembly;
	}

	private static Color EffectiveBackColor(System.Windows.Forms.Control c)
	{
		for (System.Windows.Forms.Control parent = c.Parent; parent != null; parent = parent.Parent)
		{
			Color backColor = parent.BackColor;
			if (!backColor.IsEmpty && backColor.A == byte.MaxValue)
			{
				return backColor;
			}
		}
		return System.Drawing.SystemColors.Control;
	}

	private static byte[]? ExtractBitmapRgba(Bitmap bmp)
	{
		if (bmp.Width <= 0 || bmp.Height <= 0)
		{
			return null;
		}
		try
		{
			// Single pass, no LockBits pin/roundtrip (used to be expand +
			// memcpy + swizzle: three full-pixel passes per frame).
			byte[] perfRgba = bmp.ToRgbaBytes();
			if (System.Windows.Forms.WasmFrameCounters.PerfEnabled && perfRgba != null)
				System.Windows.Forms.WasmFrameCounters.AddBytesExtracted(perfRgba.Length);
			return perfRgba;
		}
		catch
		{
			return null;
		}
	}

	private static bool PaintCenteredBitmap(ICanvas canvas, Bitmap bmp, float x, float y, float w, float h, System.Windows.Forms.Control? owner)
	{
		if (bmp.Width <= 0 || bmp.Height <= 0)
		{
			return false;
		}
		if (owner is not null && !IsFrameDirty(owner) && canvas is ICanvasRenderer { SupportsBitmapLayers: not false } rr)
		{
			string bid = LayerIdFor(bmp);
			bool have;
			bool sameSize = true;
			lock (s_layerHash)
			{
				have = s_layerHash.ContainsKey(bid);
				if (have && s_layerSize.TryGetValue(bid, out var bs))
					sameSize = bs.w == bmp.Width && bs.h == bmp.Height;
			}
			if (have && sameSize)
			{
				lock (s_liveLayers)
				{
					s_liveLayers.Add(bid);
				}
				rr.BlitBitmap(bid, x + (w - (float)bmp.Width) / 2f, y + (h - (float)bmp.Height) / 2f, bmp.Width, bmp.Height);
				return true;
			}
		}
		byte[] array = ToRgbaBytes(bmp);
		if (array == null)
		{
			return false;
		}
		RecordBitmap(bmp, canvas, array, bmp.Width, bmp.Height, x + (w - (float)bmp.Width) / 2f, y + (h - (float)bmp.Height) / 2f, bmp.Width, bmp.Height);
		return true;
	}

	private static byte[]? ToRgbaBytes(Bitmap bmp)
	{
		try
		{
			if (bmp.Width > 512 || bmp.Height > 512)
			{
				return null;
			}
			if (bmp.Width <= 0 || bmp.Height <= 0)
			{
				return null;
			}
			byte[] perfRgba = bmp.ToRgbaBytes();
			if (System.Windows.Forms.WasmFrameCounters.PerfEnabled && perfRgba != null)
				System.Windows.Forms.WasmFrameCounters.AddBytesExtracted(perfRgba.Length);
			return perfRgba;
		}
		catch
		{
			return null;
		}
	}

	private static uint ToRgba(Color c, uint fallback)
	{
		if (c.IsEmpty || c.A == 0)
		{
			return fallback;
		}
		return (uint)((c.R << 24) | (c.G << 16) | (c.B << 8) | c.A);
	}

	private static double ReadWorkspaceZoom(object? activeDw)
	{
		try
		{
			if (activeDw == null)
			{
				return 1.0;
			}
			PropertyInfo[] properties = activeDw.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (PropertyInfo propertyInfo in properties)
			{
				if (propertyInfo.Name.EndsWith("ScaleRatio", StringComparison.Ordinal))
				{
					object value = propertyInfo.GetValue(activeDw);
					double value2 = ((value is double num) ? num : ((value is float num2) ? ((double)num2) : 1.0));
					return Math.Clamp(value2, 0.05, 64.0);
				}
			}
		}
		catch
		{
		}
		return 1.0;
	}

	private static void ReadViewportOffset(object? activeDw, out double offX, out double offY)
	{
		offX = 0.0;
		offY = 0.0;
		try
		{
			if (activeDw == null)
			{
				return;
			}
			PropertyInfo[] properties = activeDw.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (PropertyInfo propertyInfo in properties)
			{
				if (propertyInfo.Name.EndsWith("ViewportCanvasOffset", StringComparison.Ordinal))
				{
					object value = propertyInfo.GetValue(activeDw);
					if (value != null)
					{
						offX = ReadDoubleMember(value, "X");
						offY = ReadDoubleMember(value, "Y");
					}
					break;
				}
			}
		}
		catch
		{
		}
	}

	private static double ReadDoubleMember(object o, string name)
	{
		try
		{
			Type type = o.GetType();
			object obj = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(o) ?? type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(o) ?? type.GetField(char.ToLowerInvariant(name[0]) + name.Substring(1), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(o);
			if (obj is double result)
			{
				return result;
			}
			if (obj is float num)
			{
				return num;
			}
		}
		catch
		{
		}
		return 0.0;
	}

	private static object? GetActiveDocumentWorkspace(System.Windows.Forms.Control c)
	{
		for (System.Windows.Forms.Control control = c; control != null; control = control.Parent)
		{
			if (control.GetType().Name == "AppWorkspace")
			{
				return control.GetType().GetProperty("ActiveDocumentWorkspace", BindingFlags.Instance | BindingFlags.Public)?.GetValue(control);
			}
		}
		return null;
	}

	private static void AppendToolProp(StringBuilder sb, string name, object? raw, int depth)
	{
		if (raw == null)
		{
			sb.Append(' ').Append(name).Append("=null");
			return;
		}
		Type type = raw.GetType();
		if (type.IsPrimitive || type.IsEnum || raw is string || raw is decimal)
		{
			string text = raw.ToString() ?? "null";
			if (text.Length > 40)
			{
				text = text.Substring(0, 40);
			}
			sb.Append(' ').Append(name).Append('=')
				.Append(text);
			return;
		}
		if (depth > 0 || sb.Length > 240)
		{
			string text2 = raw.ToString() ?? "null";
			if (text2.Length > 40)
			{
				text2 = text2.Substring(0, 40);
			}
			sb.Append(' ').Append(name).Append('=')
				.Append(text2);
			return;
		}
		try
		{
			PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
			foreach (PropertyInfo propertyInfo in properties)
			{
				if (propertyInfo.GetIndexParameters().Length == 0 && !(propertyInfo.Name == "Item") && !(propertyInfo.Name == "SyncRoot") && !(propertyInfo.Name == "Count"))
				{
					object value;
					try
					{
						value = propertyInfo.GetValue(raw);
					}
					catch
					{
						continue;
					}
					AppendToolProp(sb, name + "." + propertyInfo.Name, value, depth + 1);
					if (sb.Length > 280)
					{
						break;
					}
				}
			}
		}
		catch
		{
		}
	}

	private string GetActiveDocToolName()
	{
		try
		{
			object activeDocumentWorkspaceFromActiveForm = GetActiveDocumentWorkspaceFromActiveForm();
			object obj = activeDocumentWorkspaceFromActiveForm?.GetType().GetProperty("Tool", BindingFlags.Instance | BindingFlags.Public)?.GetValue(activeDocumentWorkspaceFromActiveForm);
			if (obj == null)
			{
				return "null";
			}
			StringBuilder stringBuilder = new StringBuilder(obj.GetType().Name);
			try
			{
				object? obj2 = obj.GetType().GetProperty("State", BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj);
				stringBuilder.Append(" state=").Append(obj2?.ToString() ?? "?");
			}
			catch
			{
			}
			try
			{
				object obj4 = obj.GetType().GetProperty("DrawingSettingsValues", BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj);
				if (obj4 != null)
				{
					PropertyInfo[] properties = obj4.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
					foreach (PropertyInfo propertyInfo in properties)
					{
						if (propertyInfo.GetIndexParameters().Length == 0)
						{
							object raw;
							try
							{
								raw = propertyInfo.GetValue(obj4);
							}
							catch
							{
								raw = null;
							}
							AppendToolProp(stringBuilder, propertyInfo.Name, raw, 0);
							if (stringBuilder.Length > 300)
							{
								break;
							}
						}
					}
				}
			}
			catch
			{
			}
			return stringBuilder.ToString();
		}
		catch
		{
			return "unknown";
		}
	}

	private object? GetActiveDocumentWorkspaceFromActiveForm()
	{
		if (_activeForm == null)
		{
			return null;
		}
		System.Windows.Forms.Control? appWorkspace = FindControlByTypeName(_activeForm, "AppWorkspace");
		if (appWorkspace == null)
		{
			return null;
		}
		return appWorkspace.GetType().GetProperty("ActiveDocumentWorkspace", BindingFlags.Instance | BindingFlags.Public)?.GetValue(appWorkspace);
	}

	private static System.Windows.Forms.Control? FindControlByTypeName(System.Windows.Forms.Control root, string typeName)
	{
		// Breadth-first: direct children first (wrapper layout), then deeper
		// levels (real MainForm nests AppWorkspace inside dock panels).
		Queue<System.Windows.Forms.Control> queue = new Queue<System.Windows.Forms.Control>();
		queue.Enqueue(root);
		while (queue.Count > 0)
		{
			System.Windows.Forms.Control control = queue.Dequeue();
			if (control.GetType().Name == typeName)
			{
				return control;
			}
			foreach (System.Windows.Forms.Control control2 in control.Controls)
			{
				queue.Enqueue(control2);
			}
		}
		return null;
	}

	private static object? GetActiveDocumentSurface(object? activeDw, out int docW, out int docH)
	{
		docW = 800;
		docH = 600;
		if (activeDw == null)
		{
			return null;
		}
		try
		{
			object obj = activeDw.GetType().GetProperty("Document", BindingFlags.Instance | BindingFlags.Public)?.GetValue(activeDw);
			if (obj != null)
			{
				docW = (int)(obj.GetType().GetProperty("Width", BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj) ?? ((object)800));
				docH = (int)(obj.GetType().GetProperty("Height", BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj) ?? ((object)600));
				object obj2 = activeDw.GetType().GetProperty("ActiveLayer", BindingFlags.Instance | BindingFlags.Public)?.GetValue(activeDw);
				if (obj2 != null)
				{
					object obj3 = obj2.GetType().GetProperty("Surface", BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj2);
					if (obj3 != null)
					{
						return obj3;
					}
				}
				object obj4 = obj.GetType().GetProperty("Layers", BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj);
				if (obj4 is IList { Count: >0 } list)
				{
					object obj5 = list[0];
					object obj6 = obj5?.GetType().GetProperty("Surface", BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj5);
					if (obj6 != null)
					{
						return obj6;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("[PaintDotNet] GetActiveDocumentSurface error: " + ex.Message);
		}
		return null;
	}

	private unsafe string DescribeActiveDocSurface()
	{
		try
		{
			object activeDocumentWorkspaceFromActiveForm = GetActiveDocumentWorkspaceFromActiveForm();
			int docW;
			int docH;
			object activeDocumentSurface = GetActiveDocumentSurface(activeDocumentWorkspaceFromActiveForm, out docW, out docH);
			if (activeDocumentSurface == null)
			{
				return "none";
			}
			object obj = activeDocumentSurface.GetType().GetProperty("Scan0", BindingFlags.Instance | BindingFlags.Public)?.GetValue(activeDocumentSurface);
			nint num = (nint)(obj?.GetType().GetProperty("Pointer", BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj) ?? ((object)IntPtr.Zero));
			if (num != IntPtr.Zero && (nuint)num > (nuint)65535u)
			{
				int num2 = 0;
				uint* ptr = (uint*)((IntPtr)num).ToPointer();
				for (int i = 0; i < docW * docH; i++)
				{
					if (ptr[i] != uint.MaxValue && ptr[i] != 0)
					{
						num2++;
					}
				}
				return $"{docW}x{docH} custom={num2}";
			}
			return $"{docW}x{docH} unreadable";
		}
		catch (Exception ex)
		{
			return "error:" + ex.GetType().Name;
		}
	}

	private unsafe static byte[]? ExtractSurfaceRgba(object surface, int w, int h)
	{
		try
		{
			object obj = surface.GetType().GetProperty("Scan0", BindingFlags.Instance | BindingFlags.Public)?.GetValue(surface);
			if (obj == null)
			{
				return null;
			}
			nint num = (nint)(obj.GetType().GetProperty("Pointer", BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj) ?? ((object)IntPtr.Zero));
			if (num == IntPtr.Zero || (nuint)num <= (nuint)65535u)
			{
				return null;
			}
			long num2 = (long)(obj.GetType().GetProperty("Length", BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj) ?? ((object)0L));
			if (num2 > 0 && num2 < (long)w * (long)h * 4)
			{
				return null;
			}
			int num3 = (int)(surface.GetType().GetProperty("Stride", BindingFlags.Instance | BindingFlags.Public)?.GetValue(surface) ?? ((object)(w * 4)));
			bool flag = false;
			byte* ptr = (byte*)((IntPtr)num).ToPointer();
			int num4 = w * h;
			uint* ptr2 = (uint*)ptr;
			for (int i = 0; i < num4; i++)
			{
				if (ptr2[i] != uint.MaxValue && ptr2[i] != 0)
				{
					flag = true;
					break;
				}
			}
			if (!flag)
			{
				return null;
			}
			byte[] array = new byte[w * h * 4];
			byte* ptr3 = (byte*)((IntPtr)num).ToPointer();
			fixed (byte* ptr4 = array)
			{
				for (int j = 0; j < h; j++)
				{
					byte* ptr5 = ptr3 + j * num3;
					byte* ptr6 = ptr4 + j * w * 4;
					for (int k = 0; k < w; k++)
					{
						int num5 = k * 4;
						int num6 = k * 4;
						ptr6[num6] = ptr5[num5 + 2];
						ptr6[num6 + 1] = ptr5[num5 + 1];
						ptr6[num6 + 2] = ptr5[num5];
						ptr6[num6 + 3] = ptr5[num5 + 3];
					}
				}
			}
			return array;
		}
		catch (Exception ex)
		{
			Console.WriteLine("[PaintDotNet] ExtractSurfaceRgba error: " + ex.Message);
			return null;
		}
	}

	// Live composited read (layers + tool overlays) for in-flight strokes.
	// Chain (vendor, decompile-verified): workspace.DocumentCanvas .
	// DocumentCanvasLayer . DocumentRenderer . GetSnapshotBitmap() ->
	// IBitmapSource<ColorBgra32>.CopyPixels(pBuffer, stride, size, null).
	// Returns null on ANY failure so the caller falls back to the raw
	// read; the working path is never at risk.
	private unsafe static byte[]? ExtractCompositedRgba(object workspace, int w, int h)
	{
		try
		{
			object? canvas = workspace.GetType().GetProperty("DocumentCanvas", BindingFlags.Instance | BindingFlags.Public)?.GetValue(workspace);
			object? layer = canvas?.GetType().GetProperty("DocumentCanvasLayer", BindingFlags.Instance | BindingFlags.Public)?.GetValue(canvas);
			// DocumentRenderer is internal on DocumentCanvasLayer: bind
			// non-public too (diag proved the public-only lookup missed).
			object? renderer = layer?.GetType().GetProperty("DocumentRenderer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(layer);
			renderer = renderer ?? layer?.GetType().GetField("documentRenderer", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(layer);
			object? snapshot = renderer?.GetType().GetMethod("GetSnapshotBitmap", BindingFlags.Instance | BindingFlags.Public)?.Invoke(renderer, null);
			if (_compositedDiagCount < 3)
			{
				_compositedDiagCount++;
				Console.WriteLine($"[PaintDotNet] Composited diag #{_compositedDiagCount}: canvas={(canvas == null ? "null" : canvas.GetType().Name)} layer={(layer == null ? "null" : layer.GetType().Name)} renderer={(renderer == null ? "null" : renderer.GetType().Name)} snapshot={(snapshot == null ? "null" : snapshot.GetType().Name)}");
			}
			if (snapshot == null || w <= 0 || h <= 0)
			{
				return null;
			}
			MethodInfo? copy = null;
			foreach (MethodInfo m in snapshot.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
			{
				if (m.Name != "CopyPixels")
				{
					continue;
				}
				var ps = m.GetParameters();
				if (ps.Length == 4 && ps[0].ParameterType.IsPointer && ps[1].ParameterType == typeof(int) && ps[2].ParameterType == typeof(uint))
				{
					copy = m;
					break;
				}
			}
			if (copy == null)
			{
				return null;
			}
			byte[] rgba = new byte[w * h * 4];
			fixed (byte* dst = rgba)
			{
				// The CopyPixels slot takes void*: box the buffer as a
				// void* Pointer (byte* does not convert).
				object boxed = System.Reflection.Pointer.Box(dst, typeof(void).MakePointerType());
				copy.Invoke(snapshot, new object?[] { boxed, w * 4, (uint)(w * h * 4), null });
			}
			// Vendor pixels are BGRA-ordered (ColorBgra32, same family as
			// the Scan0 path above): swap to RGBA in place.
			for (int b = 0; b < rgba.Length; b += 4)
			{
				byte tmp = rgba[b];
				rgba[b] = rgba[b + 2];
				rgba[b + 2] = tmp;
			}
			return rgba;
		}
		catch (Exception ex)
		{
			Console.WriteLine("[PaintDotNet] ExtractCompositedRgba error: " + ex.Message);
			return null;
		}
	}

	private static bool IsD2DControl(System.Windows.Forms.Control c)
	{
		string key = c.GetType().FullName ?? "";
		lock (s_d2dTypeCache)
		{
			if (s_d2dTypeCache.TryGetValue(key, out var value))
			{
				return value;
			}
			bool flag = false;
			Type type = c.GetType();
			while (type != null)
			{
				if (type.FullName == "PaintDotNet.Controls.Direct2DControl")
				{
					flag = true;
					break;
				}
				type = type.BaseType;
			}
			s_d2dTypeCache[key] = flag;
			return flag;
		}
	}

	private static MethodInfo? ResolveD2DRoot(Type controlType)
	{
		string key = controlType.FullName ?? "";
		lock (s_d2dRootCache)
		{
			if (s_d2dRootCache.TryGetValue(key, out MethodInfo value))
			{
				return value;
			}
			MethodInfo methodInfo = null;
			Type type = controlType;
			while (type != null && methodInfo == null)
			{
				MethodInfo[] methods = type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				foreach (MethodInfo methodInfo2 in methods)
				{
					if (!(methodInfo2.Name != "OnRenderAdvanced") && methodInfo2.GetParameters().Length == 2 && !(methodInfo2.GetParameters()[1].ParameterType.FullName != "PaintDotNet.Rendering.RectFloat") && !(methodInfo2.DeclaringType?.FullName == "PaintDotNet.Controls.AdvancedDirect2DControl"))
					{
						methodInfo = methodInfo2;
						break;
					}
				}
				type = type.BaseType;
			}
			if (methodInfo == null)
			{
				Type type2 = controlType;
				while (type2 != null && methodInfo == null)
				{
					MethodInfo[] methods2 = type2.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					foreach (MethodInfo methodInfo3 in methods2)
					{
						if (methodInfo3.Name != "OnRender" || methodInfo3.GetParameters().Length != 2)
						{
							continue;
						}
						ParameterInfo[] parameters = methodInfo3.GetParameters();
						if (parameters[1].ParameterType.FullName != "PaintDotNet.Rendering.RectFloat")
						{
							continue;
						}
						string fullName = parameters[0].ParameterType.FullName;
						if (!(fullName != "PaintDotNet.Direct2D1.IDrawingContext") || !(fullName != "PaintDotNet.Direct2D1.IDeviceContext"))
						{
							string text = methodInfo3.DeclaringType?.FullName;
							if (!(text == "PaintDotNet.Controls.InternalDirect2DControl") && !(text == "PaintDotNet.Controls.Direct2DControl"))
							{
								methodInfo = methodInfo3;
								break;
							}
						}
					}
					type2 = type2.BaseType;
				}
			}
			s_d2dRootCache[key] = methodInfo;
			return methodInfo;
		}
	}

	private static bool EnsureD2DTypes()
	{
		if (s_cpuCtxType != null && s_rectFloatCtor != null)
		{
			return true;
		}
		try
		{
			s_cpuCtxType = s_cpuCtxType ?? Type.GetType("PaintDotNet.Direct2D1.CpuDrawingContext, PaintDotNet.Windows.Core");
		}
		catch
		{
		}
		try
		{
			s_rectFloatType = s_rectFloatType ?? Type.GetType("PaintDotNet.Rendering.RectFloat, PaintDotNet.Primitives");
		}
		catch
		{
		}
		try
		{
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
			foreach (Assembly assembly in assemblies)
			{
				if (assembly.GetName().Name == "PaintDotNet.Windows.Core")
				{
					Console.WriteLine("[D2DDBG2] core asm: " + assembly.FullName + " loc=" + assembly.Location);
				}
			}
			Console.WriteLine("[D2DDBG2] core scan done");
			bool flag = false;
			Assembly[] assemblies2 = AppDomain.CurrentDomain.GetAssemblies();
			foreach (Assembly assembly2 in assemblies2)
			{
				if (assembly2.GetName().Name == "PaintDotNet.Framework")
				{
					flag = true;
				}
				if (assembly2.GetName().Name == "PaintDotNet.Windows.Core")
				{
					try
					{
						Type type = assembly2.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
						Console.WriteLine("[D2DDBG2] asm.GetType => " + ((type == null) ? "null" : "FOUND"));
					}
					catch (Exception ex)
					{
						Console.WriteLine("[D2DDBG2] asm.GetType threw: " + ex.GetType().Name);
					}
				}
			}
			Console.WriteLine("[D2DDBG2] fwLoaded=" + flag);
		}
		catch (Exception ex2)
		{
			Console.WriteLine("[D2DDBG2] scan threw: " + ex2.GetType().Name);
		}
		try
		{
			Type type2 = Type.GetType("PaintDotNet.Direct2D1.CpuDrawingContext, PaintDotNet.Windows.Core");
			Console.WriteLine("[D2DDBG2] Type.GetType => " + ((type2 == null) ? "null" : type2.Assembly.FullName));
		}
		catch (Exception ex3)
		{
			Console.WriteLine("[D2DDBG2] Type.GetType threw: " + ex3.GetType().Name + ": " + ex3.Message.Split('\n')[0]);
		}
		string[] array = new string[2] { "PaintDotNet.Primitives", "PaintDotNet.Windows.Core" };
		foreach (string assemblyString in array)
		{
			try
			{
				Assembly.Load(assemblyString);
			}
			catch
			{
			}
		}
		Assembly[] assemblies3 = AppDomain.CurrentDomain.GetAssemblies();
		foreach (Assembly assembly3 in assemblies3)
		{
			try
			{
				if (!(assembly3.GetName().Name != "paintdotnet"))
				{
					if ((object)s_cpuCtxType == null)
					{
						s_cpuCtxType = assembly3.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
					}
					if ((object)s_rectFloatType == null)
					{
						s_rectFloatType = assembly3.GetType("PaintDotNet.Rendering.RectFloat");
					}
				}
			}
			catch
			{
			}
		}
		Assembly[] assemblies4 = AppDomain.CurrentDomain.GetAssemblies();
		foreach (Assembly assembly4 in assemblies4)
		{
			try
			{
				if ((object)s_cpuCtxType == null)
				{
					s_cpuCtxType = assembly4.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
				}
				if ((object)s_rectFloatType == null)
				{
					s_rectFloatType = assembly4.GetType("PaintDotNet.Rendering.RectFloat");
				}
			}
			catch
			{
			}
		}
		if (s_rectFloatType != null && s_rectFloatCtor == null)
		{
			ConstructorInfo[] constructors = s_rectFloatType.GetConstructors();
			foreach (ConstructorInfo constructorInfo in constructors)
			{
				ParameterInfo[] parameters = constructorInfo.GetParameters();
				if (parameters.Length == 4 && parameters[0].ParameterType == typeof(float) && parameters[1].ParameterType == typeof(float) && parameters[2].ParameterType == typeof(float) && parameters[3].ParameterType == typeof(float))
				{
					s_rectFloatCtor = constructorInfo;
					break;
				}
			}
		}
		if (s_cpuCtxType != null && s_cpuCtxCtor == null)
		{
			ConstructorInfo[] constructors2 = s_cpuCtxType.GetConstructors();
			foreach (ConstructorInfo constructorInfo2 in constructors2)
			{
				ParameterInfo[] parameters2 = constructorInfo2.GetParameters();
				if (parameters2.Length == 1 && parameters2[0].ParameterType == typeof(long))
				{
					s_cpuCtxCtor = constructorInfo2;
					break;
				}
			}
		}
		if (!s_d2dTypesLogged)
		{
			s_d2dTypesLogged = true;
			int value = 0;
			string text = "";
			try
			{
				Assembly[] assemblies5 = AppDomain.CurrentDomain.GetAssemblies();
				value = assemblies5.Length;
				Assembly[] array2 = assemblies5;
				foreach (Assembly assembly5 in array2)
				{
					string text2 = assembly5.GetName().Name ?? "?";
					if (text2.StartsWith("PaintDotNet"))
					{
						text = text + text2 + ";";
					}
				}
			}
			catch
			{
			}
			Console.WriteLine($"[D2DDBG] cpuCtx={s_cpuCtxType != null} rectType={s_rectFloatType != null} rectCtor={s_rectFloatCtor != null} asms={value} pdn=[{text}]");
		}
		return s_cpuCtxType != null && s_rectFloatCtor != null;
	}

	private static void TempD2DNullProbe(System.Windows.Forms.Control c, string tn)
	{
		try
		{
			List<string> list = new List<string>();
			Type type = c.GetType();
			while (type != null && type.FullName != "System.Windows.Forms.Control")
			{
				FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				foreach (FieldInfo fieldInfo in fields)
				{
					string text = fieldInfo.FieldType.FullName ?? "";
					if (text.Contains("Effect") || text.Contains("Bitmap") || text.Contains("Brush") || text.Contains("DeviceContext") || text.Contains("CommandList"))
					{
						object obj = null;
						try
						{
							obj = fieldInfo.GetValue(c);
						}
						catch
						{
							obj = "unreadable";
						}
						list.Add(fieldInfo.Name + "=" + ((obj == null) ? "null" : "ok"));
					}
				}
				type = type.BaseType;
			}
			Console.WriteLine("[D2DNULLS] " + tn + ": " + string.Join(",", list));
		}
		catch
		{
		}
	}

	private void TryRenderD2DControl(ICanvas canvas, System.Windows.Forms.Control c, int absX, int absY, int w, int h)
	{
		if (!IsD2DControl(c) || (long)w * (long)h > 2000000)
		{
			return;
		}
		string text = c.GetType().FullName ?? "?";
		lock (s_d2dRenderFailed)
		{
			if (s_d2dRenderFailed.Contains(text))
			{
				return;
			}
		}
		if (text.EndsWith("ColorWheelControl", StringComparison.Ordinal))
		{
			lock (s_d2dRenderFailed)
			{
				s_d2dRenderFailed.Add(text);
				return;
			}
		}
		try
		{
			lock (s_d2dRenderFailed)
			{
				if (s_d2dRenderFailed.Add("try:" + text))
				{
					Console.WriteLine("[D2DTRY] " + text);
				}
			}
			lock (s_d2dRenderFailed)
			{
				if (s_d2dRenderFailed.Add("nulls:" + text))
				{
					TempD2DNullProbe(c, text);
				}
			}
			RenderD2DControlCore(canvas, c, absX, absY, w, h);
		}
		catch (Exception ex)
		{
			lock (s_d2dRenderFailed)
			{
				s_d2dRenderFailed.Add(text);
			}
			Exception ex2 = ex;
			while (ex2 is TargetInvocationException && ex2.InnerException != null)
			{
				ex2 = ex2.InnerException;
			}
			string value = ex2.Message.Split('\n')[0];
			Console.WriteLine($"[D2D] {text} CPU render failed: {ex2.GetType().Name}: {value}");
			try
			{
				StackFrame stackFrame = ((StackTrace)null)?.GetFrame(0);
				if (stackFrame != null)
				{
					Console.WriteLine($"[D2DIL] {text} il=0x{stackFrame.GetILOffset():x}");
				}
			}
			catch
			{
			}
			try
			{
				string[] array = Array.Empty<string>();
				int num = 0;
				string[] array2 = array;
				foreach (string text2 in array2)
				{
					string text3 = text2.Trim();
					if (text3.Length != 0)
					{
						Console.WriteLine($"[D2DSTACK] {text} #{num}: " + ((text3.Length > 220) ? text3.Substring(0, 220) : text3));
						if (++num >= 8)
						{
							break;
						}
					}
				}
			}
			catch
			{
			}
		}
	}

	private static void TempD2DDeepProbe(System.Windows.Forms.Control c, object cpuCtx, string tn)
	{
		try
		{
			Assembly assembly = c.GetType().Assembly;
			try
			{
				MethodInfo method = cpuCtx.GetType().GetMethod("CreateCommandList", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				object obj = ((method == null) ? "missing" : method.Invoke(cpuCtx, null));
				Console.WriteLine("[D2DDEEP] " + tn + " cmdlist=" + ((obj == null) ? "null" : "ok"));
			}
			catch (Exception ex)
			{
				Console.WriteLine("[D2DDEEP] " + tn + " cmdlist=FAIL:" + ex.GetType().Name);
			}
			try
			{
				Type type = null;
				string[] array = new string[1] { "PaintDotNet.VisualStyling.ColorRectangleDirect2D" };
				foreach (string text in array)
				{
					type = assembly.GetType(text);
					string[] array2 = new string[3] { "PaintDotNet.Framework", "PaintDotNet.Core", "paintdotnet" };
					foreach (string text2 in array2)
					{
						if (type != null)
						{
							break;
						}
						try
						{
							type = Type.GetType(text + ", " + text2);
						}
						catch
						{
						}
					}
					if (type != null)
					{
						break;
					}
				}
				if (type == null)
				{
					Console.WriteLine("[D2DDEEP] " + tn + " render=nortype");
				}
				else
				{
					MethodInfo methodInfo = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo m) => m.Name == "Render" && m.GetParameters().Length == 6);
					if (methodInfo == null)
					{
						Console.WriteLine("[D2DDEEP] " + tn + " render=nomethod");
					}
					else
					{
						ParameterInfo[] parameters = methodInfo.GetParameters();
						object[] array3 = new object[parameters.Length];
						for (int num = 0; num < parameters.Length; num++)
						{
							Type parameterType = parameters[num].ParameterType;
							if (parameterType.IsInstanceOfType(cpuCtx))
							{
								array3[num] = cpuCtx;
							}
							else if (parameterType == typeof(bool))
							{
								array3[num] = true;
							}
							else if (parameterType.IsValueType)
							{
								array3[num] = Activator.CreateInstance(parameterType);
							}
							else
							{
								array3[num] = null;
							}
						}
						methodInfo.Invoke(null, array3);
						Console.WriteLine("[D2DDEEP] " + tn + " render=ok");
					}
				}
			}
			catch (Exception ex2)
			{
				Exception ex3 = ex2;
				while (ex3 is TargetInvocationException && ex3.InnerException != null)
				{
					ex3 = ex3.InnerException;
				}
				Console.WriteLine($"[D2DDEEP] {tn} render=FAIL:{ex3.GetType().FullName}:{ex3.Message.Split('\n')[0]}");
				string[] array4 = (ex3.StackTrace ?? "").Split('\n');
				foreach (string text3 in array4)
				{
					string text4 = text3.Trim();
					if (text4.Length != 0)
					{
						Console.WriteLine("[D2DDEEPST] " + tn + " " + text4.Substring(0, Math.Min(200, text4.Length)));
					}
				}
			}
			(string, string)[] array5 = new(string, string)[2]
			{
				("checkers", "CheckerboardEffect"),
				("colormgmt", "ColorManagementEffect")
			};
			for (int num3 = 0; num3 < array5.Length; num3++)
			{
				(string, string) tuple = array5[num3];
				try
				{
					Type type2 = null;
					string[] array6 = new string[2]
					{
						"PaintDotNet.VisualStyling." + tuple.Item2,
						"PaintDotNet.Direct2D1.Effects." + tuple.Item2
					};
					foreach (string text5 in array6)
					{
						type2 = assembly.GetType(text5);
						string[] array7 = new string[4] { "PaintDotNet.Core", "PaintDotNet.Windows.Core", "PaintDotNet.Framework", "paintdotnet" };
						foreach (string text6 in array7)
						{
							if (type2 != null)
							{
								break;
							}
							try
							{
								type2 = Type.GetType(text5 + ", " + text6);
							}
							catch
							{
							}
						}
						if (type2 != null)
						{
							break;
						}
					}
					if (type2 == null)
					{
						Console.WriteLine($"[D2DDEEP] {tn} {tuple.Item1}=notype");
					}
					else
					{
						object obj4 = Activator.CreateInstance(type2, cpuCtx);
						object obj5 = type2.GetMethod("get_Properties")?.Invoke(obj4, null);
						Console.WriteLine($"[D2DDEEP] {tn} {tuple.Item1}=ok props={((obj5 == null) ? "null" : "ok")}");
					}
				}
				catch (Exception ex4)
				{
					Exception ex5 = ((ex4 is TargetInvocationException && ex4.InnerException != null) ? ex4.InnerException : ex4);
					Console.WriteLine($"[D2DDEEP] {tn} {tuple.Item1}=FAIL:{ex5.GetType().Name}:{ex5.Message.Split('\n')[0]}");
				}
			}
			try
			{
				Type type3 = null;
				string[] array8 = new string[4] { "PaintDotNet.Windows.Core", "PaintDotNet.Core", "PaintDotNet.Framework", "paintdotnet" };
				foreach (string text7 in array8)
				{
					try
					{
						type3 = Type.GetType("PaintDotNet.Direct2D1.Effects.CropEffect, " + text7);
					}
					catch
					{
					}
					if (type3 != null)
					{
						break;
					}
				}
				if (type3 == null)
				{
					Console.WriteLine("[D2DDEEP] " + tn + " crop=notype");
				}
				else
				{
					object obj7 = Activator.CreateInstance(type3, cpuCtx);
					object obj8 = type3.GetMethod("get_Properties")?.Invoke(obj7, null);
					string text8 = "?";
					string text9 = "?";
					try
					{
						MethodInfo methodInfo2 = obj8?.GetType().GetMethod("get_Storage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						text8 = ((methodInfo2 == null) ? "no-getter" : ((methodInfo2.Invoke(obj8, null) == null) ? "null" : "ok"));
					}
					catch (Exception ex6)
					{
						text8 = "THROW:" + ex6.GetType().Name;
					}
					try
					{
						MethodInfo methodInfo3 = obj8?.GetType().GetMethod("get_Factory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						text9 = ((methodInfo3 == null) ? "no-getter" : ((methodInfo3.Invoke(obj8, null) == null) ? "null" : "ok"));
					}
					catch (Exception ex7)
					{
						text9 = "THROW:" + ex7.GetType().Name;
					}
					Console.WriteLine($"[D2DDEEP] {tn} crop=ok props={((obj8 == null) ? "null" : "ok")} factory={text9} storage={text8}");
					try
					{
						object obj9 = (obj8?.GetType().GetMethod("get_Input"))?.Invoke(obj8, null);
						object obj10 = (obj9?.GetType().GetMethod("Get", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))?.Invoke(obj9, null);
						Console.WriteLine("[D2DDEEP] " + tn + " cropinput=" + ((obj10 == null) ? "null" : obj10.GetType().FullName));
					}
					catch (Exception ex8)
					{
						Exception ex9 = ((ex8 is TargetInvocationException && ex8.InnerException != null) ? ex8.InnerException : ex8);
						Console.WriteLine($"[D2DDEEP] {tn} cropinput=FAIL:{ex9.GetType().Name}:{ex9.Message.Split('\n')[0]}");
					}
				}
			}
			catch (Exception ex10)
			{
				Exception ex11 = ((ex10 is TargetInvocationException && ex10.InnerException != null) ? ex10.InnerException : ex10);
				Console.WriteLine($"[D2DDEEP] {tn} crop=FAIL:{ex11.GetType().Name}:{ex11.Message.Split('\n')[0]}");
			}
			try
			{
				List<string> source = (from type4 in cpuCtx.GetType().GetInterfaces()
					select type4.FullName).ToList();
				bool value = source.Any((string n) => n?.Contains("IDeviceEffectFactory") ?? false);
				MethodInfo method2 = cpuCtx.GetType().GetMethod("CreateEffect", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				Console.WriteLine($"[D2DDEEP] {tn} factory impl={value} method={((method2 == null) ? "missing" : "present")}");
			}
			catch (Exception ex12)
			{
				Console.WriteLine("[D2DDEEP] " + tn + " factory=FAIL:" + ex12.GetType().Name);
			}
		}
		catch
		{
		}
	}

	private static void TempD2DPostProbe(System.Windows.Forms.Control c, string tn)
	{
		try
		{
			FieldInfo field = c.GetType().GetField("croppedCheckersEffect", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field == null)
			{
				Console.WriteLine("[D2DDEEP] " + tn + " postcrop=nofield");
				return;
			}
			object value = field.GetValue(c);
			if (value == null)
			{
				Console.WriteLine("[D2DDEEP] " + tn + " postcrop=null");
				return;
			}
			MethodInfo method = value.GetType().GetMethod("get_Properties");
			object obj = null;
			try
			{
				obj = method?.Invoke(value, null);
			}
			catch (Exception ex)
			{
				Console.WriteLine("[D2DDEEP] " + tn + " postcrop-getprops=THROW:" + ex.GetType().Name);
				return;
			}
			string text = "?";
			string text2 = "?";
			string text3 = "?";
			try
			{
				MethodInfo methodInfo = obj?.GetType().GetMethod("get_Storage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				text = ((methodInfo == null) ? "no-getter" : ((methodInfo.Invoke(obj, null) == null) ? "null" : "ok"));
			}
			catch (Exception ex2)
			{
				text = "THROW:" + ex2.GetType().Name;
			}
			try
			{
				MethodInfo methodInfo2 = obj?.GetType().GetMethod("get_Factory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				text2 = ((methodInfo2 == null) ? "no-getter" : ((methodInfo2.Invoke(obj, null) == null) ? "null" : "ok"));
			}
			catch (Exception ex3)
			{
				text2 = "THROW:" + ex3.GetType().Name;
			}
			try
			{
				FieldInfo fieldInfo = value.GetType().Assembly.GetType("PaintDotNet.Direct2D1.Effects.DeviceEffect")?.GetField("effect", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				object obj2 = fieldInfo?.GetValue(value);
				text3 = ((fieldInfo == null) ? "no-field" : ((obj2 == null) ? "null" : obj2.GetType().FullName));
			}
			catch (Exception ex4)
			{
				text3 = "THROW:" + ex4.GetType().Name;
			}
			Console.WriteLine($"[D2DDEEP] {tn} postcrop props={((obj == null) ? "null" : "ok")} factory={text2} storage={text} effect={text3}");
		}
		catch
		{
		}
	}

	private static void RenderD2DControlCore(ICanvas canvas, System.Windows.Forms.Control c, int absX, int absY, int w, int h)
	{
		if (!EnsureD2DTypes())
		{
			throw new InvalidOperationException("CpuDrawingContext/RectFloat missing");
		}
		MethodInfo methodInfo = ResolveD2DRoot(c.GetType());
		if (methodInfo == null)
		{
			throw new InvalidOperationException("no OnRenderAdvanced/OnRender content override");
		}
		byte[] array = new byte[w * h * 4];
		GCHandle gCHandle = GCHandle.Alloc(array, GCHandleType.Pinned);
		try
		{
			long num = CpuPaint.CreateTarget(gCHandle.AddrOfPinnedObject(), w, h, w * 4, 4);
			if (num == 0)
			{
				throw new InvalidOperationException("CreateTarget failed");
			}
			try
			{
				CpuPaint.ClearTarget(num, 0f, 0f, 0f, 0f, hasColor: false);
				object obj = (((object)s_cpuCtxCtor != null) ? s_cpuCtxCtor.Invoke(new object[1] { num }) : Activator.CreateInstance(s_cpuCtxType, num));
				string text = c.GetType().FullName ?? "?";
				lock (s_d2dRenderFailed)
				{
					if (s_d2dRenderFailed.Add("deep:" + text))
					{
						TempD2DDeepProbe(c, obj, text);
					}
				}
				try
				{
					try
					{
						ResolveD2DInit(c.GetType())?.Invoke(c, null);
					}
					catch
					{
					}
					try
					{
						ResolveD2DCreated(c.GetType())?.Invoke(c, null);
					}
					catch (Exception ex)
					{
						lock (s_d2dRenderFailed)
						{
							if (s_d2dRenderFailed.Add("created:" + text))
							{
								Console.WriteLine($"[D2D] {text} OnDeviceContextCreated failed: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
							}
						}
					}
					object obj3 = s_rectFloatCtor.Invoke(new object[4]
					{
						0f,
						0f,
						(float)w,
						(float)h
					});
					methodInfo.Invoke(c, new object[2] { obj, obj3 });
				}
				finally
				{
					(obj as IDisposable)?.Dispose();
				}
			}
			finally
			{
				CpuPaint.FreeContext(num);
			}
		}
		finally
		{
			gCHandle.Free();
		}
		for (int i = 0; i < array.Length; i += 4)
		{
			byte b = array[i];
			array[i] = array[i + 2];
			array[i + 2] = b;
		}
		RecordBitmap(c, canvas, array, w, h, absX, absY, w, h);
		if (System.Windows.Forms.WasmFrameCounters.PerfEnabled)
			System.Windows.Forms.WasmFrameCounters.CountRasterFull((long)w * h);
	}




	internal static MethodInfo? ResolveD2DCreated(Type controlType)
	{
		string key = controlType.FullName ?? "";
		lock (s_d2dCreatedCache)
		{
			if (s_d2dCreatedCache.TryGetValue(key, out MethodInfo value))
			{
				return value;
			}
			MethodInfo methodInfo = ResolveD2DCreatedCore(controlType);
			s_d2dCreatedCache[key] = methodInfo;
			return methodInfo;
		}
	}

	private static MethodInfo? ResolveD2DCreatedCore(Type controlType)
	{
		Type type = controlType;
		while (type != null)
		{
			string fullName = type.FullName;
			if (!(fullName == "PaintDotNet.Controls.Direct2DControl") && !(fullName == "PaintDotNet.Controls.InternalDirect2DControl") && !(fullName == "PaintDotNet.Controls.AdvancedDirect2DControl"))
			{
				MethodInfo method = type.GetMethod("OnDeviceContextCreated", BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
				if (method != null)
				{
					return method;
				}
			}
			type = type.BaseType;
		}
		return null;
	}

	internal static MethodInfo? ResolveD2DInit(Type controlType)
	{
		string key = controlType.FullName ?? "";
		lock (s_d2dInitCache)
		{
			if (s_d2dInitCache.TryGetValue(key, out MethodInfo value))
			{
				return value;
			}
			MethodInfo methodInfo = ResolveD2DInitCore(controlType);
			s_d2dInitCache[key] = methodInfo;
			return methodInfo;
		}
	}

	private static MethodInfo? ResolveD2DInitCore(Type controlType)
	{
		Type type = controlType;
		while (type != null)
		{
			string fullName = type.FullName;
			if (!(fullName == "PaintDotNet.Controls.Direct2DControl") && !(fullName == "PaintDotNet.Controls.InternalDirect2DControl") && !(fullName == "PaintDotNet.Controls.AdvancedDirect2DControl"))
			{
				MethodInfo method = type.GetMethod("OnInvalidateDeviceResources", BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
				if (method != null)
				{
					return method;
				}
			}
			type = type.BaseType;
		}
		return null;
	}

	private void CycleNextTool()
	{
		object activeDocumentWorkspaceFromActiveForm = GetActiveDocumentWorkspaceFromActiveForm();
		Console.WriteLine("[PaintDotNet] CycleNextTool dw=" + (activeDocumentWorkspaceFromActiveForm?.GetType().FullName ?? "null"));
		if (activeDocumentWorkspaceFromActiveForm == null)
		{
			return;
		}
		try
		{
			Type[] array = activeDocumentWorkspaceFromActiveForm.GetType().GetProperty("Tools", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public)?.GetValue(activeDocumentWorkspaceFromActiveForm) as Type[];
			Console.WriteLine("[PaintDotNet] CycleNextTool tools=" + (array?.Length.ToString() ?? "null"));
			if (array != null && array.Length != 0)
			{
				Type type = activeDocumentWorkspaceFromActiveForm.GetType().GetMethod("GetToolType", BindingFlags.Instance | BindingFlags.Public)?.Invoke(activeDocumentWorkspaceFromActiveForm, null) as Type;
				int num = ((type != null) ? Array.IndexOf(array, type) : (-1));
				int num2 = (num + 1) % array.Length;
				Type type2 = array[num2];
				DescribeActiveTool(activeDocumentWorkspaceFromActiveForm, "pre");
				activeDocumentWorkspaceFromActiveForm.GetType().GetMethod("SetToolFromType", BindingFlags.Instance | BindingFlags.Public)?.Invoke(activeDocumentWorkspaceFromActiveForm, new object[1] { type2 });
				Console.WriteLine("[PaintDotNet] Selected tool: " + type2.Name);
				DescribeActiveTool(activeDocumentWorkspaceFromActiveForm, "post");
			}
		}
		catch (Exception value)
		{
			Console.WriteLine($"[PaintDotNet] CycleNextTool error: {value}");
		}
	}

	private static void DescribeActiveTool(object activeDw, string tag)
	{
		try
		{
			Type type = activeDw.GetType().GetMethod("GetToolType", BindingFlags.Instance | BindingFlags.Public)?.Invoke(activeDw, null) as Type;
			object obj = null;
			Type type2 = activeDw.GetType();
			while ((object)type2 != null && obj == null)
			{
				obj = type2.GetField("activeTool", BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(activeDw);
				type2 = type2.BaseType;
			}
			string value = "?";
			if (obj != null)
			{
				Type type3 = obj.GetType();
				while ((object)type3 != null)
				{
					FieldInfo field = type3.GetField("presentationSource", BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic);
					if ((object)field != null)
					{
						value = ((field.GetValue(obj) == null) ? "null" : "set");
						break;
					}
					type3 = type3.BaseType;
				}
			}
			int hashCode = RuntimeHelpers.GetHashCode(activeDw);
			int value2 = ((obj != null) ? RuntimeHelpers.GetHashCode(obj) : 0);
			int value3 = 0;
			Type type4 = obj?.GetType();
			while ((object)type4 != null)
			{
				FieldInfo field2 = type4.GetField("docWorkspace", BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic);
				if ((object)field2 != null)
				{
					object value4 = field2.GetValue(obj);
					value3 = ((value4 == null) ? (-1) : RuntimeHelpers.GetHashCode(value4));
					break;
				}
				type4 = type4.BaseType;
			}
			string value5 = "?";
			Type type5 = activeDw.GetType();
			while ((object)type5 != null)
			{
				FieldInfo field3 = type5.GetField("documentCanvas", BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic);
				if ((object)field3 != null)
				{
					value5 = ((field3.GetValue(activeDw) == null) ? "null" : "set");
					break;
				}
				type5 = type5.BaseType;
			}
			Console.WriteLine($"[PaintDotNet] ActiveTool {tag} actual={type?.Name ?? "null"} inst={obj?.GetType().Name ?? "null"} presentationSource={value} canvas={value5} dwHash={hashCode} instHash={value2} toolDwHash={value3}");
		}
		catch (Exception ex)
		{
			Console.WriteLine("[PaintDotNet] DescribeActiveTool error: " + ex.GetType().Name);
		}
	}

	internal static (int Buttons, int Change) ToPointerButtonInts(System.Windows.Forms.MouseButtons buttons, PointerEventKind kind)
	{
		int item = ((buttons == System.Windows.Forms.MouseButtons.Right) ? 32 : 16);
		if (1 == 0)
		{
		}
		int num = kind switch
		{
			PointerEventKind.Pressed => (buttons != System.Windows.Forms.MouseButtons.Right) ? 1 : 3,
			PointerEventKind.Released => (buttons == System.Windows.Forms.MouseButtons.Right) ? 4 : 2,
			_ => 0,
		};
		if (1 == 0)
		{
		}
		int item2 = num;
		return (Buttons: item, Change: item2);
	}

	private static void EnsurePointerReflectionInitialized()
	{
		if (s_pointerReflectionInitialized)
		{
			return;
		}
		s_pointerReflectionInitialized = true;
		try
		{
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
			Assembly assembly = assemblies.FirstOrDefault((Assembly a) => a.GetName().Name == "paintdotnet") ?? Assembly.Load("paintdotnet");
			Assembly assembly2 = assemblies.FirstOrDefault((Assembly a) => a.GetName().Name == "PaintDotNet.Windows.Framework") ?? Assembly.Load("PaintDotNet.Windows.Framework");
			s_point2DoubleType = (assemblies.FirstOrDefault((Assembly a) => a.GetName().Name == "PaintDotNet.Primitives") ?? Assembly.Load("PaintDotNet.Primitives"))?.GetType("PaintDotNet.Rendering.Point2Double");
			s_point2DoubleCtor = s_point2DoubleType?.GetConstructor(new Type[2]
			{
				typeof(double),
				typeof(double)
			});
			s_pointerPointType = assembly2?.GetType("PaintDotNet.Input.PointerPoint");
			s_pointerTypeEnum = assembly2?.GetType("PaintDotNet.Input.PointerType");
			s_pointerButtonsEnum = assembly2?.GetType("PaintDotNet.Input.PointerButtons");
			s_pointerButtonChangeTypeEnum = assembly2?.GetType("PaintDotNet.Input.PointerButtonChangeType");
			s_pointerPointFlagsEnum = assembly2?.GetType("PaintDotNet.Input.PointerPointFlags");
			if (s_pointerPointType != null && s_pointerTypeEnum != null && s_point2DoubleType != null && s_pointerButtonsEnum != null && s_pointerButtonChangeTypeEnum != null && s_pointerPointFlagsEnum != null)
			{
				s_pointerPointCtor = s_pointerPointType.GetConstructor(new Type[8]
				{
					typeof(int),
					s_pointerTypeEnum,
					s_point2DoubleType,
					s_point2DoubleType,
					s_pointerButtonsEnum,
					s_pointerButtonChangeTypeEnum,
					typeof(float),
					s_pointerPointFlagsEnum
				});
			}
			Type type = assembly?.GetType("PaintDotNet.DocumentPointerEventArgs");
			if (type != null && s_pointerTypeEnum != null && s_pointerButtonsEnum != null && s_pointerPointType != null)
			{
				s_docPointerArgsGetMethod = type.GetMethod("Get", BindingFlags.Static | BindingFlags.Public, new Type[3] { s_pointerTypeEnum, s_pointerButtonsEnum, s_pointerPointType });
			}
			Type type2 = assembly?.GetType("PaintDotNet.Controls.DocumentWorkspace");
			if (type2 != null && type != null)
			{
				s_onDocPointerPressedMethod = type2.GetMethod("OnDocumentPointerPressed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, new Type[1] { type });
				s_onDocPointerMovedMethod = type2.GetMethod("OnDocumentPointerMoved", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, new Type[1] { type });
				s_onDocPointerReleasedMethod = type2.GetMethod("OnDocumentPointerReleased", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, new Type[1] { type });
			}
			Console.WriteLine($"[PaintDotNet] Pointer reflection initialized: pointCtor={s_pointerPointCtor != null}, getMethod={s_docPointerArgsGetMethod != null}, pressedMethod={s_onDocPointerPressedMethod != null}");
		}
		catch (Exception value)
		{
			Console.WriteLine($"[PaintDotNet] EnsurePointerReflectionInitialized error: {value}");
		}
	}

	private void ForwardDocumentPointer(int x, int y, System.Windows.Forms.MouseButtons buttons, PointerEventKind kind)
	{
		if (_activeForm == null || _documentPageBounds.Width <= 0 || _documentPageBounds.Height <= 0)
		{
			return;
		}
		object activeDocumentWorkspaceFromActiveForm = GetActiveDocumentWorkspaceFromActiveForm();
		if (activeDocumentWorkspaceFromActiveForm == null)
		{
			return;
		}
		int num = _documentOriginalSize.Width;
		int num2 = _documentOriginalSize.Height;
		if (num <= 0 || num2 <= 0)
		{
			num = 800;
			num2 = 600;
		}
		ZoomedBlit docZoomBlit = _docZoomBlit;
		double value = (((object)docZoomBlit != null) ? DocumentZoom.MapToDoc(x, y, docZoomBlit).DocX : ((double)(x - _documentPageBounds.X) * (double)num / (double)_documentPageBounds.Width));
		value = Math.Clamp(value, 0.0, (double)num - 1.0);
		ZoomedBlit docZoomBlit2 = _docZoomBlit;
		double value2 = (((object)docZoomBlit2 != null) ? DocumentZoom.MapToDoc(x, y, docZoomBlit2).DocY : ((double)(y - _documentPageBounds.Y) * (double)num2 / (double)_documentPageBounds.Height));
		value2 = Math.Clamp(value2, 0.0, (double)num2 - 1.0);
		try
		{
			EnsurePointerReflectionInitialized();
			if (!(s_docPointerArgsGetMethod == null) && !(s_pointerPointCtor == null) && !(s_point2DoubleCtor == null) && !(s_pointerTypeEnum == null) && !(s_pointerButtonsEnum == null) && !(s_pointerButtonChangeTypeEnum == null) && !(s_pointerPointFlagsEnum == null))
			{
				object obj = s_point2DoubleCtor.Invoke(new object[2] { value, value2 });
				object obj2 = s_point2DoubleCtor.Invoke(new object[2]
				{
					(double)x,
					(double)y
				});
				(int Buttons, int Change) tuple = ToPointerButtonInts(buttons, kind);
				int item = tuple.Buttons;
				int item2 = tuple.Change;
				object obj3 = Enum.ToObject(s_pointerTypeEnum, 4);
				object obj4 = Enum.ToObject(s_pointerButtonsEnum, item);
				object obj5 = Enum.ToObject(s_pointerButtonChangeTypeEnum, item2);
				object obj6 = Enum.ToObject(s_pointerPointFlagsEnum, 0);
				object obj7 = s_pointerPointCtor.Invoke(new object[8] { 1, obj3, obj, obj2, obj4, obj5, 0.5f, obj6 });
				object obj8 = s_docPointerArgsGetMethod.Invoke(null, new object[3] { obj3, obj4, obj7 });
				if (kind == PointerEventKind.Pressed)
				{
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(32, 5);
					defaultInterpolatedStringHandler.AppendLiteral("[Host] DocPress page=(");
					defaultInterpolatedStringHandler.AppendFormatted(x);
					defaultInterpolatedStringHandler.AppendLiteral(",");
					defaultInterpolatedStringHandler.AppendFormatted(y);
					defaultInterpolatedStringHandler.AppendLiteral(") doc=(");
					defaultInterpolatedStringHandler.AppendFormatted(value, "F1");
					defaultInterpolatedStringHandler.AppendLiteral(",");
					defaultInterpolatedStringHandler.AppendFormatted(value2, "F1");
					defaultInterpolatedStringHandler.AppendLiteral(")");
					ZoomedBlit docZoomBlit3 = _docZoomBlit;
					defaultInterpolatedStringHandler.AppendFormatted(((object)docZoomBlit3 != null) ? $" blit=[z={docZoomBlit3.Zoom:G4} src=({docZoomBlit3.SrcX},{docZoomBlit3.SrcY},{docZoomBlit3.SrcW}x{docZoomBlit3.SrcH}) dst=({docZoomBlit3.DestX},{docZoomBlit3.DestY})]" : " blit=[legacy]");
					Console.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
				}
				switch (kind)
				{
				case PointerEventKind.Pressed:
					s_onDocPointerPressedMethod?.Invoke(activeDocumentWorkspaceFromActiveForm, new object[1] { obj8 });
					break;
				case PointerEventKind.Moved:
					s_onDocPointerMovedMethod?.Invoke(activeDocumentWorkspaceFromActiveForm, new object[1] { obj8 });
					break;
				case PointerEventKind.Released:
					s_onDocPointerReleasedMethod?.Invoke(activeDocumentWorkspaceFromActiveForm, new object[1] { obj8 });
					break;
				}
			}
		}
		catch (Exception value3)
		{
			Console.WriteLine($"[PaintDotNet] ForwardDocumentPointer error: {value3}");
		}
	}
}
