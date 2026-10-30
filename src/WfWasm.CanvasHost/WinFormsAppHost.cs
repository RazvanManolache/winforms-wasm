using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
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

	private sealed class ListViewScroll
	{
		public int Top;

		public int Anchor = -2;
	}

	private sealed class ClickToken : IEquatable<ClickToken>
	{
		public string Kind { get; }

		public object? Target { get; }

		public ClickToken(string kind, object? target)
		{
			Kind = kind;
			Target = target;
		}

		public bool Equals(ClickToken? other)
		{
			return other != null && Kind == other.Kind && Target == other.Target;
		}

		public override bool Equals(object? obj)
		{
			return Equals(obj as ClickToken);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(Kind, RuntimeHelpers.GetHashCode(Target));
		}
	}

	private static readonly MethodInfo? s_onPaintMethod = typeof(System.Windows.Forms.Control).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static readonly MethodInfo? s_onPaintBackgroundMethod = typeof(System.Windows.Forms.Control).GetMethod("OnPaintBackground", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static readonly ConcurrentDictionary<Type, bool> s_selfPaints = new ConcurrentDictionary<Type, bool>();

	private static readonly ConcurrentDictionary<Type, byte> s_selfPaintPoisoned = new ConcurrentDictionary<Type, byte>();

	private static readonly ConcurrentDictionary<Type, bool> s_nonFlatPaints = new ConcurrentDictionary<Type, bool>();

	private static readonly ConcurrentDictionary<string, byte> s_selfPaintLogged = new ConcurrentDictionary<string, byte>();

	private static readonly ConcurrentDictionary<string, byte> s_firstPaintLogged = new ConcurrentDictionary<string, byte>();

	private static long s_firstChanceCount;

	private static long s_slowTicks;

	private static string? s_slowType;

	private static long s_slowDraws;

	private static long s_slowPx;

	private static int s_firstChanceHooked;

	private static EventHandler<FirstChanceExceptionEventArgs>? s_firstChanceHandler;

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

	private ClickToken? _downToken;
	private System.Windows.Forms.Control? _pressTarget;
	private bool _pairedPress;

	private int _downX;

	private int _downY;

	private long _downMs;

	private System.Windows.Forms.MouseButtons _downButton;

	private ClickToken? _lastClickToken;

	private int _lastClickX;

	private int _lastClickY;

	private long _lastClickMs;

	private System.Windows.Forms.MouseButtons _lastClickButton;

	private int _hoverX = int.MinValue;

	private int _hoverY = int.MinValue;

	private long _hoverMs;

	private bool _hoverFired;

	private System.Threading.Timer? _hoverTimer;

	private double _wheelRemainder;

	private readonly ConditionalWeakTable<System.Windows.Forms.Control, ListViewScroll> _listViewScroll = new ConditionalWeakTable<System.Windows.Forms.Control, ListViewScroll>();

	internal const int ListViewHeaderH = 24;

	internal const int ListViewRowH = 22;

	internal const int ListViewScrollW = 14;

	private bool _isDraggingTitle;

	private System.Windows.Forms.Form? _dragForm;

	private int _dragStartX;

	private int _dragStartY;

	private int _dragInitialFormX;

	private int _dragInitialFormY;

	internal Rectangle _documentPageBounds;

	private ZoomedBlit? _docZoomBlit;

	private System.Windows.Forms.Control? _thumbRefreshTarget;

	private Size _documentOriginalSize;

	private bool _isDrawingOnDocument;

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

	private static Dictionary<System.Windows.Forms.Control, Rectangle?>? s_frameDirty;

	private static bool s_frameGlobal;

	private static int s_paintLogCount;

	private static int s_layoutLogCount;

	private static long s_layoutFaultedSeen;

	private static long s_layoutLogTick;

	private static readonly Dictionary<string, (int w, int h)> s_layerSize = new Dictionary<string, (int, int)>();

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

	private static int s_viewWidth = 1200;

	private static int s_viewHeight = 950;

	private static Exception? s_lastFirstChance;

	private static bool s_globalFirstChanceHooked;

	private static bool s_erpSeeded;

	private static bool s_bookShelfFitDone;

	private static bool s_threadExHooked;

	private static int s_tileLines;

	private static bool s_tileLateDumpDone;

	private static bool s_tileResizeArmed;

	private static long s_tileRearmTicks;

	private static int s_gridTreeBudget;

	private static int s_gateBudget;

	private static readonly List<object> s_paintHooks = new List<object>();

	private int _lastPopupCount;

	private static readonly HashSet<string> s_vendorDumpDone = new HashSet<string>();

	private const int WM_KEYDOWN = 256;

	private const int WM_KEYUP = 257;

	private const int WM_CHAR = 258;

	private const int WM_SYSKEYDOWN = 260;

	private const int WM_SYSKEYUP = 261;

	private const int WM_SYSCHAR = 262;

	private static readonly Dictionary<string, MethodInfo?> s_mnemonicMethods = new Dictionary<string, MethodInfo>();

	private static readonly Dictionary<string, MethodInfo?> s_commandKeyMethods = new Dictionary<string, MethodInfo>();

	private static readonly ConcurrentDictionary<string, int> s_paintFire = new ConcurrentDictionary<string, int>();

	private static int s_paintSubs;

	private static int s_panoPaintTrace;

	internal Func<long> TickNow { get; set; } = () => Environment.TickCount64;

	internal static bool PaintDiagnosticsEnabled
	{
		get
		{
			bool isEnabled;
			return AppContext.TryGetSwitch("WfWasm.PaintDiagnostics", out isEnabled) & isEnabled;
		}
	}

	public int ViewWidth
	{
		get
		{
			return s_viewWidth;
		}
		set
		{
			s_viewWidth = value;
		}
	}

	public int ViewHeight
	{
		get
		{
			return s_viewHeight;
		}
		set
		{
			s_viewHeight = value;
		}
	}

	internal static bool TypeSelfPaints(Type type)
	{
		return s_selfPaints.GetOrAdd(type, (Type type2) =>
		{
			MethodInfo method = type2.GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if ((object)method != null && method.DeclaringType != typeof(System.Windows.Forms.Control))
			{
				return true;
			}
			MethodInfo method2 = type2.GetMethod("OnPaintBackground", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			return (object)method2 != null && method2.DeclaringType != typeof(System.Windows.Forms.Control);
		});
	}

	internal static bool ButtonPaintsItself(Type type)
	{
		Type typeFromHandle = typeof(System.Windows.Forms.Button);
		if (type == null || type == typeFromHandle || !typeFromHandle.IsAssignableFrom(type))
		{
			return false;
		}
		MethodInfo method = type.GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (method != null && method.DeclaringType != null && method.DeclaringType != typeFromHandle && typeFromHandle.IsAssignableFrom(method.DeclaringType))
		{
			return true;
		}
		MethodInfo method2 = type.GetMethod("OnPaintBackground", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		return method2 != null && method2.DeclaringType != null && method2.DeclaringType != typeFromHandle && typeFromHandle.IsAssignableFrom(method2.DeclaringType);
	}

	private static void LogFirstPaint(System.Windows.Forms.Control c)
	{
		try
		{
			string text = c.GetType().FullName ?? c.GetType().Name;
			if (s_firstPaintLogged.TryAdd(text, 1))
			{
				Console.WriteLine("[paint] first-painted " + text);
				if (text.Contains("Panorama", StringComparison.Ordinal))
				{
					DumpTileVisualState(c);
				}
				if (text.Contains("RadGridView", StringComparison.Ordinal) || text.Contains("RadDiagram", StringComparison.Ordinal))
				{
					DumpInkCensus();
				}
			}
			if (!s_tileLateDumpDone && text.Contains("Panorama", StringComparison.Ordinal) && PanoramaHasGroups(c))
			{
				s_tileLateDumpDone = true;
				DumpTileVisualState(c);
				MaybeEnsureTileRepaint(c);
				SubscribeTilePaintFire(c);
				ArmTileRedumpOnResize(c);
			}
		}
		catch
		{
		}
	}

	internal static void TakeSlow(out long ticks, out string? type, out long draws, out long px)
	{
		ticks = Interlocked.Exchange(ref s_slowTicks, 0L);
		type = Interlocked.Exchange(ref s_slowType, null);
		draws = Interlocked.Exchange(ref s_slowDraws, 0L);
		px = Interlocked.Exchange(ref s_slowPx, 0L);
	}

	internal static void HookFirstChance()
	{
		if (Interlocked.Exchange(ref s_firstChanceHooked, 1) == 0)
		{
			EventHandler<FirstChanceExceptionEventArgs> value = (s_firstChanceHandler = (object? _, FirstChanceExceptionEventArgs _) =>
			{
				Interlocked.Increment(ref s_firstChanceCount);
			});
			AppDomain.CurrentDomain.FirstChanceException += value;
		}
	}

	internal static void UnhookFirstChance()
	{
		if (Interlocked.Exchange(ref s_firstChanceHooked, 0) == 0)
		{
			return;
		}
		try
		{
			EventHandler<FirstChanceExceptionEventArgs> eventHandler = Volatile.Read(in s_firstChanceHandler);
			if (eventHandler != null)
			{
				AppDomain.CurrentDomain.FirstChanceException -= eventHandler;
			}
		}
		catch
		{
		}
	}

	internal static long TakeFirstChance()
	{
		return Interlocked.Exchange(ref s_firstChanceCount, 0L);
	}

	internal static int ListViewCapacity(int height)
	{
		return Math.Max(1, (height - 24 - 2) / 22);
	}

	internal static int ListViewClampTop(int top, int count, int capacity)
	{
		return Math.Clamp(top, 0, Math.Max(0, count - capacity));
	}

	internal int ResolveListViewTop(System.Windows.Forms.ListView lv)
	{
		int num = ListViewCapacity(lv.Height);
		int count;
		try
		{
			count = lv.Items.Count;
		}
		catch
		{
			count = 0;
		}
		ListViewScroll orCreateValue = _listViewScroll.GetOrCreateValue(lv);
		int num2 = ListViewClampTop(orCreateValue.Top, count, num);
		int num3 = -1;
		try
		{
			IEnumerator enumerator = lv.SelectedIndices.GetEnumerator();
			try
			{
				if (enumerator.MoveNext())
				{
					int num4 = (int)enumerator.Current;
					num3 = num4;
				}
			}
			finally
			{
				IDisposable disposable = enumerator as IDisposable;
				if (disposable != null)
				{
					disposable.Dispose();
				}
			}
			if (num3 < 0 && lv.FocusedItem != null)
			{
				num3 = lv.FocusedItem.Index;
			}
		}
		catch
		{
			num3 = -1;
		}
		if (num3 >= 0 && num3 != orCreateValue.Anchor)
		{
			if (num3 < num2)
			{
				num2 = num3;
			}
			if (num3 >= num2 + num)
			{
				num2 = num3 - num + 1;
			}
			orCreateValue.Anchor = num3;
		}
		orCreateValue.Top = num2;
		return num2;
	}

	internal bool ScrollListView(System.Windows.Forms.ListView lv, int lines)
	{
		int num = ListViewCapacity(lv.Height);
		int count;
		try
		{
			count = lv.Items.Count;
		}
		catch
		{
			return false;
		}
		if (count <= num || lines == 0)
		{
			return false;
		}
		ListViewScroll orCreateValue = _listViewScroll.GetOrCreateValue(lv);
		int num2 = ListViewClampTop(orCreateValue.Top + lines, count, num);
		if (num2 == orCreateValue.Top)
		{
			return false;
		}
		orCreateValue.Top = num2;
		return true;
	}

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
		try
		{
			_hoverTimer?.Dispose();
		}
		catch
		{
		}
		_hoverTimer = null;
		foreach (System.Windows.Forms.Form item in _forms.Values.ToList())
		{
			try
			{
				string text = null;
				try
				{
					text = item.GetType().FullName;
				}
				catch
				{
				}
				if (text != null && text.StartsWith("PaintDotNet.", StringComparison.Ordinal))
				{
					try
					{
						item.Visible = false;
					}
					catch
					{
					}
					try
					{
						WasmDialogHost.Untrack(item);
					}
					catch
					{
					}
				}
				else
				{
					try
					{
						item.Close();
					}
					catch
					{
					}
					try
					{
						item.Dispose();
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
		}
		_forms.Clear();
		_activeForm = null;
		SyncForkForeground(null);
	}

	public WinFormsAppHost()
	{
		InitializeAppDomainResolver();
		ResetProcessInputSingletons();
		s_viewWidth = 1200;
		s_viewHeight = 950;
	}

	private static void ResetProcessInputSingletons()
	{
		try
		{
			WasmMouseState.Reset();
		}
		catch
		{
		}
		try
		{
			WasmCaptureState.Clear();
		}
		catch
		{
		}
		try
		{
			WasmDialogHost.ResetForNewHost();
		}
		catch
		{
		}
	}

	private static void InitializeAppDomainResolver()
	{
		AppDomain.CurrentDomain.AssemblyResolve += (object? sender, ResolveEventArgs args) =>
		{
			AssemblyName an = new AssemblyName(args.Name);
			if (an.Name == "System.Windows.Forms" || an.Name == "System.Windows.Forms.Primitives")
			{
				return typeof(System.Windows.Forms.Form).Assembly;
			}
			Assembly? result = ((an.Name == "System.ComponentModel.Primitives") ? typeof(Component).Assembly : ((an.Name == "System.Drawing.Common" || an.Name == "System.Drawing" || an.Name == "System.Drawing.Primitives") ? typeof(Font).Assembly : AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault((Assembly a) => a.GetName().Name == an.Name)));
			return result;
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

	private static Exception RootCause(Exception ex)
	{
		Exception ex2 = ex;
		while (ex2.InnerException != null)
		{
			ex2 = ex2.InnerException;
		}
		return ex2;
	}

	private static string StackHead(Exception ex, int maxFrames)
	{
		try
		{
			string stackTrace = ex.StackTrace;
			if (string.IsNullOrEmpty(stackTrace))
			{
				return "";
			}
			StringBuilder stringBuilder = new StringBuilder(" STACK=");
			int num = 0;
			string[] array = stackTrace.Split('\n');
			foreach (string text in array)
			{
				if (num >= maxFrames)
				{
					break;
				}
				string text2 = text.Trim();
				if (text2.Length != 0)
				{
					if (text2.Length > 160)
					{
						text2 = text2.Substring(0, 160);
					}
					stringBuilder.Append(" <= ").Append(text2);
					num++;
				}
			}
			return stringBuilder.ToString();
		}
		catch
		{
			return " STACK=?";
		}
	}

	private static string SiteOf(Exception ex)
	{
		try
		{
			MethodBase targetSite = ex.TargetSite;
			if ((object)targetSite?.DeclaringType == null)
			{
				return "";
			}
			return targetSite.DeclaringType.FullName + "." + targetSite.Name;
		}
		catch
		{
			return "";
		}
	}

	private static string SiteChain(Exception ex)
	{
		StringBuilder stringBuilder = new StringBuilder();
		for (Exception ex2 = ex; ex2 != null; ex2 = ex2.InnerException)
		{
			if (stringBuilder.Length > 0)
			{
				stringBuilder.Append(" <- ");
			}
			stringBuilder.Append(ex2.GetType().Name);
			string text = SiteOf(ex2);
			if (text.Length > 0)
			{
				stringBuilder.Append('@').Append(text);
			}
		}
		return stringBuilder.ToString();
	}

	public static object? FindCaretElement(object? node, int depth)
	{
		try
		{
			if (node == null || depth > 64)
			{
				return null;
			}
			PropertyInfo property = node.GetType().GetProperty("Caret");
			object obj = null;
			try
			{
				obj = property?.GetValue(node);
			}
			catch
			{
			}
			if (obj != null)
			{
				return obj;
			}
			PropertyInfo property2 = node.GetType().GetProperty("Children");
			object obj3 = null;
			try
			{
				obj3 = property2?.GetValue(node);
			}
			catch
			{
			}
			if (obj3 is IEnumerable enumerable)
			{
				foreach (object item in enumerable)
				{
					object obj5 = FindCaretElement(item, depth + 1);
					if (obj5 != null)
					{
						return obj5;
					}
				}
			}
			return null;
		}
		catch
		{
			return null;
		}
	}

	public static string CaretProbeInfo(object? control, Rectangle rc)
	{
		try
		{
			if (control == null)
			{
				return "no-root-element";
			}
			PropertyInfo property = control.GetType().GetProperty("RootElement");
			if ((object)property == null)
			{
				return "no-root-element";
			}
			object obj = null;
			try
			{
				obj = property.GetValue(control);
			}
			catch
			{
			}
			if (obj == null)
			{
				return "no-root-element";
			}
			object obj3 = FindCaretElement(obj, 0);
			if (obj3 == null)
			{
				return "caret-not-found";
			}
			Type type = obj3.GetType();
			string value = "";
			string value2 = "";
			PointF pointF = default;
			float num = 0f;
			float num2 = 0f;
			Rectangle rectangle = default;
			try
			{
				value = ((string)type.GetProperty("Color")?.GetValue(obj3)) ?? "";
			}
			catch
			{
			}
			try
			{
				value2 = ((string)type.GetProperty("Visibility")?.GetValue(obj3)) ?? "";
			}
			catch
			{
			}
			try
			{
				pointF = (PointF)(type.GetProperty("Position")?.GetValue(obj3) ?? ((object)default(PointF)));
			}
			catch
			{
			}
			try
			{
				num = Convert.ToSingle(type.GetProperty("Width")?.GetValue(obj3) ?? ((object)0f));
			}
			catch
			{
			}
			try
			{
				num2 = Convert.ToSingle(type.GetProperty("Height")?.GetValue(obj3) ?? ((object)0f));
			}
			catch
			{
			}
			try
			{
				rectangle = (Rectangle)(type.GetProperty("ControlBoundingRectangle")?.GetValue(obj3) ?? ((object)default(Rectangle)));
			}
			catch
			{
			}
			Rectangle rect = new Rectangle(rectangle.X + (int)pointF.X - (int)num, rectangle.Y + (int)pointF.Y - (int)num2, (int)num, (int)num2);
			string value3 = (rc.Contains(rect) ? "ok" : "miss");
			return $"caret={type.Name} color={value} pos={pointF.X:0.0},{pointF.Y:0.0} visible={value2} geom={value3}";
		}
		catch
		{
			return "probe-error";
		}
	}

	private static System.Windows.Forms.Form? CreateAppForm(string typeName, string assemblyName)
	{
		try
		{
			Type type = Type.GetType(typeName + ", " + assemblyName);
			if ((object)type == null)
			{
				type = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault((Assembly a) => string.Equals(a.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))?.GetType(typeName);
			}
			if (type == null)
			{
				Console.WriteLine("[WinFormsAppHost] Form type not found: " + typeName + " in " + assemblyName);
				type = FindSingleStartupForm(assemblyName, out string available);
				if ((object)type == null)
				{
					BrowserErrorReporter.Report("form-create", new TypeLoadException($"Form type not found: {typeName} in {assemblyName}. Available: {available}"));
					return null;
				}
				Console.WriteLine($"[WinFormsAppHost] Form fallback engaged: {type.FullName} (requested {typeName})");
			}
			try
			{
				Console.WriteLine("[APP-LIFE] create " + type.FullName);
			}
			catch
			{
			}
			Exception firstChance = null;
			List<Exception> ctorFirsts = new List<Exception>(4);
			AppDomain.CurrentDomain.FirstChanceException += FirstChanceHandler;
			System.Windows.Forms.Form form = null;
			try
			{
				form = (System.Windows.Forms.Form)Activator.CreateInstance(type);
				try
				{
					Console.WriteLine("[APP-LIFE] constructed " + type.FullName);
				}
				catch
				{
				}
				try
				{
					foreach (Exception item in ctorFirsts)
					{
						Console.WriteLine("[CTOR-FIRST] " + type.FullName + ": " + item.GetType().Name + ": " + (item.Message ?? "").Split('\n')[0] + StackHead(item, 8));
					}
				}
				catch
				{
				}
				Exception ex = null;
				try
				{
					ex = WasmCtorGuard.TakeStashed();
				}
				catch
				{
				}
				if (ex != null)
				{
					try
					{
						Console.WriteLine("[WinFormsAppHost] ctor-guard stashed " + type.FullName + ": " + ex.GetType().Name + ": " + (ex.Message ?? "").Split('\n')[0]);
					}
					catch
					{
					}
					try
					{
						BrowserErrorReporter.Report("form-create", ex);
					}
					catch
					{
					}
					try
					{
						form?.Dispose();
					}
					catch
					{
					}
					return null;
				}
				return form;
			}
			finally
			{
				AppDomain.CurrentDomain.FirstChanceException -= FirstChanceHandler;
				if (firstChance != null)
				{
					s_lastFirstChance = firstChance;
				}
			}
			void FirstChanceHandler(object? sender, FirstChanceExceptionEventArgs args)
			{
				try
				{
					Exception exception = args.Exception;
					if (exception != null)
					{
						string text = "";
						try
						{
							text = (exception.Message ?? "").Split('\n')[0];
						}
						catch
						{
						}
						try
						{
							Console.WriteLine("[CTOR-1ST] " + exception.GetType().Name + ": " + text);
						}
						catch
						{
						}
					}
				}
				catch
				{
				}
				try
				{
					if (firstChance == null)
					{
						firstChance = args.Exception;
					}
					if (ctorFirsts.Count < 4 && args.Exception != null)
					{
						ctorFirsts.Add(args.Exception);
					}
				}
				catch
				{
				}
			}
		}
		catch (Exception ex2)
		{
			Console.WriteLine($"[WinFormsAppHost] Error creating {typeName}: {ex2.GetType().Name}: {ex2.Message.Split('\n')[0]}");
			BrowserErrorReporter.Report("form-create", ex2);
			if (s_lastFirstChance != null && s_lastFirstChance != RootCause(ex2))
			{
				Console.WriteLine($"[WinFormsAppHost] First-chance during create: {s_lastFirstChance.GetType().Name} site={SiteOf(s_lastFirstChance)} msg={(s_lastFirstChance.Message ?? "").Split('\n')[0]}");
			}
			s_lastFirstChance = null;
			try
			{
				WasmCtorGuard.TakeStashed();
			}
			catch
			{
			}
			Console.WriteLine("[WinFormsAppHost] Error creating " + typeName + ": site=" + SiteChain(ex2));
			return null;
		}
	}

	private static void EnsureErpDataInitialized()
	{
		if (s_erpSeeded)
		{
			return;
		}
		try
		{
			Assembly assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault((Assembly a) => string.Equals(a.GetName().Name, "ERP.Repository", StringComparison.OrdinalIgnoreCase)) ?? Assembly.Load("ERP.Repository");
			assembly.GetType("ERP.Repository.MainRepository")?.GetMethod("InitializeData", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, null);
			s_erpSeeded = true;
			Console.WriteLine("[WinFormsAppHost] ERP InitializeData invoked.");
		}
		catch (Exception ex)
		{
			Console.WriteLine("[WinFormsAppHost] ERP InitializeData FAILED: " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]);
		}
	}

	public static void EnsureOnLoadOnce(System.Windows.Forms.Form form)
	{
		Type typeFromHandle = typeof(System.Windows.Forms.Form);
		PropertyInfo property = typeFromHandle.GetProperty("CalledOnLoad", BindingFlags.Instance | BindingFlags.NonPublic);
		if ((bool?)property?.GetValue(form) != true)
		{
			typeFromHandle.GetMethod("OnLoad", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(form, new object[1] { EventArgs.Empty });
			property?.SetValue(form, true);
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
				s_rteScrollWatch.Ensure(form);
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
			if (form == null)
			{
				DesktopBenchmarks.Benchmark benchmark = DesktopBenchmarks.Find(scene);
				if ((object)benchmark != null)
				{
					form = DesktopBenchmarkForms.Create(benchmark);
				}
			}
			if (form == null)
			{
				SyncfusionDemos.Demo demo = SyncfusionDemos.Find(scene);
				if ((object)demo != null)
				{
					form = CreateAppForm(demo.FormType, demo.Assembly);
				}
			}
			if (form != null)
			{
				form.Visible = true;
				try
				{
					EnsureOnLoadOnce(form);
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
				else if (!ApplyMaximizedPlacement(form))
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
			try
			{
				Console.WriteLine("[diagstack] " + string.Join(" <- ", from f in new StackTrace(RootCause(ex2), fNeedFileInfo: false).GetFrames().Take(12)
					select f.GetMethod()));
			}
			catch
			{
			}
			BrowserErrorReporter.Report("form-create", ex2);
		}
		return form;
	}

	private static Type? FindSingleStartupForm(string assemblyName, out string available)
	{
		available = "";
		try
		{
			Assembly assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault((Assembly a) => string.Equals(a.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
			if ((object)assembly == null)
			{
				return null;
			}
			Type[] source;
			try
			{
				source = assembly.GetTypes();
			}
			catch (ReflectionTypeLoadException ex)
			{
				source = ex.Types.Where((Type tt) => (object)tt != null).ToArray();
			}
			Type[] array = source.Where((Type tt) => (object)tt != null && tt.IsClass && !tt.IsAbstract && typeof(System.Windows.Forms.Form).IsAssignableFrom(tt) && (object)tt.GetConstructor(Type.EmptyTypes) != null).ToArray();
			available = string.Join(",", array.Select((Type tt) => tt.FullName));
			return (array.Length == 1) ? array[0] : null;
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
		// (DICTNULL probe removed WF-R-070: leftover first-chance-noise
		// diagnostic; GetOrAdd-with-null-factory throws NRE on some runtimes
		// and its frames pollute crash attribution.)
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
				try
				{
					string text = Path.Combine(AppContext.BaseDirectory, "WindowsBase.dll");
					if (File.Exists(text))
					{
						AssemblyLoadContext.Default.LoadFromAssemblyPath(text);
						Console.WriteLine("[PaintDotNet] Full WindowsBase pre-loaded.");
					}
				}
				catch (Exception ex2)
				{
					Console.WriteLine("[PaintDotNet] WindowsBase pre-load skipped: " + ex2.GetType().Name);
				}
			}
			PreseedWpfSystemParameters();
			AppDomain.CurrentDomain.FirstChanceException += (object? s, FirstChanceExceptionEventArgs e) =>
			{
				if (!(e.Exception is MissingManifestResourceException))
				{
					try
					{
						StackFrame[]? frames = null;
						try { frames = new StackTrace(0, fNeedFileInfo: false).GetFrames(); } catch { }
						var (lines, topFrame) = FormatFirstChance(e.Exception, frames);
						foreach (string line in lines)
						{
							Console.WriteLine(line);
						}
						BrowserErrorReporter.Report("first-chance", e.Exception, topFrame);
					}
					catch
					{
					}
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
			catch (Exception ex3)
			{
				Console.WriteLine("[PaintDotNet] AnimationService init skipped: " + ex3.GetType().Name);
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

	// WF-R-NRE-0730 lane A: pure first-chance formatter, extracted from the
	// EnsurePaintDotNetInitialized probe so it is unit-testable and can
	// never throw. The format matches the historical console lines exactly
	// ([1ST-EST] stack-text estimate, [1ST-N] 400-char chunks, top frame).
	// Guards added over the inline version: null Message (previously the
	// formatter itself threw NRE here, filing handler noise and masking
	// the real report), null frames, and null frame elements.
	public static (string[] ConsoleLines, string? TopFrame) FormatFirstChance(Exception exception, StackFrame[]? frames)
	{
		var lines = new List<string>();
		string? topFrame = null;
		try
		{
			ArgumentNullException.ThrowIfNull(exception);
			string? stackTrace = null;
			try { stackTrace = exception.StackTrace; } catch { }
			lines.Add("[1ST-EST] " + (((stackTrace == null) ? "null" : stackTrace.Substring(0, Math.Min(600, stackTrace.Length))).Replace('\r', ' ').Replace('\n', ' ')));
			string value = ((frames == null) ? "" : string.Join(" <- ", frames.Take(12).Where(f => f is not null).Select(f => f.GetMethod()?.DeclaringType?.Name + "." + f.GetMethod()?.Name)));
			string message = exception.Message ?? "";
			int nl = message.IndexOf('\n');
			string firstLine = ((nl < 0) ? message : message.Substring(0, nl));
			string text = $"[1ST] {exception.GetType().Name}: {firstLine} :: {value}";
			for (int num = 0; num * 400 < text.Length; num++)
			{
				lines.Add($"[1ST-{num}] " + text.Substring(num * 400, Math.Min(400, text.Length - num * 400)));
			}
			MethodBase? methodBase = ((frames == null || frames.Length == 0) ? null : frames[0]?.GetMethod());
			topFrame = ((methodBase == null) ? null : (methodBase.DeclaringType?.Name + "." + methodBase.Name));
		}
		catch
		{
		}
		return (lines.ToArray(), topFrame);
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
				obj5 = ((methodInfo != null && methodInfo.GetParameters().Length == 3) ? methodInfo.Invoke(null, new object[3] { num, num2, "Background" }) : methodInfo?.Invoke(null, new object[2] { num, num2 }));
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
			FieldInfo[] array = fields;
			foreach (FieldInfo fieldInfo in array)
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

	private static bool IsBookShelfForm(System.Windows.Forms.Form? form)
	{
		try
		{
			return form != null && form.GetType().FullName?.Contains("BookShelf", StringComparison.Ordinal) == true;
		}
		catch
		{
			return false;
		}
	}

	private bool ApplyMaximizedPlacement(System.Windows.Forms.Form form)
	{
		try
		{
			if (form.IsDisposed)
			{
				return false;
			}
			if (form.WindowState != System.Windows.Forms.FormWindowState.Maximized)
			{
				return false;
			}
			form.WindowState = System.Windows.Forms.FormWindowState.Normal;
			form.Location = new Point(0, 0);
			form.Width = ViewWidth;
			form.Height = Math.Max(100, ViewHeight - ChromeHeight(form));
			form.WindowState = System.Windows.Forms.FormWindowState.Maximized;
			form.PerformLayout();
			Console.WriteLine($"[Host] maximized {form.GetType().Name} to {form.Width}x{form.Height} (view {ViewWidth}x{ViewHeight})");
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static void CenterInViewport(System.Windows.Forms.Form form)
	{
		if (!IsBookShelfForm(form) || s_bookShelfFitDone)
		{
			int num = Math.Max(200, s_viewWidth - 20);
			int num2 = Math.Max(150, s_viewHeight - 40 - ChromeHeight(form));
			bool flag = false;
			if (form.Width > num)
			{
				form.Width = num;
				flag = true;
			}
			if (form.Height > num2)
			{
				form.Height = num2;
				flag = true;
			}
			if (flag)
			{
				form.PerformLayout();
			}
		}
		int num3 = Math.Max(form.Width, form.ClientSize.Width);
		int num4 = Math.Max(form.Height, form.ClientSize.Height);
		int num5 = Math.Max(300, (num3 > 0) ? num3 : 600);
		int num6 = Math.Max(200, (num4 > 0) ? num4 : 450);
		int x = Math.Max(10, (s_viewWidth - num5) / 2);
		int y = Math.Max(10, (s_viewHeight - num6) / 2);
		form.Location = new Point(x, y);
	}

	public void OnViewportChanged()
	{
		WasmRepaintGate.MarkGlobalDirty();
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
			else if (!form2.IsDisposed && form2.WindowState == System.Windows.Forms.FormWindowState.Maximized)
			{
				ApplyMaximizedPlacement(form2);
			}
			else if (_autoCentered.Contains(form2) && !_userPlaced.Contains(form2))
			{
				CenterInViewport(form2);
			}
		}
	}

	internal static void LogThreadExceptionArgs(object? args)
	{
		try
		{
			Exception ex = null;
			try
			{
				ex = ((dynamic)args).Exception as Exception;
			}
			catch
			{
			}
			Console.WriteLine("[THREAD-EX] " + (ex?.GetType().FullName ?? "?") + ": " + (ex?.Message ?? "").Split('\n')[0]);
		}
		catch
		{
		}
	}

	public bool HasScene(AppScene scene)
	{
		try
		{
			System.Windows.Forms.Form value;
			return _forms.TryGetValue(scene, out value) && value != null && !value.IsDisposed;
		}
		catch
		{
			return false;
		}
	}

	public void SelectScene(AppScene scene)
	{
		Interlocked.Increment(ref WasmFrameCounters.SceneEpoch);
		WasmRepaintGate.MarkGlobalDirty();
		try
		{
			CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
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
		_wheelRemainder = 0.0;
		if (!s_threadExHooked)
		{
			s_threadExHooked = true;
			System.Windows.Forms.Application.ThreadException += (_, args) =>
			{
				LogThreadExceptionArgs(args);
			};
		}
		if (!s_globalFirstChanceHooked)
		{
			s_globalFirstChanceHooked = true;
			AppDomain.CurrentDomain.FirstChanceException += (object? sender, FirstChanceExceptionEventArgs args) =>
			{
				try
				{
					Exception exception = args.Exception;
					string text = (exception.Message ?? "").Split('\n')[0];
					if (!text.Contains("UITypeEditor") && !text.Contains("System.Diagnostics.Process") && FirstChanceLogGate.ShouldLog(exception.GetType().FullName + "|" + text))
					{
						Console.WriteLine("[1ST-GLOBAL] " + exception.GetType().FullName + ": " + text);
					}
				}
				catch
				{
				}
			};
		}
		_activeForm = GetOrCreateForm(scene);
		SyncForkForeground(_activeForm);
		Console.WriteLine($"[Scene] Selected {scene}, Form={_activeForm?.GetType().FullName}, Size={_activeForm?.Size}, ClientSize={_activeForm?.ClientSize}, Controls={_activeForm?.Controls.Count}");
		System.Windows.Forms.ContainerControl activeForm = _activeForm;
		if (activeForm != null)
		{
			Console.WriteLine($"[Scene] Scale: mode={activeForm.AutoScaleMode} dims={activeForm.AutoScaleDimensions} current={activeForm.CurrentAutoScaleDimensions} font={activeForm.Font?.Name} {activeForm.Font?.SizeInPoints:F2}pt");
		}
		if (_activeForm != null)
		{
			DumpControlTree(_activeForm, 0);
			DumpGridState(_activeForm);
		}
	}

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
			string text = c.GetType().FullName ?? "";
			if (text.Contains("RadGridView", StringComparison.Ordinal) || text.Contains("DataGridView", StringComparison.Ordinal))
			{
				string text2 = "?";
				try
				{
					object obj = c.GetType().GetProperty("MasterTemplate")?.GetValue(c);
					if (obj == null)
					{
						obj = c;
					}
					int num = ((int?)obj.GetType().GetProperty("RowCount")?.GetValue(obj)) ?? (-1);
					if (num < 0)
					{
						object obj2 = obj.GetType().GetProperty("Rows")?.GetValue(obj);
						num = ((int?)obj2?.GetType().GetProperty("Count")?.GetValue(obj2)) ?? (-1);
					}
					object obj3 = obj.GetType().GetProperty("Columns")?.GetValue(obj);
					int value = ((int?)obj3?.GetType().GetProperty("Count")?.GetValue(obj3)) ?? (-1);
					object obj4 = null;
					try
					{
						obj4 = obj.GetType().GetProperty("DataSource")?.GetValue(obj);
					}
					catch
					{
					}
					text2 = $"rows={num} cols={value} datasource={((obj4 == null) ? "null" : obj4.GetType().Name)}";
				}
				catch (Exception ex)
				{
					text2 = "ERR " + ex.GetType().Name;
				}
				Console.WriteLine($"[GRID-STATE] {c.GetType().Name} '{c.Name}' Bounds={c.Bounds} Visible={c.Visible} {text2}");
				try
				{
					object obj6 = c.GetType().GetProperty("MasterTemplate")?.GetValue(c);
					if (obj6 == null)
					{
						obj6 = c;
					}
					object obj7 = obj6.GetType().GetProperty("Rows")?.GetValue(obj6);
					IEnumerator enumerator = (obj7 as IEnumerable)?.GetEnumerator();
					if (enumerator != null && enumerator.MoveNext() && enumerator.Current != null)
					{
						StringBuilder stringBuilder = new StringBuilder();
						int num2 = 0;
						PropertyInfo[] properties = enumerator.Current.GetType().GetProperties();
						foreach (PropertyInfo propertyInfo in properties)
						{
							if (num2 >= 8)
							{
								break;
							}
							string text3 = propertyInfo.PropertyType.FullName ?? "";
							if (text3.StartsWith("System.Int32") || text3.StartsWith("System.Single") || text3.StartsWith("System.Boolean") || text3.StartsWith("System.Drawing.Size"))
							{
								object obj8 = null;
								try
								{
									obj8 = propertyInfo.GetValue(enumerator.Current);
								}
								catch
								{
									continue;
								}
								stringBuilder.Append(propertyInfo.Name).Append('=').Append(obj8)
									.Append(';');
								num2++;
							}
						}
						Console.WriteLine($"[GRID-ROW0] {stringBuilder}");
						StringBuilder stringBuilder2 = new StringBuilder();
						try
						{
							object obj10 = enumerator.Current.GetType().GetProperty("Cells")?.GetValue(enumerator.Current);
							int value2 = ((int?)obj10?.GetType().GetProperty("Count")?.GetValue(obj10)) ?? (-1);
							stringBuilder2.Append("cells=").Append(value2).Append(';');
							IEnumerator enumerator2 = (obj10 as IEnumerable)?.GetEnumerator();
							if (enumerator2 != null && enumerator2.MoveNext() && enumerator2.Current != null)
							{
								PropertyInfo[] properties2 = enumerator2.Current.GetType().GetProperties();
								foreach (PropertyInfo propertyInfo2 in properties2)
								{
									string name = propertyInfo2.Name;
									if (!(name != "Size") || !(name != "Location") || !(name != "Bounds") || !(name != "Visible") || !(name != "Value"))
									{
										object obj11 = null;
										try
										{
											obj11 = propertyInfo2.GetValue(enumerator2.Current);
										}
										catch
										{
											continue;
										}
										string value3;
										if (obj11 is string text4)
										{
											value3 = ((text4.Length > 24) ? text4.Substring(0, 24) : text4);
										}
										else
										{
											value3 = obj11?.ToString() ?? "null";
										}
										stringBuilder2.Append(name).Append('=').Append(value3)
											.Append(';');
									}
								}
							}
						}
						catch (Exception ex2)
						{
							stringBuilder2.Append("ERR ").Append(ex2.GetType().Name);
						}
						Console.WriteLine($"[GRID-CELL0] {stringBuilder2}");
					}
				}
				catch (Exception ex3)
				{
					Console.WriteLine("[GRID-ROW0] ERR " + ex3.GetType().Name);
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

	internal static void DumpGridVisualState(System.Windows.Forms.Control root)
	{
		try
		{
			DumpGridVisualCore(root);
		}
		catch
		{
		}
	}

	private static void DumpGridVisualCore(System.Windows.Forms.Control c)
	{
		try
		{
			string text = c.GetType().FullName ?? "";
			if (text.Contains("RadGridView", StringComparison.Ordinal))
			{
				StringBuilder stringBuilder = new StringBuilder();
				try
				{
					object element = c.GetType().GetProperty("TableElement")?.GetValue(c);
					DescribeVisual(element, "table", stringBuilder);
					object element2 = FirstVisualChild(element);
					DescribeVisual(element2, "row0", stringBuilder);
					object element3 = FirstVisualChild(element2);
					DescribeVisual(element3, "cell0", stringBuilder);
					DumpGridColumns(c);
					DumpGridTree(c);
					s_gateBudget = 0;
					DumpGridLayoutGate(c);
					ProbeGridPaint(c);
				}
				catch (Exception ex)
				{
					stringBuilder.Append("ERR ").Append(ex.GetType().Name);
				}
				Console.WriteLine($"[GRID-VIS] {c.GetType().Name} '{c.Name}' {stringBuilder}");
			}
		}
		catch
		{
		}
		foreach (System.Windows.Forms.Control control in c.Controls)
		{
			DumpGridVisualCore(control);
		}
	}

	internal static void DumpInkCensus()
	{
		try
		{
			Console.WriteLine("[INK] TEXT-CENSUS " + WfWasm.Shim.TextRenderer.DumpCensus());
		}
		catch
		{
		}
		try
		{
			string text = WasmGdiText.DumpGdiPaintCensus();
			Console.WriteLine("[INK] GDI-CENSUS " + text);
		}
		catch
		{
		}
		try
		{
			string text2 = Graphics.DumpDrawStringCensus();
			Console.WriteLine("[INK] DS-CENSUS " + text2);
		}
		catch
		{
		}
	}

	internal static void DumpTileVisualState(System.Windows.Forms.Control root)
	{
		try
		{
			s_tileLines = 0;
			DumpTileVisualCore(root);
		}
		catch
		{
		}
		try
		{
			Console.WriteLine("[TILE-VIS] TEXT-CENSUS " + WfWasm.Shim.TextRenderer.DumpCensus());
		}
		catch
		{
		}
		try
		{
			string text = WasmGdiText.DumpGdiPaintCensus();
			Console.WriteLine("[TILE-VIS] GDI-CENSUS " + text);
		}
		catch
		{
		}
		try
		{
			string text2 = Graphics.DumpDrawStringCensus();
			Console.WriteLine("[TILE-VIS] DS-CENSUS " + text2);
		}
		catch
		{
		}
	}

	internal static void ArmTileRedumpOnResize(System.Windows.Forms.Control c)
	{
		try
		{
			if (s_tileResizeArmed)
			{
				return;
			}
			s_tileResizeArmed = true;
			System.Windows.Forms.Form form = c.FindForm();
			if (form == null)
			{
				return;
			}
			form.SizeChanged += (object? _, EventArgs _) =>
			{
				try
				{
					long ticks = DateTime.UtcNow.Ticks;
					if (ticks - s_tileRearmTicks < 600000000)
					{
						return;
					}
					s_tileRearmTicks = ticks;
					s_tileLateDumpDone = false;
					s_panoPaintTrace = 0;
					s_paintSubs = 0;
					s_paintFire.Clear();
					try
					{
						Console.WriteLine("[TILE-VIS] redump-armed on resize");
					}
					catch
					{
					}
				}
				catch
				{
				}
			};
		}
		catch
		{
		}
	}

	private static void DumpTileVisualCore(System.Windows.Forms.Control c)
	{
		try
		{
			string text = c.GetType().FullName ?? "";
			if (text.Contains("Panorama", StringComparison.Ordinal))
			{
				Console.WriteLine($"[TILE-VIS] {c.GetType().Name} '{c.Name}' Bounds={c.Bounds} Visible={c.Visible}");
				object[] array = new object[4]
				{
					SafeGetObj(c, "PanoramaElement"),
					SafeGetObj(c, "RootElement"),
					SafeGetObj(c, "Groups"),
					SafeGetObj(c, "Items")
				};
				object[] array2 = array;
				foreach (object obj in array2)
				{
					if (obj != null)
					{
						DumpTileElement(obj, 1);
					}
				}
			}
		}
		catch
		{
		}
		foreach (System.Windows.Forms.Control control in c.Controls)
		{
			DumpTileVisualCore(control);
		}
	}

	private static object? SafeGetObj(object? o, string name)
	{
		try
		{
			return o?.GetType().GetProperty(name)?.GetValue(o);
		}
		catch
		{
			return null;
		}
	}

	private static bool PanoramaHasGroups(object? panoramaControl)
	{
		try
		{
			object obj = SafeGetObj(panoramaControl, "PanoramaElement");
			if (obj == null)
			{
				obj = SafeGetObj(panoramaControl, "RootElement");
			}
			object obj2 = SafeGetObj(obj, "Groups");
			if (obj2 is IEnumerable enumerable)
			{
				{
					IEnumerator enumerator = enumerable.GetEnumerator();
					try
					{
						if (enumerator.MoveNext())
						{
							object current = enumerator.Current;
							return true;
						}
					}
					finally
					{
						IDisposable disposable = enumerator as IDisposable;
						if (disposable != null)
						{
							disposable.Dispose();
						}
					}
				}
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	internal static bool MaybeEnsureTileRepaint(System.Windows.Forms.Control c)
	{
		try
		{
			if (!PanoramaHasGroups(c))
			{
				return false;
			}
			try
			{
				WasmRepaintGate.RequestRepaint(c, invalidateChildren: true);
			}
			catch
			{
			}
			try
			{
				Console.WriteLine("[TILE-VIS] ensure-paint invalidated " + c.GetType().Name);
			}
			catch
			{
			}
			try
			{
				System.Windows.Forms.Form form = c.FindForm();
				if (!s_bookShelfFitDone && IsBookShelfForm(form))
				{
					s_bookShelfFitDone = true;
					CenterInViewport(form);
					Console.WriteLine("[TILE-VIS] deferred-fit applied BookShelf size=" + form.Size);
					try
					{
						WasmRepaintGate.RequestRepaint(c, invalidateChildren: true);
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static void DumpTileList(object? list, int depth)
	{
		if (!(list is IEnumerable enumerable) || list is string)
		{
			return;
		}
		int num = 0;
		foreach (object item in enumerable)
		{
			if (item == null || num >= 12 || s_tileLines >= 300)
			{
				break;
			}
			DumpTileElement(item, depth);
			num++;
		}
	}

	private static void DumpTileElement(object? element, int depth)
	{
		if (element == null || depth > 7 || s_tileLines >= 300)
		{
			return;
		}
		if (element is IEnumerable && !(element is string))
		{
			DumpTileList(element, depth);
			return;
		}
		try
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append(' ', depth * 2);
			stringBuilder.Append('[').Append(element.GetType().Name).Append(']');
			DescribeVisual(element, "el", stringBuilder);
			stringBuilder.Append("Arrange=").Append(SafeProp(element, "IsArrangeValid")).Append(';');
			stringBuilder.Append("Measure=").Append(SafeProp(element, "IsMeasureValid")).Append(';');
			stringBuilder.Append("En=").Append(SafeProp(element, "Enabled")).Append(';');
			stringBuilder.Append("SP=").Append(SafeProp(element, "ShouldPaint")).Append(';');
			stringBuilder.Append("DT=").Append(SafeProp(element, "DrawText")).Append(';');
			stringBuilder.Append("Img=").Append((SafeGetObj(element, "Image") == null) ? "null" : "set").Append(';');
			stringBuilder.Append("PR=").Append(SafeSubProp(element, "TextParams", "paintingRectangle")).Append(';');
			stringBuilder.Append("FNT=").Append(SafeProp(element, "Font")).Append(';');
			stringBuilder.Append("TPF=").Append(SafeSubProp(element, "TextParams", "font")).Append(';');
			stringBuilder.Append("CBR=").Append(SafeProp(element, "ControlBoundingRectangle")).Append(';');
			stringBuilder.Append("TT=").Append(SafeProp(element, "TotalTransform")).Append(';');
			stringBuilder.Append("SPC=").Append(SafeProp(element, "ShouldPaintChildren")).Append(';');
			stringBuilder.Append("IMPL=").Append(SafeFieldType(element, "textPrimitiveImpl")).Append(';');
			stringBuilder.Append("LAYC=").Append(SafeProp(element, "LayoutableChildrenCount")).Append(';');
			stringBuilder.Append("ZC2=").Append(SafeZOrderedCount(element)).Append(';');
			string text = SafeProp(element, "Text");
			if (text.Length > 48)
			{
				text = text.Substring(0, 48);
			}
			stringBuilder.Append("Text='").Append(text.Replace('\r', ' ').Replace('\n', ' ')).Append("';");
			Console.WriteLine($"[TILE-VIS] {stringBuilder}");
			s_tileLines++;
			object[] array = new object[3]
			{
				SafeGetObj(element, "Children"),
				SafeGetObj(element, "Groups"),
				SafeGetObj(element, "Items")
			};
			int num = s_tileLines;
			object[] array2 = array;
			foreach (object list in array2)
			{
				DumpTileList(list, depth + 1);
			}
			int num2 = ChildCount(element);
			int num3 = s_tileLines - num;
			if (num2 > num3)
			{
				Console.WriteLine($"[TILE-VIS] {new string(' ', depth * 2)}... +{num2 - num3} more");
			}
		}
		catch
		{
		}
	}

	private static string SafeSubProp(object? o, string outer, string inner)
	{
		try
		{
			object obj = SafeGetObj(o, outer);
			if (obj == null)
			{
				return "null";
			}
			object obj2 = obj.GetType().GetProperty(inner)?.GetValue(obj);
			if (obj2 == null)
			{
				obj2 = obj.GetType().GetField(inner, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj);
			}
			return obj2?.ToString() ?? "null";
		}
		catch
		{
			return "ERR";
		}
	}

	private static string SafeFieldType(object? o, string field)
	{
		try
		{
			return (o?.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(o))?.GetType().Name ?? "null";
		}
		catch
		{
			return "ERR";
		}
	}

	private static string SafeZOrderedCount(object? o)
	{
		try
		{
			if (o == null)
			{
				return "null";
			}
			MethodInfo methodInfo = null;
			Type type = o.GetType();
			while ((object)type != null && (object)methodInfo == null)
			{
				methodInfo = type.GetMethod("GetChildren", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				type = type.BaseType;
			}
			if ((object)methodInfo == null)
			{
				return "no-m";
			}
			ParameterInfo[] parameters = methodInfo.GetParameters();
			if (parameters.Length != 1)
			{
				return "no-1p";
			}
			object obj = Enum.Parse(parameters[0].ParameterType, "ZOrdered");
			object obj2 = methodInfo.Invoke(o, new object[1] { obj });
			if (!(obj2 is IEnumerable enumerable))
			{
				return "no-en";
			}
			int num = 0;
			foreach (object item in enumerable)
			{
				num++;
				if (num > 20)
				{
					break;
				}
			}
			return num.ToString();
		}
		catch
		{
			return "ERR";
		}
	}

	private static string SafeProp(object? o, string name)
	{
		try
		{
			if (o == null)
			{
				return "?";
			}
			return (o.GetType().GetProperty(name)?.GetValue(o))?.ToString() ?? "null";
		}
		catch
		{
			return "ERR";
		}
	}

	private static int ChildCount(object? element)
	{
		try
		{
			object obj = element?.GetType().GetProperty("Children")?.GetValue(element);
			return ((int?)obj?.GetType().GetProperty("Count")?.GetValue(obj)) ?? (-1);
		}
		catch
		{
			return -2;
		}
	}

	private static void DumpGridColumns(System.Windows.Forms.Control c)
	{
		try
		{
			StringBuilder stringBuilder = new StringBuilder();
			object obj = c.GetType().GetProperty("Columns")?.GetValue(c);
			int value = ((int?)obj?.GetType().GetProperty("Count")?.GetValue(obj)) ?? (-1);
			stringBuilder.Append("cols=").Append(value).Append(';');
			IEnumerator enumerator = (obj as IEnumerable)?.GetEnumerator();
			int num = 0;
			while (enumerator != null && enumerator.MoveNext() && num < 15)
			{
				object current = enumerator.Current;
				stringBuilder.Append('[').Append(num).Append(' ')
					.Append(SafeProp(current, "Name"));
				stringBuilder.Append(" W=").Append(SafeProp(current, "Width")).Append(" Min=")
					.Append(SafeProp(current, "MinWidth"));
				stringBuilder.Append(" Max=").Append(SafeProp(current, "MaxWidth")).Append(" Vis=")
					.Append(SafeProp(current, "IsVisible"))
					.Append(']');
				num++;
			}
			object obj2 = c.GetType().GetProperty("MasterTemplate")?.GetValue(c);
			stringBuilder.Append("tmpl[AutoSize=").Append(SafeProp(obj2 ?? c, "AutoSizeColumnsMode"));
			stringBuilder.Append(" RowH=").Append(SafeProp(obj2, "RowHeight")).Append(" Grp=")
				.Append(SafeProp(obj2, "EnableGrouping"))
				.Append("];");
			Console.WriteLine($"[GRID-COLS] {c.GetType().Name} '{c.Name}' {stringBuilder}");
		}
		catch (Exception ex)
		{
			Console.WriteLine("[GRID-COLS] ERR " + ex.GetType().Name);
		}
	}

	private static void WalkGridTree(object? el, int depth, StringBuilder sb)
	{
		if (el == null || depth > 5 || s_gridTreeBudget > 120)
		{
			return;
		}
		s_gridTreeBudget++;
		try
		{
			sb.Append('[').Append(depth).Append(' ')
				.Append(el.GetType().Name);
			sb.Append(' ').Append(SafeProp(el, "Bounds")).Append(' ')
				.Append(SafeProp(el, "Visibility"));
			sb.Append(" kids=").Append(ChildCount(el)).Append(']');
			object obj = el.GetType().GetProperty("Children")?.GetValue(el);
			IEnumerator enumerator = (obj as IEnumerable)?.GetEnumerator();
			while (enumerator != null && enumerator.MoveNext())
			{
				WalkGridTree(enumerator.Current, depth + 1, sb);
			}
		}
		catch
		{
			sb.Append("[ERR]");
		}
	}

	private static void DumpGridTree(System.Windows.Forms.Control c)
	{
		try
		{
			object el = c.GetType().GetProperty("TableElement")?.GetValue(c);
			StringBuilder stringBuilder = new StringBuilder();
			s_gridTreeBudget = 0;
			WalkGridTree(el, 0, stringBuilder);
			Console.WriteLine($"[GRID-TREE] {c.GetType().Name} '{c.Name}' {stringBuilder}");
		}
		catch (Exception ex)
		{
			Console.WriteLine("[GRID-TREE] ERR " + ex.GetType().Name);
		}
	}

	private static string SafeField(object? o, string name)
	{
		try
		{
			if (o == null)
			{
				return "?";
			}
			FieldInfo fieldInfo = null;
			Type type = o.GetType();
			while ((object)type != null && (object)fieldInfo == null)
			{
				fieldInfo = type.GetField(name, BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				type = type.BaseType;
			}
			return (fieldInfo?.GetValue(o))?.ToString() ?? "null";
		}
		catch
		{
			return "ERR";
		}
	}

	private static void DumpGridLayoutGate(System.Windows.Forms.Control c)
	{
		try
		{
			StringBuilder stringBuilder = new StringBuilder();
			object el = c.GetType().GetProperty("TableElement")?.GetValue(c);
			WalkLayoutGate(el, 0, stringBuilder);
			Console.WriteLine($"[GRID-GATE] {c.GetType().Name} '{c.Name}' {stringBuilder}");
		}
		catch (Exception ex)
		{
			Console.WriteLine("[GRID-GATE] ERR " + ex.GetType().Name);
		}
	}

	private static void WalkLayoutGate(object? el, int depth, StringBuilder sb)
	{
		if (el == null || depth > 4 || s_gateBudget > 40)
		{
			return;
		}
		s_gateBudget++;
		try
		{
			sb.Append('[').Append(depth).Append(' ')
				.Append(el.GetType().Name);
			sb.Append(" state=").Append(SafeField(el, "state")).Append(" susp=")
				.Append(SafeField(el, "layoutSuspendCount"));
			sb.Append(" treeNull=").Append((SafeField(el, "elementTree") == "null") ? "Y" : "n");
			sb.Append(" measQ?=").Append((SafeField(el, "layoutManager") == "null") ? "noMgr" : "mgr").Append(']');
			object obj = el.GetType().GetProperty("Children")?.GetValue(el);
			IEnumerator enumerator = (obj as IEnumerable)?.GetEnumerator();
			int num = 0;
			while (enumerator != null && enumerator.MoveNext() && num < 6)
			{
				WalkLayoutGate(enumerator.Current, depth + 1, sb);
				num++;
			}
		}
		catch
		{
			sb.Append("[ERR]");
		}
	}

	private static void ProbeGridPaint(System.Windows.Forms.Control c)
	{
		try
		{
			object el = c.GetType().GetProperty("TableElement")?.GetValue(c);
			StringBuilder stringBuilder = new StringBuilder();
			s_gridTreeBudget = 0;
			WalkPaintProbe(el, 0, stringBuilder, c.Name ?? "?");
			Console.WriteLine($"[GRID-PAINT] {c.GetType().Name} '{c.Name}' {stringBuilder}");
		}
		catch (Exception ex)
		{
			Console.WriteLine("[GRID-PAINT] ERR " + ex.GetType().Name);
		}
	}

	private static void WalkPaintProbe(object? el, int depth, StringBuilder sb, string grid)
	{
		if (el == null || depth > 6 || s_gridTreeBudget > 140)
		{
			return;
		}
		s_gridTreeBudget++;
		try
		{
			string tn = el.GetType().Name;
			sb.Append('[').Append(depth).Append(' ')
				.Append(tn);
			sb.Append(" off=").Append(SafeField(el, "layoutOffset"));
			object obj = null;
			try
			{
				obj = el.GetType().GetProperty("Transform")?.GetValue(el);
			}
			catch
			{
			}
			sb.Append(" dx=").Append((obj == null) ? "?" : SafeProp(obj, "DX")).Append(" dy=")
				.Append((obj == null) ? "?" : SafeProp(obj, "DY"));
			sb.Append(" bbox=").Append(SafeProp(el, "BoundingRectangle"));
			sb.Append(" cbbox=").Append(SafeProp(el, "ControlBoundingRectangle"));
			sb.Append(" clip=").Append(SafeProp(el, "ClipDrawing")).Append(']');
			try
			{
				EventInfo eventInfo = el.GetType().GetEvent("ElementPainted");
				if ((object)eventInfo != null)
				{
					System.Windows.Forms.PaintEventHandler paintEventHandler = (object? s, System.Windows.Forms.PaintEventArgs e) =>
					{
						if (!PaintDiagnosticsEnabled)
						{
							return;
						}
						try
						{
							Console.WriteLine($"[GRID-PAINTED] {grid} {tn} clip={e.ClipRectangle}");
						}
						catch
						{
						}
					};
					eventInfo.AddEventHandler(el, paintEventHandler);
					s_paintHooks.Add(paintEventHandler);
				}
			}
			catch
			{
			}
			object obj4 = el.GetType().GetProperty("Children")?.GetValue(el);
			IEnumerator enumerator = (obj4 as IEnumerable)?.GetEnumerator();
			int num = ((depth >= 4) ? 2 : 99);
			int num2 = 0;
			while (enumerator != null && enumerator.MoveNext() && num2 < num)
			{
				try
				{
					object value = el.GetType().GetMethod("ShouldPaintChild", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(el, new object[1] { enumerator.Current });
					sb.Append("{spc:").Append(enumerator.Current?.GetType().Name).Append('=')
						.Append(value)
						.Append('}');
				}
				catch
				{
				}
				WalkPaintProbe(enumerator.Current, depth + 1, sb, grid);
				num2++;
			}
		}
		catch
		{
			sb.Append("[ERR]");
		}
	}

	internal static void DumpDockTabText(System.Windows.Forms.Control root)
	{
		try
		{
			StringBuilder stringBuilder = new StringBuilder();
			WalkDockTabs(root, 0, stringBuilder);
			Console.WriteLine($"[DOCK-TABS] {stringBuilder}");
		}
		catch (Exception ex)
		{
			Console.WriteLine("[DOCK-TABS] ERR " + ex.GetType().Name);
		}
	}

	private static void WalkDockTabs(object? o, int depth, StringBuilder sb)
	{
		if (o == null || depth > 6 || sb.Length > 6000)
		{
			return;
		}
		try
		{
			string name = o.GetType().Name;
			if (name == "DocumentTabStrip" || name == "TabStripPanel")
			{
				try
				{
					object obj = o.GetType().GetProperty("TabStripElement")?.GetValue(o);
					StringBuilder stringBuilder = new StringBuilder();
					stringBuilder.Append("{stripel:").Append(obj?.GetType().Name);
					object obj2 = obj?.GetType().GetProperty("Items")?.GetValue(obj);
					IEnumerator enumerator = (obj2 as IEnumerable)?.GetEnumerator();
					int num = 0;
					while (enumerator != null && enumerator.MoveNext() && num < 6)
					{
						stringBuilder.Append("[itm:").Append(SafeProp(enumerator.Current, "Text")).Append("/sel=")
							.Append(SafeProp(enumerator.Current, "IsSelected"))
							.Append("/vis=")
							.Append(SafeProp(enumerator.Current, "Visibility"))
							.Append("/b=")
							.Append(SafeProp(enumerator.Current, "Bounds"));
						stringBuilder.Append("/st:").Append(SafeProp(enumerator.Current, "DrawFill")).Append(",")
							.Append(SafeProp(enumerator.Current, "DrawText"))
							.Append(",")
							.Append(SafeProp(enumerator.Current, "DrawBorder"));
						stringBuilder.Append("/c:").Append(SafeProp(enumerator.Current, "BackColor")).Append(",")
							.Append(SafeProp(enumerator.Current, "ForeColor"))
							.Append(",")
							.Append(SafeProp(enumerator.Current, "Font"));
						stringBuilder.Append("/g:").Append(SafeProp(enumerator.Current, "BackColor2")).Append(",")
							.Append(SafeProp(enumerator.Current, "BackColor3"))
							.Append(",")
							.Append(SafeProp(enumerator.Current, "BackColor4"))
							.Append(",")
							.Append(SafeProp(enumerator.Current, "GradientStyle"))
							.Append(",")
							.Append(SafeProp(enumerator.Current, "NumberOfColors"));
						stringBuilder.Append("/th:").Append(SafeProp(enumerator.Current, "ThemeRole")).Append(",")
							.Append(SafeProp(enumerator.Current, "ThemeEffectiveType"))
							.Append(",")
							.Append(SafeProp(enumerator.Current, "ElementState"));
						stringBuilder.Append("/kids").Append(ChildCount(enumerator.Current)).Append(':');
						try
						{
							object obj3 = enumerator.Current?.GetType().GetProperty("Children")?.GetValue(enumerator.Current);
							IEnumerator enumerator2 = (obj3 as IEnumerable)?.GetEnumerator();
							int num2 = 0;
							while (enumerator2 != null && enumerator2.MoveNext() && num2 < 6)
							{
								stringBuilder.Append(enumerator2.Current?.GetType().Name).Append('(').Append(SafeProp(enumerator2.Current, "Visibility"))
									.Append(',')
									.Append(SafeProp(enumerator2.Current, "BackColor"))
									.Append(',')
									.Append(SafeProp(enumerator2.Current, "Bounds"))
									.Append(");");
								num2++;
							}
						}
						catch
						{
							stringBuilder.Append("kERR;");
						}
						try
						{
							object obj5 = enumerator.Current?.GetType().GetProperty("BackgroundShape")?.GetValue(enumerator.Current);
							stringBuilder.Append("/bg:").Append(obj5?.GetType().Name ?? "null");
							object obj6 = obj5?.GetType().GetProperty("Image")?.GetValue(obj5);
							if (obj6 is Image image)
							{
								stringBuilder.Append(',').Append(image.Width).Append('x')
									.Append(image.Height);
								try
								{
									stringBuilder.Append(',').Append((image as Bitmap)?.GetPixel(0, 0).ToString() ?? "nopx");
								}
								catch
								{
									stringBuilder.Append(",pxERR");
								}
							}
							else
							{
								stringBuilder.Append(",noimg");
							}
							stringBuilder.Append(',').Append(SafeProp(enumerator.Current, "BackgroundShapePaintMode"));
						}
						catch
						{
							stringBuilder.Append("/bg:ERR");
						}
						try
						{
							Type type = enumerator.Current?.GetType();
							Assembly assembly = null;
							while (type != null)
							{
								if (type.Assembly.GetName().Name == "Telerik.WinControls")
								{
									assembly = type.Assembly;
									break;
								}
								type = type.BaseType;
							}
							stringBuilder.Append("/theme:core=").Append((assembly == null) ? "null" : "ok");
							Type type2 = assembly?.GetType("Telerik.WinControls.ThemeResolutionService");
							stringBuilder.Append(",trs=").Append((type2 == null) ? "null" : "ok");
							object obj9 = type2?.GetMethod("GetTheme", new Type[1] { typeof(string) })?.Invoke(null, new object[1] { "TelerikMetro" });
							stringBuilder.Append(",theme=").Append((obj9 == null) ? "null" : obj9.GetType().Name);
							object obj10 = obj9?.GetType().GetMethod("FindRepository", new Type[1] { typeof(string) })?.Invoke(obj9, new object[1] { "PageViewItemSelectedImageShape" });
							stringBuilder.Append(",selrepo=").Append((obj10 == null) ? "null" : "ok");
							object obj11 = obj10?.GetType().GetMethod("FindSetting", new Type[1] { typeof(string) })?.Invoke(obj10, new object[1] { "BackgroundShape" });
							stringBuilder.Append(",set=").Append((obj11 == null) ? "null" : "ok");
							object obj12 = obj11?.GetType().GetProperty("Value")?.GetValue(obj11);
							stringBuilder.Append(",val=").Append((obj12 == null) ? "null" : obj12.GetType().Name);
							if (obj12 != null)
							{
								object obj13 = obj12.GetType().GetProperty("Image")?.GetValue(obj12);
								if (obj13 is Image image2)
								{
									stringBuilder.Append(",img=").Append(image2.Width).Append('x')
										.Append(image2.Height);
									try
									{
										stringBuilder.Append(',').Append((image2 as Bitmap)?.GetPixel(0, 0).ToString() ?? "nopx");
									}
									catch
									{
										stringBuilder.Append(",pxERR");
									}
								}
								else
								{
									stringBuilder.Append(",imgnull");
								}
							}
						}
						catch (Exception ex)
						{
							stringBuilder.Append("/theme:ERR-").Append(ex.GetType().Name);
						}
						try
						{
							Type type3 = enumerator.Current?.GetType();
							Assembly assembly2 = null;
							while (type3 != null)
							{
								if (type3.Assembly.GetName().Name == "Telerik.WinControls")
								{
									assembly2 = type3.Assembly;
									break;
								}
								type3 = type3.BaseType;
							}
							if (assembly2 != null)
							{
								stringBuilder.Append("/gt:");
								string[] array = new string[3] { "Telerik.WinControls.RadImageShape", "Telerik.WinControls.RadImageShape, Telerik.WinControls", "Telerik.WinControls.RadImageShape, Telerik.WinControls, Version=2026.3.1008.462, Culture=neutral, PublicKeyToken=5bb2a467cbec794e" };
								for (int i = 0; i < array.Length; i++)
								{
									try
									{
										stringBuilder.Append((assembly2.GetType(array[i], throwOnError: false, ignoreCase: false) == null) ? "null;" : "ok;");
									}
									catch (Exception ex2)
									{
										stringBuilder.Append("EX-").Append(ex2.GetType().Name).Append(";");
									}
								}
							}
							else
							{
								stringBuilder.Append("/gt:nocore");
							}
						}
						catch (Exception ex3)
						{
							stringBuilder.Append("/gt:ERR-").Append(ex3.GetType().Name);
						}
						stringBuilder.Append("]");
						num++;
					}
					stringBuilder.Append("/stripkids").Append(ChildCount(obj)).Append(':');
					try
					{
						object obj15 = obj?.GetType().GetProperty("Children")?.GetValue(obj);
						IEnumerator enumerator3 = (obj15 as IEnumerable)?.GetEnumerator();
						int num3 = 0;
						while (enumerator3 != null && enumerator3.MoveNext() && num3 < 10)
						{
							stringBuilder.Append(enumerator3.Current?.GetType().Name).Append('(').Append(SafeProp(enumerator3.Current, "Visibility"))
								.Append(',')
								.Append(SafeProp(enumerator3.Current, "BackColor"))
								.Append(',')
								.Append(SafeProp(enumerator3.Current, "Bounds"))
								.Append(");");
							num3++;
						}
					}
					catch
					{
						stringBuilder.Append("skERR;");
					}
					stringBuilder.Append("}");
					Console.WriteLine($"[DOCK-STRIP] {stringBuilder}");
				}
				catch (Exception ex4)
				{
					sb.Append("{stripelERR:").Append(ex4.GetType().Name).Append("}");
				}
			}
			if (o is System.Windows.Forms.Control control && (name.Contains("ToolWindow") || name.Contains("DocumentWindow") || name == "RadDock"))
			{
				sb.Append("{win:").Append(control.Name).Append(" h=")
					.Append(control.IsHandleCreated)
					.Append(" hwnd=")
					.Append(((IntPtr)control.Handle).ToInt64());
				try
				{
					PropertyInfo property = control.GetType().GetProperty("WindowText", BindingFlags.Instance | BindingFlags.NonPublic);
					sb.Append(" wtxt=").Append(property?.GetValue(control) ?? "null");
					string text = null;
					Type type4 = control.GetType();
					while ((object)type4 != null)
					{
						FieldInfo field = type4.GetField("_text", BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic);
						if ((object)field != null)
						{
							text = (string)field.GetValue(control);
							break;
						}
						type4 = type4.BaseType;
					}
					sb.Append(" _text=").Append(text ?? "null");
					try
					{
						Type typeFromHandle = typeof(System.Windows.Forms.Control);
						FieldInfo field2 = typeFromHandle.GetField("s_cacheTextCountProperty", BindingFlags.Static | BindingFlags.NonPublic);
						object obj17 = typeFromHandle.GetProperty("Properties", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(control);
						object value = ((obj17?.GetType().GetMethod("GetValueOrDefault", BindingFlags.Instance | BindingFlags.Public))?.MakeGenericMethod(typeof(int)))?.Invoke(obj17, new object[2]
						{
							field2?.GetValue(null),
							0
						});
						sb.Append(" ctc=").Append(value);
						object value2 = typeFromHandle.GetProperty("CacheTextInternal", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(control);
						sb.Append(" cti=").Append(value2);
					}
					catch (Exception ex5)
					{
						sb.Append(" ctcERR=").Append(ex5.GetType().Name);
					}
				}
				catch (Exception ex6)
				{
					sb.Append(" winERR=").Append(ex6.GetType().Name);
				}
				sb.Append('}');
			}
			if (!name.Contains("TabStrip") && !name.Contains("DockWindow") && !name.Contains("SplitContainer") && !name.Contains("TabItem") && !name.Contains("DocumentTabStrip") && !name.Contains("ToolWindow"))
			{
				switch (name)
				{
				case "RadDock":
				case "TabPanel":
				case "DockWindowCollection":
				case "CaptionElement":
					goto IL_0f51;
				}
				if (!name.Contains("Caption") && !name.Contains("PageViewStrip"))
				{
					goto IL_11e6;
				}
			}
			goto IL_0f51;
			IL_0f51:
			sb.Append('[').Append(depth).Append(' ')
				.Append(name);
			sb.Append(" txt=").Append(SafeProp(o, "Text")).Append(" cap=")
				.Append(SafeProp(o, "Caption"));
			sb.Append(" vis=").Append(SafeProp(o, "Visibility")).Append(']');
			try
			{
				PropertyInfo[] properties = o.GetType().GetProperties();
				foreach (PropertyInfo propertyInfo in properties)
				{
					string name2 = propertyInfo.Name;
					if ((!name2.Contains("Text") && !name2.Contains("Title") && !name2.Contains("Caption") && !name2.Contains("Item") && !name2.Contains("Panel") && !name2.Contains("Strip") && !name2.Contains("Window") && !name2.Contains("Associated")) || propertyInfo.GetIndexParameters().Length != 0)
					{
						continue;
					}
					object obj18 = null;
					try
					{
						obj18 = propertyInfo.GetValue(o);
					}
					catch
					{
						continue;
					}
					if (obj18 is string text2)
					{
						sb.Append(name2).Append('=').Append((text2.Length > 40) ? text2.Substring(0, 40) : text2)
							.Append(';');
					}
					else if (obj18 is IEnumerable enumerable && !(obj18 is string))
					{
						int k = 0;
						IEnumerator enumerator4 = enumerable.GetEnumerator();
						sb.Append(name2).Append(':');
						for (; k < 6; k++)
						{
							if (!enumerator4.MoveNext())
							{
								break;
							}
							sb.Append(SafeProp(enumerator4.Current, "Text")).Append('/').Append(SafeProp(enumerator4.Current, "Title"))
								.Append(',');
						}
						sb.Append(';');
					}
					else if (obj18 != null)
					{
						sb.Append(name2).Append('=').Append(obj18.GetType().Name)
							.Append(';');
					}
				}
			}
			catch
			{
			}
			goto IL_11e6;
			IL_11e6:
			if (o is System.Windows.Forms.Control control2)
			{
				foreach (System.Windows.Forms.Control control3 in control2.Controls)
				{
					WalkDockTabs(control3, depth + 1, sb);
				}
			}
			try
			{
				object obj21 = o.GetType().GetProperty("Children")?.GetValue(o);
				IEnumerator enumerator6 = (obj21 as IEnumerable)?.GetEnumerator();
				while (enumerator6 != null && enumerator6.MoveNext())
				{
					WalkDockTabs(enumerator6.Current, depth + 1, sb);
				}
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	private static object? FirstVisualChild(object? element)
	{
		try
		{
			object obj = element?.GetType().GetProperty("Children")?.GetValue(element);
			IEnumerator enumerator = (obj as IEnumerable)?.GetEnumerator();
			if (enumerator != null && enumerator.MoveNext())
			{
				return enumerator.Current;
			}
		}
		catch
		{
		}
		return null;
	}

	private static void DescribeVisual(object? element, string tag, StringBuilder sb)
	{
		if (element == null)
		{
			sb.Append(tag).Append("=null;");
			return;
		}
		sb.Append(tag).Append('[');
		try
		{
			PropertyInfo[] properties = element.GetType().GetProperties();
			foreach (PropertyInfo propertyInfo in properties)
			{
				string name = propertyInfo.Name;
				if (!(name != "Size") || !(name != "Location") || !(name != "Bounds") || !(name != "Visible") || !(name != "Visibility"))
				{
					object obj = null;
					try
					{
						obj = propertyInfo.GetValue(element);
					}
					catch
					{
						continue;
					}
					sb.Append(name).Append('=').Append(obj)
						.Append(';');
				}
			}
			object obj3 = element.GetType().GetProperty("Children")?.GetValue(element);
			int value = ((int?)obj3?.GetType().GetProperty("Count")?.GetValue(obj3)) ?? (-1);
			sb.Append("kids=").Append(value).Append(';');
		}
		catch (Exception ex)
		{
			sb.Append("ERR ").Append(ex.GetType().Name);
		}
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
		try
		{
			object obj = c.GetType().GetProperty("Colors")?.GetValue(c);
			int num = ((int?)obj?.GetType().GetProperty("Count")?.GetValue(obj)) ?? (-1);
			if (num >= 0)
			{
				Console.WriteLine($"{value}  [Colors] Count={num}");
			}
		}
		catch
		{
		}
		foreach (System.Windows.Forms.Control control in c.Controls)
		{
			DumpControlTree(control, indent + 1);
		}
	}

	internal static int ChromeHeight(System.Windows.Forms.Form form)
	{
		if (form.FormBorderStyle == System.Windows.Forms.FormBorderStyle.None)
		{
			return 0;
		}
		Type type = form.GetType();
		while ((object)type != null)
		{
			string name = type.Name;
			if (name.Contains("RadForm", StringComparison.Ordinal) || name.Contains("ShapedForm", StringComparison.Ordinal))
			{
				return 0;
			}
			if (name == "Form")
			{
				break;
			}
			type = type.BaseType;
		}
		try
		{
			int windowStyle = WasmWindowLongs.GetWindowStyle(form.Handle);
			if (windowStyle != 0 && (windowStyle & 0xC00000) != 12582912)
			{
				return 0;
			}
		}
		catch
		{
		}
		return 30;
	}

	public void Paint(ICanvas canvas)
	{
		ArgumentNullException.ThrowIfNull(canvas, "canvas");
		bool perfEnabled = WasmFrameCounters.PerfEnabled;
		long num = (perfEnabled ? Stopwatch.GetTimestamp() : 0);
		int num2 = WasmLayoutQueue.Drain();
		long num3 = (perfEnabled ? (Stopwatch.GetTimestamp() - num) : 0);
		if (num2 > 0)
		{
			int num4 = Interlocked.Increment(ref s_layoutLogCount);
			long totalFaulted = WasmLayoutQueue.TotalFaulted;
			long tickCount = Environment.TickCount64;
			if (num4 <= 3 || totalFaulted != s_layoutFaultedSeen || tickCount - s_layoutLogTick >= 5000)
			{
				s_layoutFaultedSeen = totalFaulted;
				s_layoutLogTick = tickCount;
				Console.WriteLine($"[LayoutQueue] drained={num2} posted={WasmLayoutQueue.TotalPosted} drainedTotal={WasmLayoutQueue.TotalDrained} faulted={totalFaulted} pending={WasmLayoutQueue.PendingCount}");
			}
		}
		if (AppContext.TryGetSwitch("WfWasm.RequireGpuRendering", out var isEnabled) & isEnabled)
		{
			throw new NotSupportedException("GPU rendering is required (WfWasm.RequireGpuRendering) but no GPU render target is available; the software target is rejected before painting.");
		}
		s_frameGlobal = WasmRepaintGate.DrainDirty(out Dictionary<System.Windows.Forms.Control, Rectangle?> dirty);
		PollGridScrollWatches();
		s_rteScrollWatch.Poll();
		s_frameDirty = dirty;
		if (WasmFrameCounters.PerfEnabled)
		{
			WasmFrameCounters.ClaimFrame();
			WasmFrameCounters.ResetFrame();
			if (num3 != 0)
			{
				WasmFrameCounters.AddDrainTicks(num3);
			}
			WasmFrameCounters.FrameGlobal = (s_frameGlobal ? 1 : 0);
			WasmFrameCounters.FrameDirtyCount = dirty.Count;
			WasmFrameCounters.TakePending(out var from, out var to);
			WasmFrameCounters.CsFrom = from;
			WasmFrameCounters.CsTo = to;
			WasmFrameCounters.TakeMarks();
		}
		if (!s_frameGlobal && dirty.Count == 0)
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
		int num5 = ChromeHeight(_activeForm);
		int x = _activeForm.Location.X;
		int y = _activeForm.Location.Y;
		int width = _activeForm.Width;
		int height = _activeForm.Height;
		uint value = ToRgba(_activeForm.BackColor, 4042322175u);
		int num6 = Interlocked.Increment(ref s_paintLogCount);
		if (PaintDiagnosticsEnabled && num6 == 1 && _activeForm != null)
		{
			try
			{
				StringBuilder stringBuilder = new StringBuilder();
				Type type = _activeForm.GetType();
				while ((object)type != null)
				{
					stringBuilder.Append(type.Name).Append('/');
					if (type.Name == "Form")
					{
						break;
					}
					type = type.BaseType;
				}
				Console.WriteLine($"[CHROME] type={_activeForm.GetType().FullName} chain={stringBuilder} border={_activeForm.FormBorderStyle} h={ChromeHeight(_activeForm)}");
			}
			catch
			{
			}
		}
		if (PaintDiagnosticsEnabled && (num6 <= 3 || num6 % 64 == 0))
		{
			Console.WriteLine($"[Paint] Form: X={x}, Y={y}, W={width}, H={height}, BackColor={_activeForm.BackColor.Name} (0x{value:X8}), Controls={_activeForm.Controls.Count}");
		}
		if (PaintDiagnosticsEnabled)
		{
			if (num6 == 1 && _activeForm != null)
			{
				DumpGridVisualState(_activeForm);
				DumpTileVisualState(_activeForm);
			}
			DumpDockTabText(_activeForm);
		}
		canvas.FillRectangle(x + 4, y + 4, width, height + num5, 64u);
		if (num5 > 0)
		{
			canvas.FillGradientVertical(x, y, width, num5, 7919615u, 5938943u);
			canvas.DrawTextAligned(_activeForm.Text ?? "Window", x + 10, y + num5 / 2, "bold 13px 'Segoe UI', sans-serif", uint.MaxValue, TextHAlign.Left, TextVAlign.Middle);
			int num7 = x + width - 28;
			int num8 = y + 6;
			canvas.FillRectangle(num7, num8, 18f, 18f, 3893437439u);
			canvas.DrawTextAligned("\u00d7", num7 + 9, num8 + 9, "bold 14px sans-serif", uint.MaxValue, TextHAlign.Center, TextVAlign.Middle);
		}
		value = ToRgba(_activeForm.BackColor, 4042322175u);
		int num9 = y + num5;
		canvas.FillRectangle(x, num9, width, height, value);
		RenderControls(canvas, _activeForm.Controls, x, num9);
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

	internal static bool TryBlitCleanLayer(ICanvas canvas, System.Windows.Forms.Control c, int absX, int absY, int w, int h)
	{
		if (IsFrameDirty(c))
		{
			return false;
		}
		if (!(canvas is ICanvasRenderer { SupportsBitmapLayers: not false } canvasRenderer))
		{
			return false;
		}
		string text = LayerIdFor(c);
		lock (s_layerHash)
		{
			if (!s_layerHash.ContainsKey(text))
			{
				return false;
			}
			if (s_layerSize.TryGetValue(text, out (int, int) value) && (value.Item1 != w || value.Item2 != h))
			{
				return false;
			}
		}
		lock (s_liveLayers)
		{
			s_liveLayers.Add(text);
		}
		canvasRenderer.BlitBitmap(text, absX, absY, w, h);
		if (WasmFrameCounters.PerfEnabled)
		{
			WasmFrameCounters.CountBlitClean();
		}
		return true;
	}

	internal static bool IsFrameDirty(System.Windows.Forms.Control c)
	{
		if (s_frameGlobal)
		{
			return true;
		}
		return s_frameDirty?.ContainsKey(c) ?? true;
	}

	internal static bool FrameDirtyRect(System.Windows.Forms.Control c, out Rectangle? rc)
	{
		if (s_frameGlobal || s_frameDirty == null)
		{
			rc = null;
			return true;
		}
		if (s_frameDirty.TryGetValue(c, out rc))
		{
			return true;
		}
		rc = null;
		return false;
	}

	internal static ulong HashBytes(byte[] data)
	{
		ulong num = 14695981039346656037uL;
		int i = 0;
		for (int num2 = data.Length & -4; i < num2; i += 4)
		{
			uint num3 = (uint)(data[i] | (data[i + 1] << 8) | (data[i + 2] << 16) | (data[i + 3] << 24));
			num ^= num3;
			num *= 1099511628211L;
		}
		for (; i < data.Length; i++)
		{
			num ^= data[i];
			num *= 1099511628211L;
		}
		return num;
	}

	internal static void RecordBitmap(object layerKey, ICanvas canvas, byte[] rgba, int w, int h, float x, float y, float dw, float dh, bool keepRetained = false)
	{
		if (canvas is ICanvasRenderer { SupportsBitmapLayers: not false } canvasRenderer)
		{
			string text = LayerIdFor(layerKey);
			if (!keepRetained)
			{
				lock (s_retainedPaint)
				{
					if (s_retainedPaint.TryGetValue(text, out Bitmap value))
					{
						s_retainedPaint.Remove(text);
						value.Dispose();
					}
				}
			}
			lock (s_liveLayers)
			{
				s_liveLayers.Add(text);
			}
			ulong num = HashBytes(rgba);
			if (WasmFrameCounters.PerfEnabled)
			{
				WasmFrameCounters.AddBytesHashed(rgba.Length);
			}
			bool flag;
			lock (s_layerHash)
			{
				flag = s_layerHash.TryGetValue(text, out var value2) && value2 == num;
				s_layerHash[text] = num;
				s_layerSize[text] = (w, h);
				if (WasmFrameCounters.PerfEnabled)
				{
					WasmFrameCounters.CountLayerHashHit(flag);
				}
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
				string[] array3 = array2;
				foreach (string key in array3)
				{
					s_layerHash.Remove(key);
					s_layerSize.Remove(key);
					lock (s_retainedPaint)
					{
						if (s_retainedPaint.TryGetValue(key, out Bitmap value))
						{
							s_retainedPaint.Remove(key);
							value.Dispose();
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
		WasmShownWindow[] array3 = array2;
		foreach (WasmShownWindow wasmShownWindow in array3)
		{
			System.Windows.Forms.Form form = wasmShownWindow.Form;
			if (IsEffectWindow(form) || !form.Visible || form.IsDisposed || form.Disposing)
			{
				continue;
			}
			if (wasmShownWindow.IsModal && form.DialogResult != System.Windows.Forms.DialogResult.None)
			{
				CloseModalIfSettled(form);
			}
			else
			{
				if (_forms.ContainsValue(form))
				{
					continue;
				}
				if (!_placedWindows.Contains(form))
				{
					_placedWindows.Add(form);
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
				try
				{
					if (form.Text == "Layers")
					{
						_thumbRefreshTarget = form;
					}
				}
				catch
				{
				}
				int x = form.Location.X;
				int y = form.Location.Y;
				int width = form.Width;
				int height = form.Height;
				int num8 = ChromeHeight(form);
				canvas.FillRectangle(x + 4, y + 4, width, height + num8, 96u);
				if (num8 > 0)
				{
					canvas.FillGradientVertical(x, y, width, num8, 7919615u, 5938943u);
					canvas.DrawTextAligned(form.Text ?? "Dialog", x + 10, y + num8 / 2, "bold 13px 'Segoe UI', sans-serif", uint.MaxValue, TextHAlign.Left, TextVAlign.Middle);
					int num9 = x + width - 28;
					int num10 = y + 6;
					canvas.FillRectangle(num9, num10, 18f, 18f, 3893437439u);
					canvas.DrawTextAligned("x", num9 + 9, num10 + 9, "bold 14px sans-serif", uint.MaxValue, TextHAlign.Center, TextVAlign.Middle);
				}
				uint rgba = ToRgba(form.BackColor, 4042322175u);
				canvas.FillRectangle(x, y + num8, width, height, rgba);
				RenderControls(canvas, form.Controls, x, y + num8);
			}
		}
	}

	private void RenderTrackedPopups(ICanvas canvas)
	{
		System.Windows.Forms.Control[] array = WasmPopupHost.Snapshot();
		if (array.Length != _lastPopupCount)
		{
			_lastPopupCount = array.Length;
			WasmRepaintGate.MarkGlobalDirty();
		}
		System.Windows.Forms.Control[] array2 = array;
		foreach (System.Windows.Forms.Control control in array2)
		{
			if (!control.Visible || control.IsDisposed || control.Disposing || control.Width <= 0 || control.Height <= 0)
			{
				continue;
			}
			int x = control.Location.X;
			int y = control.Location.Y;
			canvas.FillRectangle(x + 3, y + 3, control.Width, control.Height, 96u);
			if (!TryRenderSelfPainted(canvas, control, x, y, control.Width, control.Height))
			{
				if (!control.BackColor.IsEmpty && control.BackColor != Color.Transparent)
				{
					canvas.FillRectangle(x, y, control.Width, control.Height, ToRgba(control.BackColor, 4042322175u));
				}
				TryRenderD2DControl(canvas, control, x, y, control.Width, control.Height);
			}
			if (control.Controls.Count > 0)
			{
				RenderControls(canvas, control.Controls, x, y);
			}
		}
	}

	private bool TopPopupAt(int x, int y, out System.Windows.Forms.Control? popup)
	{
		popup = null;
		System.Windows.Forms.Control[] array = WasmPopupHost.Snapshot();
		for (int num = array.Length - 1; num >= 0; num--)
		{
			System.Windows.Forms.Control control = array[num];
			if (control.Visible && !control.IsDisposed && !control.Disposing && x >= control.Location.X && x < control.Location.X + control.Width && y >= control.Location.Y && y < control.Location.Y + control.Height)
			{
				popup = control;
				return true;
			}
		}
		return false;
	}

	private bool RoutePopupMouseDown(int x, int y, System.Windows.Forms.MouseButtons buttons)
	{
		if (WasmPopupHost.Snapshot().Length == 0)
		{
			return false;
		}
		if (TopPopupAt(x, y, out System.Windows.Forms.Control popup) && popup != null)
		{
			Console.WriteLine("[Host] OnMouseDown popup-hit=" + popup.GetType().Name + " '" + popup.Name + "'");
			RecordInput("down", $"{x},{y} {buttons} popup={popup.Name}");
			_mouseTarget = HitTestDeep(popup.Controls, popup.Location.X, popup.Location.Y, x, y) ?? popup;
			WasmRepaintGate.MarkDirtyChain(_mouseTarget);
			NoteFocus(_mouseTarget);
			RaiseMouseEvent(s_onMouseDownMethod, _mouseTarget, buttons, 1, x, y);
			return true;
		}
		int num = WasmPopupHost.Snapshot().Length;
		LogPopupState("miss");
		DismissPopupsForOutsideDown(x, y);
		if (WasmPopupHost.Snapshot().Length < num)
		{
			return true;
		}
		return false;
	}

	private bool RoutePopupClick(int x, int y, System.Windows.Forms.MouseButtons clickedButton, int clicks)
	{
		if (WasmPopupHost.Snapshot().Length == 0)
		{
			return false;
		}
		if (TopPopupAt(x, y, out System.Windows.Forms.Control popup) && popup != null)
		{
			System.Windows.Forms.Control control = HitTestDeep(popup.Controls, popup.Location.X, popup.Location.Y, x, y) ?? popup;
			Console.WriteLine("[Host] OnClick popup-hit=" + popup.GetType().Name + " '" + popup.Name + "'");
			if (control == popup && TryVendorElementClick(popup, x, y))
				return true;
			RecordInput("click", $"{x},{y} popup={popup.Name} target={DescribeControl(control)}");
			WasmRepaintGate.MarkDirtyChain(control);
			RaiseMouseEvent(s_onMouseClickMethod, control, clickedButton, clicks, x, y);
			return true;
		}
		int num = WasmPopupHost.Snapshot().Length;
		LogPopupState("miss");
		DismissPopupsForOutsideDown(x, y);
		if (WasmPopupHost.Snapshot().Length < num)
		{
			return true;
		}
		return false;
	}

	private static void LogPopupState(string why)
	{
		try
		{
			System.Windows.Forms.Control[] array = WasmPopupHost.Snapshot();
			string text = "[POPUP] state " + why + " n=" + array.Length;
			System.Windows.Forms.Control[] array2 = array;
			foreach (System.Windows.Forms.Control control in array2)
			{
				string text2;
				try
				{
					text2 = ((control.Parent == null) ? "null" : control.Parent.GetType().Name);
				}
				catch
				{
					text2 = "THREW";
				}
				text = text + " [" + control.GetType().Name + " par=" + text2 + " at=" + control.Location.X + "," + control.Location.Y + " sz=" + control.Width + "x" + control.Height + "]";
			}
			Console.WriteLine(text);
		}
		catch
		{
		}
	}

	private static void DismissPopupsForOutsideDown(int x, int y)
	{
		try
		{
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
			foreach (Assembly assembly in assemblies)
			{
				if (!(assembly.GetName().Name != "Telerik.WinControls.UI"))
				{
					Type type = assembly.GetType("Telerik.WinControls.UI.PopupManager");
					object obj = type?.GetProperty("Default", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
					type?.GetMethod("CallOnMouseDown", BindingFlags.Instance | BindingFlags.Public)?.Invoke(obj, new object[1]
					{
						new Point(x, y)
					});
					break;
				}
			}
		}
		catch
		{
		}
		WasmRepaintGate.MarkGlobalDirty();
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

	private static bool IsEffectWindow(System.Windows.Forms.Form form)
	{
		try
		{
			return !form.Modal && form.Owner != null && form.FormBorderStyle == System.Windows.Forms.FormBorderStyle.None && form.Controls.Count == 0;
		}
		catch
		{
			return false;
		}
	}

	private static int s_trackDumpCount;
	private bool TopTrackedWindowAt(int x, int y, out System.Windows.Forms.Form? win, out bool isModal)
	{
		win = null;
		isModal = false;
		WasmShownWindow[] array = WasmDialogHost.Snapshot();
		if (System.Threading.Interlocked.Increment(ref s_trackDumpCount) <= 10)
		{
			string ts = "[VAPI] track n=" + array.Length + " pt=" + x + "," + y;
			foreach (WasmShownWindow w in array)
			{
				try { ts += " [" + w.Form.GetType().Name + " at=" + w.Form.Location.X + "," + w.Form.Location.Y + " sz=" + w.Form.Width + "x" + w.Form.Height + " vis=" + w.Form.Visible + " modal=" + w.IsModal + "]"; } catch { }
			}
			Console.WriteLine(ts);
		}
		for (int num = array.Length - 1; num >= 0; num--)
		{
			System.Windows.Forms.Form form = array[num].Form;
			if (!IsEffectWindow(form) && !_forms.ContainsValue(form))
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
		if (ChromeHeight(dlg) == 0)
		{
			return false;
		}
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
		if (_mouseTarget is not null && TryCommitSfListItem(dlg, _mouseTarget, x, y))
			return;
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
			WasmRepaintGate.MarkDirtyChain(win);
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
		try
		{
			if (modal.Modal && modal.Visible && !modal.IsDisposed && !modal.Disposing && modal.DialogResult != System.Windows.Forms.DialogResult.None)
			{
				modal.Close();
			}
		}
		catch (Exception ex)
		{
			BrowserErrorReporter.Report("dialog-close", ex);
		}
	}

	private bool TryCommitSfListItem(System.Windows.Forms.Form win, System.Windows.Forms.Control? control, int x, int y)
	{
		// Syncfusion combo-popup commit: the popup hosts an SfListView
		// whose mouse handling does not select headless. Map the point to
		// a row via live ItemHeight and drive SelectedIndex (the same
		// event the mouse would raise). True = consumed exactly once.
		try
		{
			if (!win.GetType().Name.Contains("Popup", StringComparison.Ordinal))
				return false;
			System.Windows.Forms.Control? list = FindControlByNamePart(win.Controls, "SfListView");
			if (list is null || control != list)
				return false;
			if (list is null)
				return false;
			int lx = win.Location.X;
			int ly = win.Location.Y + ChromeHeight(win);
			for (System.Windows.Forms.Control? c = list; c is not null && c != win; c = c.Parent)
			{
				lx += c.Left;
				ly += c.Top;
			}
			double ih = 0;
			try
			{
				object? v = list.GetType().GetProperty("ItemHeight")?.GetValue(list);
				if (v is not null)
					ih = System.Convert.ToDouble(v);
			}
			catch { }
			if (ih <= 0)
				return false;
			int index = (int)System.Math.Floor((y - ly) / ih);
			int count = -1;
			try
			{
				object? ds = list.GetType().GetProperty("DataSource")?.GetValue(list);
				if (ds is System.Collections.ICollection col)
					count = col.Count;
			}
			catch { }
			Console.WriteLine("[VAPI] sflist pt=" + (x - lx) + "," + (y - ly) + " ih=" + ih + " idx=" + index + " count=" + count);
			if (index < 0 || (count >= 0 && index >= count))
				return false;
			System.Reflection.PropertyInfo? sel = list.GetType().GetProperty("SelectedIndex");
			if (sel is null || !sel.CanWrite || sel.PropertyType != typeof(int))
				return false;
			sel.SetValue(list, index);
			try
			{
				System.Windows.Forms.Control? pop = FindControlByNamePart(win.Controls, "ComboBoxPopup");
				object? combo = null;
				try { combo = pop?.GetType().GetProperty("ComboBox", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(pop); } catch { }
				object? item = null;
				try
				{
					object? ds = list.GetType().GetProperty("DataSource")?.GetValue(list);
					if (ds is System.Collections.IList il && index >= 0 && index < il.Count)
						item = il[index];
				}
				catch { }
				Console.WriteLine("[VAPI] sfcombo owner=" + (combo?.GetType().Name ?? "?") + " item=" + (item?.ToString() ?? "?"));
				if (combo is not null && item is not null)
				{
					System.Reflection.PropertyInfo? csi = combo.GetType().GetProperty("SelectedItem");
					if (csi is not null && csi.CanWrite)
						csi.SetValue(combo, item);
					foreach (System.Reflection.MethodInfo hm in combo.GetType().GetMethods(
						System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
					{
						if (hm.Name == "HideDropDown" && !hm.ContainsGenericParameters && hm.GetParameters().Length == 0)
						{
							hm.Invoke(combo, null);
							Console.WriteLine("[VAPI] sfcombo-hide");
							break;
						}
					}
				}
			}
			catch (Exception ex2) { Console.WriteLine("[VAPI] sfcombo-throw " + ex2.GetType().Name); }
			RecordInput("vendor", $"sflist-select {list.GetType().Name} {win.Name} {x},{y} idx={index}");
			Console.WriteLine("[VAPI] sflist-selected idx=" + index);
			DumpPageState();
			try { WasmRepaintGate.MarkGlobalDirty(); } catch { }
			try { win.Invalidate(); } catch { }
			return true;
		}
		catch (Exception ex) { Console.WriteLine("[VAPI] sflist-throw " + ex.GetType().Name); return false; }
	}
	private static System.Windows.Forms.Control? FindControlByNamePart(System.Windows.Forms.Control.ControlCollection cs, string part)
	{
		foreach (System.Windows.Forms.Control c in cs)
		{
			try
			{
				if (c.GetType().Name.Contains(part, StringComparison.Ordinal))
					return c;
			}
			catch { }
			System.Windows.Forms.Control? r = FindControlByNamePart(c.Controls, part);
			if (r is not null)
				return r;
		}
		return null;
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
				if (modal.Modal && modal.DialogResult == System.Windows.Forms.DialogResult.None)
				{
					modal.DialogResult = System.Windows.Forms.DialogResult.Cancel;
				}
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
			try
			{
				System.Windows.Forms.Form activeForm = _activeForm;
				Console.WriteLine("[Host] NULLHIT form=" + ((activeForm == null) ? "null" : (activeForm.GetType().Name + " loc=" + activeForm.Location.ToString() + " cli=" + activeForm.ClientSize)) + " n=" + (activeForm?.Controls.Count ?? (-1)));
			}
			catch
			{
			}
		}
		if (control == null)
		{
			RecordInput("click", $"{x},{y} {tag}-swallow dlg={modal.Name}");
			return;
		}
		if (TryCommitSfListItem(modal, control, x, y))
			return;
		if (control is System.Windows.Forms.Button button)
		{
			RecordInput("click", $"{x},{y} {tag}-button={button.Name}");
			try
			{
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
		if (control is System.Windows.Forms.IButtonControl buttonControl)
		{
			RecordInput("click", $"{x},{y} {tag}-pushbutton={control.Name}");
			try
			{
				NoteFocus(control);
				buttonControl.PerformClick();
				RaiseMouseEvent(s_onMouseClickMethod, control, clickedButton, clicks, x, y);
				CloseModalIfSettled(modal);
				return;
			}
			catch (Exception ex3)
			{
				Console.WriteLine($"[WinFormsAppHost] Modal pushbutton click error: {ex3.GetType().Name}: {ex3.Message.Split('\n')[0]} site={SiteOf(ex3)}");
				BrowserErrorReporter.Report("button-click", ex3);
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
			// Real click semantics are down-then-click: custom-painted dock
			// controls (PDN LayersStrip checkboxes) hit-test on mouse-down, so
			// an OnClick-raise alone is a no-op for them. Raise down first.
			RaiseMouseEvent(s_onMouseDownMethod, control, clickedButton, clicks, x, y);
			s_onClickMethod?.Invoke(control, new object[1] { EventArgs.Empty });
			RaiseMouseEvent(s_onMouseClickMethod, control, clickedButton, clicks, x, y);
		}
		catch (Exception ex4)
		{
			Console.WriteLine("[WinFormsAppHost] Modal control click error: " + ex4.GetType().Name + ": " + ex4.Message.Split('\n')[0]);
			BrowserErrorReporter.Report("control-click", ex4);
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
			if (control is System.Windows.Forms.Button button && !ButtonPaintsItself(control.GetType()))
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
					float num3 = Math.Max(1f, textBox.Font.GetHeight());
					string[] array = text.Split(new string[2] { "\r\n", "\n" }, StringSplitOptions.None);
					for (int j = 0; j < array.Length; j++)
					{
						canvas.DrawTextAligned(array[j], num + 5, (float)(num2 + 2) + (float)j * num3, fontCss2, rgba5, TextHAlign.Left, TextVAlign.Top);
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
				int num4 = 24;
				canvas.FillRectangle(num, num2, width, num4, 4008636159u);
				canvas.DrawLine(num, num2 + num4, num + width, num2 + num4, 1f, 3435973887u);
				int num5 = num + 6;
				for (int k = 0; k < listView.Columns.Count; k++)
				{
					System.Windows.Forms.ColumnHeader columnHeader = listView.Columns[k];
					canvas.DrawTextAligned(columnHeader.Text, num5, (float)num2 + (float)num4 / 2f, "bold 12px sans-serif", 858993663u, TextHAlign.Left, TextVAlign.Middle);
					num5 += columnHeader.Width;
				}
				int num6 = num2 + num4 + 2;
				int num7 = 22;
				int num8 = ListViewCapacity(listView.Height);
				int num9 = ResolveListViewTop(listView);
				int count = listView.Items.Count;
				bool flag2 = count > num8;
				int num10 = (flag2 ? (width - 14) : width);
				for (int l = num9; l < count && l - num9 < num8; l++)
				{
					int num11 = num6 + (l - num9) * num7;
					System.Windows.Forms.ListViewItem listViewItem = listView.Items[l];
					if (listViewItem.Selected)
					{
						canvas.FillRectangle(num + 1, num11, num10 - 2, num7, 865730559u);
						canvas.DrawTextAligned(listViewItem.Text, num + 6, (float)num11 + (float)num7 / 2f, "12px sans-serif", uint.MaxValue, TextHAlign.Left, TextVAlign.Middle);
					}
					else
					{
						canvas.DrawTextAligned(listViewItem.Text, num + 6, (float)num11 + (float)num7 / 2f, "12px sans-serif", 286331391u, TextHAlign.Left, TextVAlign.Middle);
					}
				}
				if (flag2)
				{
					int num12 = num + width - 14;
					int num13 = num2 + num4;
					int num14 = height - num4;
					canvas.FillRectangle(num12, num13, 14f, num14, 3840206079u);
					canvas.DrawLine(num12, num13, num12, num13 + num14, 1f, 3183328767u);
					int num15 = Math.Max(20, num14 * num8 / count);
					int num16 = num13 + ((count - num8 > 0) ? ((num14 - num15) * num9 / (count - num8)) : 0);
					canvas.FillRectangle(num12 + 2, num16, 10f, num15, 2661195519u);
				}
			}
			else if (control is System.Windows.Forms.ToolStrip toolStrip)
			{
				uint rgba7 = ToRgba(toolStrip.BackColor, 4126603263u);
				canvas.FillRectangle(num, num2, width, height, rgba7);
				canvas.DrawLine(num, num2 + height, num + width, num2 + height, 1f, 3755991039u);
				for (int m = 0; m < toolStrip.Items.Count; m++)
				{
					System.Windows.Forms.ToolStripItem toolStripItem = toolStrip.Items[m];
					if (!toolStripItem.Visible || toolStripItem.Width <= 0 || toolStripItem.Height <= 0 || toolStripItem.IsOnOverflow || toolStripItem.Bounds.Y >= toolStrip.Height || toolStripItem.Bounds.X >= toolStrip.Width)
					{
						continue;
					}
					int num17 = num + toolStripItem.Bounds.X;
					int num18 = num2 + toolStripItem.Bounds.Y;
					int width3 = toolStripItem.Width;
					int height2 = toolStripItem.Height;
					if (toolStripItem is System.Windows.Forms.ToolStripSeparator)
					{
						canvas.DrawLine(num17 + width3 / 2, num18 + 3, num17 + width3 / 2, num18 + height2 - 3, 1f, 3503345919u);
						continue;
					}
					bool flag3 = toolStripItem is System.Windows.Forms.ToolStripButton toolStripButton && toolStripButton.Checked;
					if ((toolStripItem.Selected || toolStripItem.Pressed) | flag3)
					{
						uint rgba8 = ((toolStripItem.Pressed | flag3) ? 3151557375u : 3437557759u);
						canvas.FillRoundedRectangle(num17, num18, width3, height2, 2f, rgba8);
						canvas.DrawLine(num17, num18, num17 + width3, num18, 1f, 7919615u);
						canvas.DrawLine(num17, num18 + height2, num17 + width3, num18 + height2, 1f, 7919615u);
						canvas.DrawLine(num17, num18, num17, num18 + height2, 1f, 7919615u);
						canvas.DrawLine(num17 + width3, num18, num17 + width3, num18 + height2, 1f, 7919615u);
					}
					int num19 = width3;
					if (toolStripItem is System.Windows.Forms.ToolStripSplitButton || toolStripItem is System.Windows.Forms.ToolStripDropDownButton)
					{
						num19 = Math.Max(16, width3 - 12);
						canvas.DrawLine(num17 + num19, num18 + 3, num17 + num19, num18 + height2 - 3, 1f, 3503345919u);
						canvas.DrawTextAligned("\u25be", num17 + width3 - 6, (float)num18 + (float)height2 / 2f, "9px sans-serif", 1145324799u, TextHAlign.Center, TextVAlign.Middle);
					}
					System.Windows.Forms.ToolStripItemDisplayStyle displayStyle = toolStripItem.DisplayStyle;
					bool flag4 = toolStripItem.Image is Bitmap { Width: >0 } bitmap && bitmap.Height > 0;
					string text2 = CleanItemText(toolStripItem.Text);
					bool flag5 = !string.IsNullOrEmpty(text2);
					switch (displayStyle)
					{
					case System.Windows.Forms.ToolStripItemDisplayStyle.Image:
					{
						if (flag4)
						{
							PaintCenteredBitmap(canvas, (Bitmap)toolStripItem.Image, num17, num18, num19, height2, toolStrip);
							break;
						}
						string itemGlyph = GetItemGlyph(toolStripItem.Name, m);
						canvas.DrawTextAligned(itemGlyph, (float)num17 + (float)num19 / 2f, (float)num18 + (float)height2 / 2f, "11px sans-serif", 1145324799u, TextHAlign.Center, TextVAlign.Middle);
						break;
					}
					case System.Windows.Forms.ToolStripItemDisplayStyle.Text:
						if (flag5)
						{
							uint rgba12 = ToRgba(toolStripItem.ForeColor, 572662527u);
							canvas.DrawTextAligned(text2, (float)num17 + (float)num19 / 2f, (float)num18 + (float)height2 / 2f, "12px 'Segoe UI', sans-serif", rgba12, TextHAlign.Center, TextVAlign.Middle);
						}
						break;
					case System.Windows.Forms.ToolStripItemDisplayStyle.ImageAndText:
						if ((flag4 & flag5) && num19 >= ((Bitmap)toolStripItem.Image).Width + 24)
						{
							Bitmap bitmap2 = (Bitmap)toolStripItem.Image;
							PaintCenteredBitmap(canvas, bitmap2, num17 + 2, num18, bitmap2.Width, height2, toolStrip);
							uint rgba10 = ToRgba(toolStripItem.ForeColor, 572662527u);
							canvas.DrawTextAligned(text2, num17 + bitmap2.Width + 4, (float)num18 + (float)height2 / 2f, "12px 'Segoe UI', sans-serif", rgba10, TextHAlign.Left, TextVAlign.Middle);
						}
						else if (flag4)
						{
							PaintCenteredBitmap(canvas, (Bitmap)toolStripItem.Image, num17, num18, num19, height2, toolStrip);
						}
						else if (flag5)
						{
							uint rgba11 = ToRgba(toolStripItem.ForeColor, 572662527u);
							canvas.DrawTextAligned(text2, (float)num17 + (float)num19 / 2f, (float)num18 + (float)height2 / 2f, "12px 'Segoe UI', sans-serif", rgba11, TextHAlign.Center, TextVAlign.Middle);
						}
						break;
					default:
						if (flag4)
						{
							PaintCenteredBitmap(canvas, (Bitmap)toolStripItem.Image, num17, num18, num19, height2, toolStrip);
						}
						else if (flag5)
						{
							uint rgba9 = ToRgba(toolStripItem.ForeColor, 572662527u);
							canvas.DrawTextAligned(text2, (float)num17 + (float)num19 / 2f, (float)num18 + (float)height2 / 2f, "12px 'Segoe UI', sans-serif", rgba9, TextHAlign.Center, TextVAlign.Middle);
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
				object activeDocumentSurface = GetActiveDocumentSurface(activeDocumentWorkspace, out var docW, out var docH);
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
						byte[] array2 = null;
						if (_isDrawingOnDocument || GetDocumentLayerCount(activeDocumentWorkspace) > 1)
						{
							array2 = ExtractCompositedRgba(activeDocumentWorkspace, docW, docH);
						}
						int blankKind = 0;
						array2 = array2 ?? ExtractSurfaceRgba(activeDocumentSurface, docW, docH, out blankKind);
						if ((array2 == null || array2.Length == 0) && blankKind > 0 && !_isDrawingOnDocument)
						{
							bool flag6 = false;
							try
							{
								flag6 = CpuPaint.NoteDocBlank(docW, docH, blankKind == 1);
							}
							catch
							{
							}
							if (flag6)
							{
								try
								{
									System.Windows.Forms.Control thumbRefreshTarget = _thumbRefreshTarget;
									if (thumbRefreshTarget != null && !thumbRefreshTarget.IsDisposed && !thumbRefreshTarget.Disposing)
									{
										// Subtree: the Layers strip self-paints (PDN GdiControl
										// OnPaint override) and reuses its retained layer while
										// clean, so the form alone is not enough to refresh rows.
										thumbRefreshTarget.Invalidate(true);
									}
									else
									{
										_thumbRefreshTarget = null;
									}
								}
								catch
								{
								}
							}
						}
						if (array2 != null && array2.Length != 0)
						{
							bool flag7 = false;
							try
							{
								flag7 = CpuPaint.NoteDocComposite(array2, docW, docH);
							}
							catch
							{
							}
							if (flag7)
							{
								try
								{
									System.Windows.Forms.Control thumbRefreshTarget2 = _thumbRefreshTarget;
									if (thumbRefreshTarget2 != null && !thumbRefreshTarget2.IsDisposed && !thumbRefreshTarget2.Disposing)
									{
										// Subtree: see above (strip rows reuse retained layers).
										thumbRefreshTarget2.Invalidate(true);
									}
									else
									{
										_thumbRefreshTarget = null;
									}
								}
								catch
								{
								}
							}
							if (zoomedBlit.SrcX != 0 || zoomedBlit.SrcY != 0 || zoomedBlit.SrcW != docW || zoomedBlit.SrcH != docH)
							{
								array2 = DocumentZoom.CropRgba(array2, docW, docH, zoomedBlit.SrcX, zoomedBlit.SrcY, zoomedBlit.SrcW, zoomedBlit.SrcH);
							}
							RecordBitmap(s_docLayerKey, canvas, array2, zoomedBlit.SrcW, zoomedBlit.SrcH, destX, destY, destW, destH);
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
				int num20 = num2 + height / 2;
				int num21 = Math.Max(20, width - 45);
				canvas.DrawLine(num, num20, num + num21, num20, 2f, 2576980479u);
				canvas.DrawEllipse(num + num21 / 2 - 4, num20 - 4, 8f, 8f, 1f, 7919615u, 7919615u);
				canvas.DrawTextAligned("100%", num + num21 + 6, num20, "11px 'Segoe UI', sans-serif", 572662527u, TextHAlign.Left, TextVAlign.Middle);
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
				bool flag8 = !IsD2DControl(control) && TryRenderSelfPainted(canvas, control, num, num2, width, height);
				if (!flag8 && !control.BackColor.IsEmpty && control.BackColor != Color.Transparent)
				{
					canvas.FillRectangle(num, num2, width, height, ToRgba(control.BackColor, 4042322175u));
				}
				if (!flag8 && !IsD2DControl(control) && !string.IsNullOrEmpty(control.Text) && control.Controls.Count == 0)
				{
					canvas.DrawTextAligned(control.Text, num + 4, num2 + 12, GetFontCss(control.Font), ToRgba(control.ForeColor, 255u), TextHAlign.Left, TextVAlign.Middle);
				}
				if (!flag8)
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
			{
				System.Windows.Forms.NativeWindow.WasmNotifyPaint(control.Handle);
			}
			if (WasmOverlayBridge.TryGetOverlay(control.Handle, control.ClientSize, out Bitmap bitmap) && bitmap != null)
			{
				byte[] array = ExtractBitmapRgba(bitmap);
				if (array != null)
				{
					canvas.DrawImage(array, bitmap.Width, bitmap.Height, absX, absY, bitmap.Width, bitmap.Height);
				}
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
		_mouseButtons |= buttons;
		WasmMouseState.SetModifiers(ToKeys(shift, ctrl, alt));
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
				WasmRepaintGate.MarkDirtyChain(_mouseTarget);
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
				WasmRepaintGate.MarkGlobalDirty();
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
		if (DownTrackedDock(x, y, buttons))
		{
			return;
		}
		Console.WriteLine("[Host] OnMouseDown hit=" + ((control == null) ? "null" : (control.GetType().Name + " '" + control.Name + "'")));
		if (control == null)
		{
			try
			{
				System.Windows.Forms.Form activeForm = _activeForm;
				Console.WriteLine("[Host] NULLHIT form=" + ((activeForm == null) ? "null" : (activeForm.GetType().Name + " loc=" + activeForm.Location.ToString() + " cli=" + activeForm.ClientSize)) + " n=" + (activeForm?.Controls.Count ?? (-1)));
			}
			catch
			{
			}
		}
		Console.WriteLine($"[Host] form loc={_activeForm.Location} size={_activeForm.Width}x{_activeForm.Height} kids={_activeForm.Controls.Count} chrome={ChromeHeight(_activeForm)} view={ViewWidth}x{ViewHeight}");
		Console.WriteLine("[Host] kids=" + DescribeKids(_activeForm.Controls, 0));
		if (control is System.Windows.Forms.Button pressedButton)
		{
			_pressedButton = pressedButton;
		}
		NoteFocus(control);
		if (control != null)
		{
			try
			{
				control.Focus();
			}
			catch
			{
			}
		}
		_mouseTarget = control;
		if (_mouseTarget != null)
		{
			MarkCursorNeighborhood(_mouseTarget, x, y, 128);
		}
		RecordInput("down", $"{x},{y} {buttons} target={DescribeControl(control)}");
		if (control != null && !(control is System.Windows.Forms.ToolStrip))
		{
			Console.WriteLine("[Host] Invoking RaiseMouseEvent OnMouseDown on " + control.Name);
			RaiseMouseEvent(s_onMouseDownMethod, control, buttons, 1, x, y);
			Console.WriteLine("[Host] RaiseMouseEvent OnMouseDown done");
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
			WasmRepaintGate.MarkGlobalDirty();
			_isDrawingOnDocument = false;
			Console.WriteLine("[Host] PostStroke surface=" + DescribeActiveDocSurface());
		}
		_isDraggingTitle = false;
		_pressedButton = null;
		_mouseButtons &= ~buttons;
		WasmMouseState.SetModifiers(ToKeys(shift, ctrl, alt));
		if (_mouseTarget != null)
		{
			WasmRepaintGate.MarkDirtyChain(_mouseTarget);
			ResetButtonPress(_mouseTarget);
			if (!SkipButtonMouseUp(_mouseTarget))
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
		WasmMouseState.SetModifiers(ToKeys(shift, ctrl, alt));
		NoteMoveForHover(x, y);
		if (_isDrawingOnDocument)
		{
			FeedInput(x, y, _mouseButtons, ToKeys(shift, ctrl, alt));
			ForwardDocumentPointer(x, y, _mouseButtons, PointerEventKind.Moved);
			WasmRepaintGate.MarkGlobalDirty();
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
				WasmRepaintGate.MarkDirtyChain(form);
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

	private void MarkCursorNeighborhood(System.Windows.Forms.Control target, int x, int y, int half = 48)
	{
		if (_activeForm == null || (long)target.Width * (long)target.Height <= 65536)
		{
			WasmRepaintGate.MarkDirtyChain(target);
			return;
		}
		int controlAbsX = GetControlAbsX(target, _activeForm.Location.X);
		int controlAbsY = GetControlAbsY(target, _activeForm.Location.Y + ChromeHeight(_activeForm));
		Rectangle a = new Rectangle(x - controlAbsX - half, y - controlAbsY - half, half * 2, half * 2);
		a = Rectangle.Intersect(a, new Rectangle(0, 0, target.Width, target.Height));
		if (a.Width <= 0 || a.Height <= 0)
		{
			WasmRepaintGate.MarkDirtyChain(target);
		}
		else
		{
			WasmRepaintGate.MarkDirtyChainRect(target, a);
		}
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
		if (TopPopupAt(x, y, out System.Windows.Forms.Control popup) && popup != null)
		{
			System.Windows.Forms.Control control = HitTestDeep(popup.Controls, popup.Location.X, popup.Location.Y, x, y) ?? popup;
			SetHoverControl(control);
			RaiseMouseEvent(s_onMouseMoveMethod, control, _mouseButtons, 0, x, y);
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
			System.Windows.Forms.Control control2 = HitTestDeep(win.Controls, win.Location.X, win.Location.Y + ChromeHeight(win), x, y);
			if (control2 is System.Windows.Forms.ToolStrip toolStrip)
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
				SetHoverControl(control2);
				if (control2 != null)
				{
					RaiseMouseEvent(s_onMouseMoveMethod, control2, _mouseButtons, 0, x, y);
				}
			}
			return;
		}
		if (TopModalWindow(out System.Windows.Forms.Form modal) && modal != null)
		{
			System.Windows.Forms.Control control3 = HitTestDeep(modal.Controls, modal.Location.X, modal.Location.Y + ChromeHeight(modal), x, y);
			if (control3 is System.Windows.Forms.ToolStrip toolStrip2)
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
				SetHoverControl(control3);
				if (control3 != null)
				{
					RaiseMouseEvent(s_onMouseMoveMethod, control3, _mouseButtons, 0, x, y);
				}
			}
			return;
		}
		if (_documentPageBounds.Width > 0 && _documentPageBounds.Contains(x, y))
		{
			SetHoverControl(null);
			return;
		}
		System.Windows.Forms.Control control4 = HitTestDeep(_activeForm.Controls, x2, num, x, y);
		if (control4 is System.Windows.Forms.ToolStrip toolStrip3)
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
			SetHoverControl(control4);
			if (control4 != null)
			{
				RaiseMouseEvent(s_onMouseMoveMethod, control4, _mouseButtons, 0, x, y);
			}
		}
	}

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
			for (System.Windows.Forms.Control control2 = control; control2 != null; control2 = control2.Parent)
			{
				if (control2 is System.Windows.Forms.Form form)
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

	private static void SyncForkForeground(System.Windows.Forms.Form? form)
	{
		try
		{
			if (form == null || form.IsDisposed)
			{
				WasmForeground.Clear();
				return;
			}
			try
			{
				WasmForeground.Set(form.Handle);
			}
			catch
			{
				WasmForeground.Clear();
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
			WasmRepaintGate.MarkDirtyChain(next);
			WasmRepaintGate.MarkDirtyChain(hoverControl);
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
		WasmRepaintGate.MarkDirtyChain(next?.Owner);
		WasmRepaintGate.MarkDirtyChain(hoverItem?.Owner);
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
		if (method == null || _activeForm == null || target.IsDisposed)
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
			try
			{
				Console.WriteLine("[diagstack] " + string.Join(" <- ", from f in new StackTrace(RootCause(ex), fNeedFileInfo: false).GetFrames().Take(12)
					select f.GetMethod()));
			}
			catch
			{
			}
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
		if (openDropdowns.Count == 0 || openDropdowns.Any(((System.Windows.Forms.ToolStripDropDown Drop, int Ox, int Oy) d) => d.Drop == mi.DropDown))
		{
			return;
		}
		foreach (var (toolStripDropDown, _, _) in openDropdowns.ToList())
		{
			try
			{
				toolStripDropDown.Visible = false;
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
		foreach (var (toolStripDropDown, _, _) in GetOpenDropdowns().ToList())
		{
			if (toolStripDropDown != parentDrop && IsOwnedBy(toolStripDropDown, parentDrop) && (!(item is System.Windows.Forms.ToolStripMenuItem { HasDropDownItems: not false } toolStripMenuItem) || toolStripDropDown != toolStripMenuItem.DropDown))
			{
				try
				{
					toolStripDropDown.Visible = false;
				}
				catch
				{
				}
			}
		}
		System.Windows.Forms.ToolStripMenuItem mi = item as System.Windows.Forms.ToolStripMenuItem;
		if (mi != null && mi.HasDropDownItems && !GetOpenDropdowns().Any(((System.Windows.Forms.ToolStripDropDown Drop, int Ox, int Oy) d) => d.Drop == mi.DropDown))
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
		bool flag = method == s_onMouseDownMethod;
		bool flag2 = method == s_onMouseUpMethod;
		if (flag && !(target is System.Windows.Forms.ToolStrip))
		{
			try
			{
				target.Capture = true;
			}
			catch
			{
			}
		}
		try
		{
			int x2 = _activeForm.Location.X;
			int formClientY = _activeForm.Location.Y + ChromeHeight(_activeForm);
			System.Windows.Forms.Form form = target as System.Windows.Forms.Form;
			System.Windows.Forms.Control parent = target.Parent;
			while (parent != null && form == null)
			{
				if (parent is System.Windows.Forms.Form form2)
				{
					form = form2;
				}
				parent = parent.Parent;
			}
			if (form != null && form.Parent == null)
			{
				x2 = form.Location.X;
				formClientY = form.Location.Y + ChromeHeight(form);
			}
			int controlAbsX = GetControlAbsX(target, x2);
			int controlAbsY = GetControlAbsY(target, formClientY);
			System.Windows.Forms.MouseEventArgs e = new System.Windows.Forms.MouseEventArgs(buttons, clicks, x - controlAbsX, y - controlAbsY, delta);
			method.Invoke(target, new object[1] { e });
		}
		catch (Exception ex)
		{
			Console.WriteLine("[WinFormsAppHost] Mouse event error: " + ex.GetType().Name + ": " + (ex.Message ?? "").Split('\n')[0] + " site=" + SiteOf(ex));
			Console.WriteLine("[WinFormsAppHost] Mouse event error: root=" + RootCause(ex).GetType().Name + " site=" + SiteOf(RootCause(ex)) + " msg=" + (RootCause(ex).Message ?? "").Split('\n')[0]);
			try
			{
				Console.WriteLine("[diagstack] " + string.Join(" <- ", from f in new StackTrace(RootCause(ex), fNeedFileInfo: false).GetFrames().Take(12)
					select f.GetMethod()));
			}
			catch
			{
			}
		}
		if (!flag2)
		{
			return;
		}
		try
		{
			if (target.Capture)
			{
				target.Capture = false;
			}
		}
		catch
		{
		}
	}

	internal static System.Windows.Forms.MouseButtons MapDomButton(long button)
	{
		if (1 == 0)
		{
		}
		long num = button - 1;
		if ((ulong)num > 3uL)
		{
			goto IL_0048;
		}
		switch ((int)num)
		{
		case 0:
			break;
		case 1:
			goto IL_0030;
		case 2:
			goto IL_0038;
		case 3:
			goto IL_0040;
		default:
			goto IL_0048;
		}
		System.Windows.Forms.MouseButtons result = System.Windows.Forms.MouseButtons.Middle;
		goto IL_0050;
		IL_0040:
		result = System.Windows.Forms.MouseButtons.XButton2;
		goto IL_0050;
		IL_0048:
		result = System.Windows.Forms.MouseButtons.Left;
		goto IL_0050;
		IL_0050:
		if (1 == 0)
		{
		}
		return result;
		IL_0030:
		result = System.Windows.Forms.MouseButtons.Right;
		goto IL_0050;
		IL_0038:
		result = System.Windows.Forms.MouseButtons.XButton1;
		goto IL_0050;
	}

	private static System.Windows.Forms.Keys ToKeys(bool shift, bool ctrl, bool alt)
	{
		return (System.Windows.Forms.Keys)((shift ? 65536 : 0) | (ctrl ? 131072 : 0) | (alt ? 262144 : 0));
	}

	private static void FeedInput(int x, int y, System.Windows.Forms.MouseButtons buttons, System.Windows.Forms.Keys modifiers)
	{
		WasmMouseState.Update(x, y, buttons, modifiers);
	}

	private ClickToken ResolveClickToken(int x, int y)
	{
		if (_activeForm == null)
		{
			return new ClickToken("none", null);
		}
		if (TopModalWindow(out System.Windows.Forms.Form modal) && modal != null)
		{
			return new ClickToken("modal", HitTestDeep(modal.Controls, modal.Location.X, modal.Location.Y + ChromeHeight(modal), x, y));
		}
		if (TryHitDropdown(x, y, out System.Windows.Forms.ToolStripDropDown _, out System.Windows.Forms.ToolStripItem item, out int _, out int _))
		{
			return new ClickToken("dropdown", item);
		}
		if (_documentPageBounds.Width > 0 && _documentPageBounds.Contains(x, y))
		{
			return new ClickToken("doc", null);
		}
		int x2 = _activeForm.Location.X;
		int y2 = _activeForm.Location.Y;
		int num = ChromeHeight(_activeForm);
		if (x >= x2 && x <= x2 + _activeForm.Width && y >= y2 && y < y2 + num)
		{
			return new ClickToken("title", null);
		}
		return new ClickToken("client", HitTestDeep(_activeForm.Controls, x2, y2 + num, x, y));
	}

	private void NotePress(int x, int y, System.Windows.Forms.MouseButtons buttons)
	{
		_downToken = ResolveClickToken(x, y);
		Console.WriteLine("[Host] Press tok=" + _downToken.Kind + "/" + ((_downToken.Target == null) ? "null" : _downToken.Target.GetType().Name) + " at=" + x + "," + y);
		_downX = x;
		_pressTarget = _downToken.Target as System.Windows.Forms.Control;
		_pairedPress = false;
		_downY = y;
		_downMs = TickNow();
		_downButton = buttons;
	}

	private static bool SkipButtonMouseUp(System.Windows.Forms.Control? target)
	{
		// Single-dispatch guard: the click-side PerformClick is the single
		// authority for RadButton activation. Letting the up-dispatch reach
		// the ButtonElement fires Click a second time for buttons whose
		// element geometry aligns (search toggles back to Main). Buttons
		// whose element misses (showAll) never fired on up anyway.
		try
		{
			if (target is null || !IsVendorControl(target))
				return false;
			if (!target.GetType().Name.Contains("RadButton", StringComparison.Ordinal))
				return false;
			Console.WriteLine("[VAPI] button-up-skipped " + target.Name);
			return true;
		}
		catch { return false; }
	}
	
	private static void ResetButtonPress(System.Windows.Forms.Control? target)
	{
		// Single-dispatch guard: a Release-mode RadButton whose element
		// geometry aligns would fire its Click on mouse-up AND again via
		// the click-side PerformClick (toggle buttons visibly double-fire).
		// Clearing the pressed state before up-dispatch makes the click
		// side the single authority. Buttons whose element misses (showAll)
		// never fired on up anyway.
		try
		{
			if (target is null || !IsVendorControl(target))
				return;
			if (!target.GetType().Name.Contains("RadButton", StringComparison.Ordinal))
				return;
			object? btnEl = target.GetType().GetProperty("ButtonElement")?.GetValue(target);
			System.Reflection.PropertyInfo? pressed = btnEl?.GetType().GetProperty("IsPressed");
			if (pressed is not null && pressed.CanWrite && pressed.PropertyType == typeof(bool))
			{
				pressed.SetValue(btnEl, false);
				Console.WriteLine("[VAPI] button-press-reset " + target.Name);
			}
			else
				Console.WriteLine("[VAPI] button-press-noreset " + target.Name);
		}
		catch (Exception ex) { Console.WriteLine("[VAPI] button-press-throw " + ex.GetType().Name); }
	}
	
	private static bool PressTargetContains(ClickToken? up, ClickToken? down)
	{
		// Capture semantics: a press whose own mouse-down handling
		// transiently unresolves its button (Telerik press visuals) still
		// completes when released over the button's parent. Same gesture
		// kind only; popups/modals keep their own priority upstream.
		try
		{
			if (up is null || down is null || up.Target is null || down.Target is null)
				return false;
			if (!string.Equals(up.Kind, down.Kind, StringComparison.Ordinal))
				return false;
			System.Windows.Forms.Control? u = up.Target as System.Windows.Forms.Control;
			System.Windows.Forms.Control? d = down.Target as System.Windows.Forms.Control;
			if (u is null || d is null || u == d)
				return false;
			for (System.Windows.Forms.Control? c = d.Parent; c is not null; c = c.Parent)
			{
				if (c == u)
					return true;
			}
			for (System.Windows.Forms.Control? c2 = u.Parent; c2 is not null; c2 = c2.Parent)
			{
				if (c2 == d)
					return true;
			}
		}
		catch { }
		return false;
	}
	
	private void PairClick(int x, int y, System.Windows.Forms.MouseButtons buttons)
	{
		if (_activeForm == null || _downToken == null)
		{
			return;
		}
		ClickToken clickToken = ResolveClickToken(x, y);
		Size dragSize = System.Windows.Forms.SystemInformation.DragSize;
		bool flag = clickToken.Equals(_downToken) || PressTargetContains(clickToken, _downToken);
		bool flag2 = buttons == _downButton;
		bool flag3 = Math.Abs(x - _downX) <= dragSize.Width && Math.Abs(y - _downY) <= dragSize.Height;
		_downToken = null;
		if (flag && flag2 && flag3)
		{
			Console.WriteLine("[Host] Pair up=" + x + "," + y + " b=" + buttons + " tok=" + clickToken.Kind + "/" + ((clickToken.Target == null) ? "null" : clickToken.Target.GetType().Name) + " down=" + _downX + "," + _downY + " db=" + _downButton + " f=" + flag + " b2=" + flag2 + " d=" + flag3);
			long num = TickNow();
			_pairedPress = true;
			Size doubleClickSize = System.Windows.Forms.SystemInformation.DoubleClickSize;
			if (_lastClickToken != null && clickToken.Equals(_lastClickToken) && buttons == _lastClickButton && num - _lastClickMs <= System.Windows.Forms.SystemInformation.DoubleClickTime && Math.Abs(x - _lastClickX) <= doubleClickSize.Width && Math.Abs(y - _lastClickY) <= doubleClickSize.Height)
			{
				_lastClickToken = null;
				OnDoubleClickAt(clickToken, buttons, x, y);
				return;
			}
			_lastClickToken = clickToken;
			_lastClickX = x;
			_lastClickY = y;
			_lastClickMs = num;
			_lastClickButton = buttons;
			Console.WriteLine($"[Host] OnClick(x={x}, y={y}) paired button={buttons}");
			OnClick(x, y, buttons);
		}
	}

	private void OnDoubleClickAt(ClickToken token, System.Windows.Forms.MouseButtons buttons, int x, int y)
	{
		WasmDialogHost.AdvanceTurn();
		WasmRepaintGate.MarkGlobalDirty();
		if (token.Target is System.Windows.Forms.Control control)
		{
			Console.WriteLine($"[Host] OnDoubleClick target={control.GetType().Name} button={buttons}");
			RecordInput("doubleclick", $"{x},{y} {buttons} control={DescribeControl(control)}");
			RaiseMouseEvent(s_onDoubleClickMethod, control, buttons, 2, x, y);
			RaiseMouseEvent(s_onMouseDoubleClickMethod, control, buttons, 2, x, y);
		}
		else
		{
			Console.WriteLine("[Host] OnDoubleClick chrome token=" + token.Kind + ", repeated activation");
			OnClick(x, y, buttons);
		}
	}

	internal static bool GridScrollDegenerate(int minimum, int maximum, int largeChange)
	{
		return maximum - largeChange + 1 <= minimum;
	}

	private sealed class GridScrollWatch
	{
		public GridScrollWatch(object vsb, int last) { Vsb = vsb; Last = last; }
		public object Vsb;
		public int Last;
	}

	private static readonly ConditionalWeakTable<System.Windows.Forms.Control, GridScrollWatch> s_gridScrollWatches = new();

	private static readonly RichTextScrollWatch s_rteScrollWatch = new();

	// Subscribes a grid's smooth pixel scrollbar for displacement polling:
	// the pump diffs VScrollBar.Value per frame and notes exact pixel
	// movement, whatever moved it (wheel timer, thumb drag, keyboard).
	// Smooth mode is required: only there is Value in pixels. Entries die
	// with their grid (no leak); dead reads are skipped per frame.
	private static void EnsureGridScrollWatch(System.Windows.Forms.Control? target)
	{
		if (target == null)
			return;
		System.Windows.Forms.Control? grid = target;
		while (grid != null && grid.GetType().FullName != "Telerik.WinControls.UI.RadGridView")
			grid = grid.Parent;
		if (grid == null || s_gridScrollWatches.TryGetValue(grid, out _))
			return;
		try
		{
			object? gv = grid.GetType().GetProperty("GridViewElement")?.GetValue(grid);
			object? table = gv?.GetType().GetProperty("TableElement")?.GetValue(gv);
			object? scroller = table?.GetType().GetProperty("RowScroller")?.GetValue(table);
			object? vsb = table?.GetType().GetProperty("VScrollBar")?.GetValue(table);
			if (scroller == null || vsb == null)
				return;
			if ((scroller.GetType().GetProperty("ScrollMode")?.GetValue(scroller)?.ToString()) != "Smooth")
				return;
			int v = (int)(vsb.GetType().GetProperty("Value")?.GetValue(vsb) ?? 0);
			s_gridScrollWatches.Add(grid, new GridScrollWatch(vsb, v));
		}
		catch
		{
		}
	}

	private static void PollGridScrollWatches()
	{
		foreach (var kv in s_gridScrollWatches)
		{
			try
			{
				int v = (int)(kv.Value.Vsb.GetType().GetProperty("Value")?.GetValue(kv.Value.Vsb) ?? kv.Value.Last);
				if (v != kv.Value.Last && WasmFrameCounters.PerfEnabled)
					Console.WriteLine($"[Host] WatchDiff control='{kv.Key.GetType().FullName}' {kv.Value.Last} -> {v}");
				if (v != kv.Value.Last)
				{
					WasmRepaintGate.NoteScroll(kv.Key, 0, -(v - kv.Value.Last));
					kv.Value.Last = v;
				}
			}
			catch
			{
			}
		}
	}

	// Metric repair for Telerik RadGridView grids whose headless scrollbar
	// metrics degenerate (Maximum - LargeChange + 1 <= Minimum while rows
	// overflow: the realized-only view-state total never covers unrealized
	// rows, so the vendor's own wheel path no-ops and the grid can never
	// scroll). The host recomputes the range through the vendor's own
	// public updater from row count x row height; the vendor then owns
	// scrolling and painting natively (genuine invalidation, smooth
	// timer). Healthy metrics are never touched.
	private static void TryRepairVendorGridMetrics(System.Windows.Forms.Control? target)
	{
		if (target == null)
			return;
		System.Windows.Forms.Control? grid = target;
		while (grid != null && grid.GetType().FullName != "Telerik.WinControls.UI.RadGridView")
			grid = grid.Parent;
		if (grid == null)
			return;
		try
		{
			object? gv = grid.GetType().GetProperty("GridViewElement")?.GetValue(grid);
			if ((bool?)gv?.GetType().GetProperty("IsInEditMode")?.GetValue(gv) == true)
				return;
			object? table = gv?.GetType().GetProperty("TableElement")?.GetValue(gv);
			object? scroller = table?.GetType().GetProperty("RowScroller")?.GetValue(table);
			object? vsb = table?.GetType().GetProperty("VScrollBar")?.GetValue(table);
			if (scroller == null || vsb == null)
				return;
			var vsbType = vsb.GetType();
			int min = (int)(vsbType.GetProperty("Minimum")?.GetValue(vsb) ?? 0);
			int max = (int)(vsbType.GetProperty("Maximum")?.GetValue(vsb) ?? 0);
			int large = (int)(vsbType.GetProperty("LargeChange")?.GetValue(vsb) ?? 0);
			int small = (int)(vsbType.GetProperty("SmallChange")?.GetValue(vsb) ?? 0);
			int rowH = (int)(table?.GetType().GetProperty("RowHeight")?.GetValue(table) ?? 0);
			object? rows = grid.GetType().GetProperty("Rows")?.GetValue(grid);
			int count = (int)(rows?.GetType().GetProperty("Count")?.GetValue(rows) ?? 0);
			if (!GridScrollDegenerate(min, max, large))
				return;
			int unit = Math.Max(rowH, small);
			long total = (long)count * unit;
			if (count <= 0 || unit <= 0 || total <= large || total > int.MaxValue)
				return;
			Console.WriteLine($"[Host] GridWheelRepair grid='{grid.Name}' rows={count} rowH={rowH} max {max} -> {total}");
			scroller.GetType().GetMethod("UpdateScrollRange", new[] { typeof(int), typeof(bool) })?.Invoke(scroller, new object[] { (int)total, true });
		}
		catch
		{
		}
	}

	public void OnMouseWheelRaw(int x, int y, double deltaX, double deltaY, int deltaMode, bool shift = false, bool ctrl = false, bool alt = false)
	{
		FeedInput(x, y, _mouseButtons, ToKeys(shift, ctrl, alt));
		if (_activeForm == null)
		{
			return;
		}
		double num = ((deltaMode == 1) ? (deltaY / 3.0) : (deltaY / 100.0));
		_wheelRemainder += num;
		int num2 = 0;
		while (_wheelRemainder >= 1.0)
		{
			num2++;
			_wheelRemainder--;
		}
		while (_wheelRemainder <= -1.0)
		{
			num2--;
			_wheelRemainder++;
		}
		if (num2 != 0)
		{
			int num3 = -num2 * 120;
			System.Windows.Forms.Control control = _hoverControl;
			if (control == null)
			{
				control = HitTestDeep(_activeForm.Controls, _activeForm.Location.X, _activeForm.Location.Y + ChromeHeight(_activeForm), x, y);
			}
			if (control == null)
			{
				control = _activeForm;
			}
			Console.WriteLine($"[Host] OnMouseWheel(x={x}, y={y}, delta={num3})");
			RecordInput("wheel", $"{x},{y} delta={num3}");
			System.Windows.Forms.Control control2 = HitTestDeep(_activeForm.Controls, _activeForm.Location.X, _activeForm.Location.Y + ChromeHeight(_activeForm), x, y);
			if (control2 is System.Windows.Forms.ListView lv && num2 != 0 && ScrollListView(lv, num2 * System.Windows.Forms.SystemInformation.MouseWheelScrollLines))
			{
				WasmRepaintGate.MarkGlobalDirty();
			}
			if (control2 != null)
			{
				TryRepairVendorGridMetrics(control2);
				EnsureGridScrollWatch(control2);
				s_rteScrollWatch.Ensure(control2);
			}
			RaiseMouseEvent(s_onMouseWheelMethod, control, _mouseButtons, 0, x, y, num3);
		}
	}

	private void NoteMoveForHover(int x, int y)
	{
		Size mouseHoverSize = System.Windows.Forms.SystemInformation.MouseHoverSize;
		if (Math.Abs(x - _hoverX) > mouseHoverSize.Width || Math.Abs(y - _hoverY) > mouseHoverSize.Height)
		{
			_hoverX = x;
			_hoverY = y;
			_hoverMs = TickNow();
			_hoverFired = false;
		}
		if (_hoverTimer != null)
		{
			return;
		}
		try
		{
			_hoverTimer = new System.Threading.Timer((object? state) =>
			{
				((WinFormsAppHost)state).FireHoverIfStill();
			}, this, 100, 100);
		}
		catch
		{
		}
	}

	private void FireHoverIfStill()
	{
		try
		{
			WinFormsExecution.Run(() =>
			{
				CheckHover(TickNow());
			});
		}
		catch
		{
		}
	}

	internal void CheckHover(long nowMs)
	{
		System.Windows.Forms.Control hoverControl = _hoverControl;
		if (!_hoverFired && _mouseButtons == System.Windows.Forms.MouseButtons.None && hoverControl != null && nowMs - _hoverMs >= System.Windows.Forms.SystemInformation.MouseHoverTime)
		{
			_hoverFired = true;
			Console.WriteLine("[Host] OnMouseHover control=" + DescribeControl(hoverControl));
			RaiseSimpleEvent(s_onMouseHoverMethod, hoverControl);
		}
	}

	public void OnClick(int x, int y, System.Windows.Forms.MouseButtons clickedButton = System.Windows.Forms.MouseButtons.Left, int clicks = 1)
	{
		WasmDialogHost.AdvanceTurn();
		Console.WriteLine($"[Host] OnClick(x={x}, y={y})");
		WasmRepaintGate.MarkGlobalDirty();
		if (_activeForm == null || ClickOpenDropdown(x, y) || RoutePopupClick(x, y, clickedButton, clicks))
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
			try
			{
				System.Windows.Forms.Form activeForm = _activeForm;
				Console.WriteLine("[Host] NULLHIT form=" + ((activeForm == null) ? "null" : (activeForm.GetType().Name + " loc=" + activeForm.Location.ToString() + " cli=" + activeForm.ClientSize)) + " n=" + (activeForm?.Controls.Count ?? (-1)));
			}
			catch
			{
			}
		}
		if (control != null)
		{
			bool usePress = _pairedPress;
		_pairedPress = false;
		System.Windows.Forms.Control? pressTarget = usePress ? _pressTarget : null;
		_pressTarget = null;
		if (pressTarget is not null && !pressTarget.IsDisposed && !pressTarget.Disposing && pressTarget.Enabled && pressTarget != control)
		{
			Console.WriteLine("[Host] OnClick press-target " + pressTarget.GetType().Name + " '" + pressTarget.Name + "' over " + ((control == null) ? "null" : (control.GetType().Name + " '" + control.Name + "'")));
			control = pressTarget;
		}
		DispatchControlClick(control, x, y, clickedButton, clicks, x2, num2);
		}
	}

	private static int s_pageDumpCount;

	private void DumpPageState()
	{
		try
		{
			if (System.Threading.Interlocked.Increment(ref s_pageDumpCount) > 60)
				return;
			System.Windows.Forms.Form f = _activeForm;
			if (f is null)
				return;
			foreach (System.Windows.Forms.Control c in f.Controls)
				Console.WriteLine("[VAPI] page c=" + c.GetType().Name + " '" + c.Name + "' b=" + c.Bounds.ToString() + " vis=" + c.Visible);
			System.Windows.Forms.Control? sb = FindByName(f.Controls, "showAllItemsButton");
			try
			{
				System.Windows.Forms.Control? dd = FindVendorDropDown(f.Controls);
				string dt = "?";
				if (dd is not null)
				{
					try
					{
						object? si = dd.GetType().GetProperty("SelectedItem")?.GetValue(dd);
						object? tx = si?.GetType().GetProperty("Text")?.GetValue(si);
						if (tx is not null)
							dt = tx.ToString() ?? "?";
					}
					catch { }
				}
				Console.WriteLine("[VAPI] page dropdown=" + (dd is null ? "null" : (dd.GetType().Name + " '" + dd.Name + "'")) + " sel='" + dt + "'");
			try
			{
				System.Windows.Forms.Control? dp = FindByName(f.Controls, "dropDownPosition");
				string dpt = "?";
				if (dp is not null)
				{
					try { object? v = dp.GetType().GetProperty("Text")?.GetValue(dp); if (v is not null) dpt = v.ToString() ?? "?"; } catch { }
				}
				Console.WriteLine("[VAPI] page sfcombo text='" + dpt + "'");
			}
			catch { }
			}
			catch { }
			Console.WriteLine("[VAPI] page showAll=" + (sb is null ? "null" : ("b=" + sb.Bounds.ToString() + " vis=" + sb.Visible + " en=" + sb.Enabled)));
		}
		catch { }
	}

	private static System.Windows.Forms.Control? FindVendorDropDown(System.Windows.Forms.Control.ControlCollection cs)
	{
		foreach (System.Windows.Forms.Control c in cs)
		{
			try
			{
				if (IsVendorControl(c) && c.GetType().Name.Contains("DropDown", StringComparison.Ordinal)
					&& !c.GetType().Name.Contains("ToolStrip", StringComparison.Ordinal))
					return c;
			}
			catch { }
			System.Windows.Forms.Control? r = FindVendorDropDown(c.Controls);
			if (r is not null)
				return r;
		}
		return null;
	}
	
	private static System.Windows.Forms.Control? FindByName(System.Windows.Forms.Control.ControlCollection cs, string name)
	{
		foreach (System.Windows.Forms.Control c in cs)
		{
			if (c.Name == name)
				return c;
			System.Windows.Forms.Control? r = FindByName(c.Controls, name);
			if (r is not null)
				return r;
		}
		return null;
	}

	private void DumpVendorApi(System.Windows.Forms.Control control, int x, int y, int formX, int formClientY)
	{
		try
		{
			string text = control.GetType().FullName ?? "?";
			if (!text.Contains("Telerik", StringComparison.Ordinal) && !text.Contains("Syncfusion", StringComparison.Ordinal))
			{
				return;
			}
			lock (s_vendorDumpDone)
			{
				DumpPageState();
				if (text.Contains("RadGridView", StringComparison.Ordinal))
				{
					try
					{
						object? rows = control.GetType().GetProperty("SelectedRows")?.GetValue(control);
						int rc = -1;
						if (rows is System.Collections.ICollection rcol)
							rc = rcol.Count;
						object? cur = control.GetType().GetProperty("CurrentRow")?.GetValue(control);
						object? idx = cur?.GetType().GetProperty("Index")?.GetValue(cur);
						Console.WriteLine("[VAPI] grid selcount=" + rc + " currow=" + (idx?.ToString() ?? "?"));
					}
					catch (Exception exg) { Console.WriteLine("[VAPI] grid-throw " + exg.GetType().Name); }
				}
				if (s_vendorDumpDone.Count > 200 || !s_vendorDumpDone.Add(text))
				{
					return;
				}
			}
			int controlAbsX = GetControlAbsX(control, formX);
			int controlAbsY = GetControlAbsY(control, formClientY);
			Console.WriteLine("[VAPI] control=" + text + " clientPt=" + (x - controlAbsX) + "," + (y - controlAbsY) + " bounds=" + control.Bounds.ToString() + " visible=" + control.Visible + " enabled=" + control.Enabled);
			try
			{
				System.Windows.Forms.Control parent = control.Parent;
				string text2 = ((parent == null) ? "null" : (parent.GetType().Name + " vis=" + parent.Visible));
				Console.WriteLine("[VAPI] parent=" + text2);
			}
			catch
			{
			}
			MethodInfo[] methods = control.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (MethodInfo methodInfo in methods)
			{
				string name = methodInfo.Name;
				if (!name.Contains("Click", StringComparison.OrdinalIgnoreCase) && !name.Contains("Press", StringComparison.OrdinalIgnoreCase) && !name.Contains("Popup", StringComparison.OrdinalIgnoreCase) && !name.Contains("Perform", StringComparison.OrdinalIgnoreCase) && !name.Contains("Show", StringComparison.OrdinalIgnoreCase) && !name.Contains("Toggle", StringComparison.OrdinalIgnoreCase) && !name.Contains("DropDown", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				string text3 = "";
				try
				{
					ParameterInfo[] parameters = methodInfo.GetParameters();
					foreach (ParameterInfo parameterInfo in parameters)
					{
						text3 = text3 + parameterInfo.ParameterType.Name + ",";
					}
				}
				catch
				{
				}
				Console.WriteLine("[VAPI] ctl.m " + (methodInfo.DeclaringType?.Name ?? "?") + "." + name + "(" + text3 + ")");
			}
			object obj3 = SafeGetObj(control, "ElementTree");
			if (obj3 == null)
			{
				object obj4 = SafeGetObj(control, "RootElement");
				Console.WriteLine("[VAPI] ElementTree=null RootElement=" + ((obj4 == null) ? "null" : obj4.GetType().FullName));
				return;
			}
			Console.WriteLine("[VAPI] tree=" + (obj3.GetType().FullName ?? "?"));
			MethodInfo[] methods2 = obj3.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (MethodInfo methodInfo2 in methods2)
			{
				string name2 = methodInfo2.Name;
				if (!name2.Contains("ElementAtPoint", StringComparison.Ordinal) && !name2.Contains("Dispatch", StringComparison.Ordinal) && !name2.Contains("Mouse", StringComparison.Ordinal) && !name2.Contains("Click", StringComparison.Ordinal))
				{
					continue;
				}
				string text4 = "";
				try
				{
					ParameterInfo[] parameters2 = methodInfo2.GetParameters();
					foreach (ParameterInfo parameterInfo2 in parameters2)
					{
						text4 = text4 + parameterInfo2.ParameterType.Name + ",";
					}
				}
				catch
				{
				}
				Console.WriteLine("[VAPI] tree.m " + name2 + "(" + text4 + ")");
			}
			object obj6 = null;
			try
			{
				MethodInfo method = ResolvePointOverload(obj3);
				if ((object)method != null)
				{
					obj6 = method.Invoke(obj3, new object[1]
					{
						new Point(x - controlAbsX, y - controlAbsY)
					});
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("[VAPI] at-throw " + ex.ToString().Substring(0, System.Math.Min(600, ex.ToString().Length)));
			}
			Console.WriteLine("[VAPI] at=" + ((obj6 == null) ? "null" : obj6.GetType().FullName));
		}
		catch (Exception ex2)
		{
			try
			{
				Console.WriteLine("[VAPI] dump-throw " + ex2.GetType().Name);
			}
			catch
			{
			}
		}
	}

	private static System.Reflection.MethodInfo? ResolvePointOverload(object tree)
	{
		// GetElementAtPoint has several overloads (Point / Point+predicate /
		// subtree walkers, some open-generic); GetMethod(name) throws
		// AmbiguousMatchException. Prefer the closed single-Point overload
		// declared on the tree type itself, else any closed single-Point one.
		System.Reflection.MethodInfo? any = null;
		try
		{
			foreach (System.Reflection.MethodInfo m in tree.GetType().GetMethods(
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
			{
				if (m.Name != "GetElementAtPoint" || m.ContainsGenericParameters)
					continue;
				System.Reflection.ParameterInfo[] ps = m.GetParameters();
				if (ps.Length != 1 || ps[0].ParameterType.FullName != "System.Drawing.Point")
					continue;
				if (m.DeclaringType == tree.GetType())
					return m;
				any ??= m;
			}
		}
		catch { }
		return any;
	}

	private static bool IsVendorControl(System.Windows.Forms.Control control)
	{
		try
		{
			string full = control.GetType().FullName ?? "";
			return full.Contains("Telerik", System.StringComparison.Ordinal)
				|| full.Contains("Syncfusion", System.StringComparison.Ordinal);
		}
		catch { return false; }
	}

	private bool TrySelectGridRow(System.Windows.Forms.Control control, int x, int y, int formX, int formClientY)
	{
		// Telerik grid row select through the vendor element tree: map the
		// point to the row element, resolve its row info, drive CurrentRow.
		// Synthetic mouse reaches edit mode but does not move selection.
		try
		{
			try { EnsureElementLayout(control); } catch { }
			object? tree = control.GetType().GetProperty("ElementTree")?.GetValue(control);
			if (tree is null)
				return false;
			System.Reflection.MethodInfo? gap = null;
			foreach (System.Reflection.MethodInfo m in tree.GetType().GetMethods(
				System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
			{
				if (m.Name == "GetElementAtPoint" && !m.ContainsGenericParameters && m.GetParameters().Length == 1
					&& m.GetParameters()[0].ParameterType.FullName == "System.Drawing.Point")
				{
					gap = m;
					break;
				}
			}
			if (gap is null)
				return false;
			int cx = x - GetControlAbsX(control, formX);
			int cy = y - GetControlAbsY(control, formClientY);
			object? el = null;
			try { el = gap.Invoke(tree, new object[] { new Point(cx, cy) }); } catch { }
			if (el is null)
				return false;
			string chain = "";
			object? rowEl = null;
			object? cur = el;
			for (int i = 0; i < 10 && cur is not null; i++)
			{
				string tn = cur.GetType().Name;
				chain += (i == 0 ? "" : "<") + tn;
				if (rowEl is null && tn.Contains("Row", StringComparison.Ordinal)
					&& !tn.Contains("Header", StringComparison.Ordinal) && !tn.Contains("Table", StringComparison.Ordinal)
					&& !tn.Contains("Summary", StringComparison.Ordinal))
					rowEl = cur;
				try { cur = cur.GetType().GetProperty("Parent")?.GetValue(cur); } catch { cur = null; }
			}
			Console.WriteLine("[VAPI] gridrow chain=" + chain);
			if (rowEl is null)
				return false;
			object? rowInfo = null;
			foreach (string pn in new string[] { "RowInfo", "Data", "Row" })
			{
				try { rowInfo = rowEl.GetType().GetProperty(pn)?.GetValue(rowEl); } catch { }
				if (rowInfo is not null)
					break;
			}
			Console.WriteLine("[VAPI] gridrow info=" + (rowInfo?.GetType().Name ?? "?"));
			if (rowInfo is null)
				return false;
			System.Reflection.PropertyInfo? curProp = control.GetType().GetProperty("CurrentRow");
			if (curProp is not null && curProp.CanWrite && curProp.PropertyType.IsInstanceOfType(rowInfo))
			{
				curProp.SetValue(control, rowInfo);
				RecordInput("vendor", $"gridrow current {control.Name} {x},{y}");
				Console.WriteLine("[VAPI] gridrow-selected current");
			try
			{
				object? post = control.GetType().GetProperty("CurrentRow")?.GetValue(control);
				object? pidx = post?.GetType().GetProperty("Index")?.GetValue(post);
				Console.WriteLine("[VAPI] gridrow-post idx=" + (pidx?.ToString() ?? "?"));
			}
			catch { }
				DumpPageState();
				control.Invalidate();
				return true;
			}
			return false;
		}
		catch (Exception ex) { Console.WriteLine("[VAPI] gridrow-throw " + ex.GetType().Name); return false; }
	}
	private bool TryActivatePanoramaTile(System.Windows.Forms.Control control, int x, int y, int formX, int formClientY)
	{
		// Panorama tiles carry the book commands but synthetic Control
		// mouse events never reach the tile element (vendor hit-testing
		// uses its own cursor state). Resolve the tile at the point
		// through the vendor tree and activate it once.
		try
		{
			try { EnsureElementLayout(control); } catch { }
			object? tree = control.GetType().GetProperty("ElementTree")?.GetValue(control);
			if (tree is null)
				return false;
			System.Reflection.MethodInfo? gap = null;
			foreach (System.Reflection.MethodInfo m in tree.GetType().GetMethods(
				System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
			{
				if (m.Name == "GetElementAtPoint" && !m.ContainsGenericParameters && m.GetParameters().Length == 1
					&& m.GetParameters()[0].ParameterType.FullName == "System.Drawing.Point")
				{
					gap = m;
					break;
				}
			}
			if (gap is null)
				return false;
			Point pt = new Point(x - GetControlAbsX(control, formX), y - GetControlAbsY(control, formClientY));
			object? el = null;
			try { el = gap.Invoke(tree, new object[] { pt }); } catch { }
			Console.WriteLine("[VAPI] tile-el=" + (el is null ? "null" : el.GetType().Name) + " pt=" + pt);
			if (el is null || !IsActionableElement(el.GetType().Name))
				return false;
			System.Reflection.MethodInfo? click = null;
			foreach (System.Reflection.MethodInfo m in el.GetType().GetMethods(
				System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
			{
				if (m.Name == "PerformClick" && !m.ContainsGenericParameters && m.GetParameters().Length == 0)
				{
					click = m;
					break;
				}
			}
			if (click is null)
				return false;
			try { click.Invoke(el, null); }
			catch (Exception ex) { Console.WriteLine("[VAPI] tile-invoke-throw " + ex.GetType().Name); return false; }
			RecordInput("vendor", $"tile-click {el.GetType().Name} {control.Name} {x},{y}");
			Console.WriteLine("[VAPI] tile-clicked " + el.GetType().Name);
			DumpPageState();
			control.Invalidate();
			return true;
		}
		catch (Exception ex) { Console.WriteLine("[VAPI] tile-throw " + ex.GetType().Name); return false; }
	}
	
	private bool TryVendorActivate(System.Windows.Forms.Control control, int x, int y, int formX, int formClientY)
	{
		// Explicit vendor input adapter: route clicks that the generic
		// synthetic path cannot serve through the vendor's own activation,
		// exactly once (true = consumed, skip the synthetic dispatch).
		// Proven-preserved paths stay untouched: panorama tiles by
		// container element dispatch (details navigate). Standalone
		// RadButtons route through the vendor PerformClick: synthesizing
		// Control.OnClick on the button bypasses its ButtonElement click
		// path, so the wired Click handler never runs (SHOW ALL dead).
		try
		{
			if (!IsVendorControl(control) || !control.Visible || !control.Enabled)
				return false;
			string name = control.GetType().Name;
			if (name.Contains("DropDown", System.StringComparison.Ordinal)
				&& !(control.GetType().Name.Contains("ToolStrip", System.StringComparison.Ordinal)))
			{
				System.Reflection.MethodInfo? show = null;
				System.Reflection.MethodInfo? hide = null;
				foreach (System.Reflection.MethodInfo m in control.GetType().GetMethods(
					System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
				{
					if (m.ContainsGenericParameters)
						continue;
					if (m.Name == "ShowDropDown" && m.GetParameters().Length == 0)
						show = m;
					if ((m.Name == "HidePopup" || m.Name == "ClosePopup") && m.GetParameters().Length == 0)
						hide = m;
				}
				if (show is not null)
				{
					bool isOpen = false;
					try
					{
						object? pv = control.GetType().GetProperty("IsPopupVisible")?.GetValue(control);
						if (pv is bool b)
							isOpen = b;
					}
					catch { }
					try
					{
						if (isOpen && hide is not null)
							hide.Invoke(control, null);
						else if (!isOpen)
							show.Invoke(control, null);
						// Popup already open and no closer available: consume
						// the click so the synthetic Control.Click fallback
						// cannot toggle the popup shut; item clicks route
						// through the popup path before reaching dispatch.
					}
					catch { return false; }
					RecordInput("vendor", $"dropdown {(isOpen ? (hide is not null ? "hide" : "keep-open") : "show")} {control.Name} {x},{y}");
					control.Invalidate();
					return true;
				}
			}
			if (name.Contains("Panorama", System.StringComparison.Ordinal))
			{
				if (TryActivatePanoramaTile(control, x, y, formX, formClientY))
					return true;
			}
			if (name.Contains("RadGridView", System.StringComparison.Ordinal))
			{
				if (TrySelectGridRow(control, x, y, formX, formClientY))
					return true;
			}
			if (name.Contains("RadButton", System.StringComparison.Ordinal))
			{
				System.Reflection.MethodInfo? perform = null;
				foreach (System.Reflection.MethodInfo m in control.GetType().GetMethods(
					System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
				{
					if (m.Name == "PerformClick" && !m.ContainsGenericParameters && m.GetParameters().Length == 0)
					{
						perform = m;
						break;
					}
				}
				if (perform is not null)
				{
					if (ReadClickMode(control) == "Press")
					{
						RecordInput("vendor", $"button press-mode consumed {control.Name} {x},{y}");
						control.Invalidate();
						return true;
					}
					try
					{
						perform.Invoke(control, null);
					}
					catch { return false; }
					RecordInput("vendor", $"button perform-click {control.Name} {x},{y}");
					DumpPageState();
					control.Invalidate();
					return true;
				}
			}
		}
		catch { }
		return false;
	}

	private static string ReadClickMode(System.Windows.Forms.Control control)
	{
		// ClickMode decides press-vs-release firing.
		try
		{
			object? btnEl = control.GetType().GetProperty("ButtonElement")?.GetValue(control);
			object? mode = btnEl?.GetType().GetProperty("ClickMode")?.GetValue(btnEl);
			return mode?.ToString() ?? "?";
		}
		catch { return "?"; }
	}
	
	private void FireOuterKeyDown(System.Windows.Forms.Control outer, System.Windows.Forms.Keys keyData)
	{
		// Subscriber-direct KeyDown: the vendor OnKeyDown override may not
		// raise subscriber KeyDown (no search on Return). Fire the Control
		// event list directly, once.
		try
		{
			System.Reflection.FieldInfo? keyField = typeof(System.Windows.Forms.Control).GetField("s_keyDownEvent",
				System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
			if (keyField is null)
				return;
			object? key = keyField.GetValue(null);
			if (key is null)
				return;
			System.Reflection.PropertyInfo? evProp = typeof(System.Windows.Forms.Control).GetProperty("Events",
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
			System.ComponentModel.EventHandlerList? list = evProp?.GetValue(outer) as System.ComponentModel.EventHandlerList;
			System.Delegate? d = list?[key];
			if (d is System.Windows.Forms.KeyEventHandler keh)
			{
				keh.Invoke(outer, new System.Windows.Forms.KeyEventArgs(keyData));
			}
		}
		catch (Exception ex) { Console.WriteLine("[VAPI] keysub-throw " + ex.GetType().Name); }
	}
	
	private static System.Windows.Forms.Control OuterKeyControl(System.Windows.Forms.Control c)
	{
		// Hosted editors (Telerik HostedTextBoxBase): focus sits on the
		// inner box but the app wires KeyDown on the outer vendor control.
		// Desktop routes hosted keys to the owner; climb to it.
		try
		{
			while (c.Parent is not null && c.GetType().Name.Contains("Hosted", StringComparison.Ordinal) && IsVendorControl(c.Parent))
				c = c.Parent;
		}
		catch { }
		return c;
	}
	
	private static bool IsActionableElement(string typeName)
	{
		if (typeName.Contains("ScrollBar", System.StringComparison.Ordinal)
			|| typeName.Contains("Primitive", System.StringComparison.Ordinal)
			|| typeName.Contains("Grip", System.StringComparison.Ordinal)
			|| typeName.Contains("Container", System.StringComparison.Ordinal))
			return false;
		return typeName.Contains("Item", System.StringComparison.Ordinal)
			|| typeName.Contains("Button", System.StringComparison.Ordinal)
			|| typeName.Contains("Tile", System.StringComparison.Ordinal)
			|| typeName.Contains("Tab", System.StringComparison.Ordinal);
	}

	private static object? WalkElementAt(object? root, int px, int py)
	{
		// Manual deepest-child hit test in tree space (CBRs are parent-
		// relative): the vendor GetElementAtPoint answers null headless,
		// but laid-out CBRs are truthful. Later siblings win (paint order).
		object? best = null;
		try
		{
			WalkElementKids(root, 0, 0, px, py, ref best);
		}
		catch { }
		return best;
	}

	private static void WalkElementKids(object? el, int ox, int oy, int px, int py, ref object? best)
	{
		System.Collections.IEnumerable? kids = null;
		try { kids = el?.GetType().GetProperty("Children")?.GetValue(el) as System.Collections.IEnumerable; } catch { }
		if (kids is null)
			return;
		try
		{
		foreach (object? k in kids)
		{
			try
			{
			if (k is null)
				continue;
			int kx = 0, ky = 0, kw = 0, kh = 0;
			try
			{
				object? r = k.GetType().GetProperty("ControlBoundingRectangle")?.GetValue(k);
				if (r is null)
					continue;
				kx = System.Convert.ToInt32(r.GetType().GetProperty("X")?.GetValue(r));
				ky = System.Convert.ToInt32(r.GetType().GetProperty("Y")?.GetValue(r));
				kw = System.Convert.ToInt32(r.GetType().GetProperty("Width")?.GetValue(r));
				kh = System.Convert.ToInt32(r.GetType().GetProperty("Height")?.GetValue(r));
			}
			catch { continue; }
			if (kw <= 0 || kh <= 0)
				continue;
			int ax = ox + kx, ay = oy + ky;
			if (px < ax || px >= ax + kw || py < ay || py >= ay + kh)
				continue;
			if (IsActionableElement(k.GetType().Name))
				best = k;
			WalkElementKids(k, ax, ay, px, py, ref best);
			}
			catch { continue; }
		}
		}
		catch { }
	}

	private bool TryCommitListDataItem(object el)
	{
		// List-item commit through the DATA item: the application listens
		// to SelectedIndexChanged on the owning dropdown, which the data
		// item's Selected setter drives (the app's own
		// UpdateDropDownListSelectedItem does exactly this). The visual
		// item's Selected is read-only; its PerformClick only raises the
		// base element Click event, which commits nothing visible.
		try
		{
			string tn = el.GetType().Name;
			if (!tn.Contains("ListVisualItem", StringComparison.Ordinal)
				&& !tn.Contains("ListItem", StringComparison.Ordinal))
				return false;
			object? data = null;
			try { data = el.GetType().GetProperty("Data")?.GetValue(el); } catch { }
			if (data is null)
				return false;
			System.Reflection.PropertyInfo? sel = data.GetType().GetProperty("Selected");
			if (sel is null || !sel.CanWrite || sel.PropertyType != typeof(bool))
				return false;
			sel.SetValue(data, true);
			Console.WriteLine("[VAPI] item-data-selected " + data.GetType().Name);
			return true;
		}
		catch (Exception ex) { Console.WriteLine("[VAPI] item-data-throw " + ex.GetType().Name); return false; }
	}
	private void CloseOwnerDropdown()
	{
		// Dismiss the open vendor dropdown popup after a committed
		// selection (desktop mouse handling closes it; programmatic
		// selection does not).
		try
		{
			System.Windows.Forms.Form? f = _activeForm;
			if (f is null)
				return;
			System.Collections.Generic.List<System.Windows.Forms.Control> stack = new() { f };
			while (stack.Count > 0)
			{
				System.Windows.Forms.Control c = stack[stack.Count - 1];
				stack.RemoveAt(stack.Count - 1);
				try
				{
					if (IsVendorControl(c) && c.GetType().Name.Contains("DropDown", StringComparison.Ordinal))
					{
						object? pv = c.GetType().GetProperty("IsPopupVisible")?.GetValue(c);
						if (pv is bool b && b)
						{
							foreach (System.Reflection.MethodInfo m in c.GetType().GetMethods(
								System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
							{
								if ((m.Name == "HidePopup" || m.Name == "ClosePopup") && !m.ContainsGenericParameters && m.GetParameters().Length == 0)
								{
									m.Invoke(c, null);
									Console.WriteLine("[VAPI] owner-dropdown-closed " + c.Name);
									break;
								}
							}
						}
					}
				}
				catch { }
				foreach (System.Windows.Forms.Control k in c.Controls)
					stack.Add(k);
			}
		}
		catch { }
	}
	
	private bool TryVendorElementClick(System.Windows.Forms.Control popup, int x, int y)
	{
		// Popup item activation: the popup hosts pure elements (no child
		// controls), so synthetic Control dispatch cannot serve item clicks.
		// Resolve the element under the point and activate it through the
		// vendor path (PerformClick). True = consumed exactly once.
		try
		{
			if (!IsVendorControl(popup))
				return false;
			object? tree = null;
			try { tree = popup.GetType().GetProperty("ElementTree")?.GetValue(popup); } catch { }
			if (tree is null)
				return false;
			try { EnsureElementLayout(popup); } catch { }
			// Popups are canvas-positioned: TopPopupAt matches Location
			// directly against page coordinates, so the client point is
			// Location-relative. GetControlAbsX(popup, popup.Location.X)
			// double-counts popup.Left and every lookup misses.
			int ax = popup.Location.X;
			int ay = popup.Location.Y;
			Point pt = new Point(x - ax, y - ay);
			Console.WriteLine("[VAPI] item-pt=" + pt + " popup-loc=" + popup.Location + " popup-sz=" + popup.Width + "x" + popup.Height);
			object? el = null;
			try
			{
				System.Reflection.MethodInfo? gap = ResolvePointOverload(tree);
				if (gap is not null)
					el = gap.Invoke(tree, new object[] { pt });
			}
			catch { }
			if (el is null)
			{
				object? root = null;
				try { root = tree.GetType().GetProperty("RootElement")?.GetValue(tree); } catch { }
				el = WalkElementAt(root, pt.X, pt.Y);
			}
			Console.WriteLine("[VAPI] item-el=" + (el is null ? "null" : el.GetType().FullName));
			if (el is null || !IsActionableElement(el.GetType().Name))
				return false;
			System.Reflection.MethodInfo? click = null;
			try
			{
				foreach (System.Reflection.MethodInfo m in el.GetType().GetMethods(
					System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
				{
					if (m.Name == "PerformClick" && !m.ContainsGenericParameters && m.GetParameters().Length == 0)
					{
						click = m;
						break;
					}
				}
			}
			catch { }
			if (click is null)
			{
				Console.WriteLine("[VAPI] item-no-click");
				return false;
			}
			try
			{
				if (TryCommitListDataItem(el))
			{
				CloseOwnerDropdown();
				RecordInput("vendor", $"element-select {el.GetType().Name} {popup.Name} {x},{y}");
				Console.WriteLine("[VAPI] element-selected " + el.GetType().Name);
				DumpPageState();
				try { WasmRepaintGate.MarkGlobalDirty(); } catch { }
				try { popup.Invalidate(); } catch { }
				return true;
			}
			click.Invoke(el, null);
			}
			catch (Exception ex2) { Console.WriteLine("[VAPI] item-invoke-throw " + ex2.GetType().Name); return false; }
			RecordInput("vendor", $"element-click {el.GetType().Name} {popup.Name} {x},{y}");
			Console.WriteLine("[VAPI] element-clicked " + el.GetType().Name);
			try { WasmRepaintGate.MarkDirtyChain(popup); } catch { }
			try { popup.Invalidate(); } catch { }
			return true;
		}
		catch { }
		return false;
	}

	private void DispatchControlClick(System.Windows.Forms.Control control, int x, int y, System.Windows.Forms.MouseButtons clickedButton, int clicks, int formX, int formClientY)
	{
		DumpVendorApi(control, x, y, formX, formClientY);
		if (TryVendorActivate(control, x, y, formX, formClientY))
			return;
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
			int num = 24;
			int num2 = 22;
			int num3 = ListViewCapacity(listView.Height);
			int num4 = ResolveListViewTop(listView);
			int count = listView.Items.Count;
			if (count > num3 && x - GetControlAbsX(listView, formX) >= listView.Width - 14)
			{
				int num5 = ((y - GetControlAbsY(listView, formClientY) < listView.Height / 2) ? (-num3) : num3);
				if (ScrollListView(listView, num5))
				{
					RecordInput("click", $"{x},{y} listview={listView.Name} page={num5}");
					WasmRepaintGate.MarkGlobalDirty();
				}
				return;
			}
			int num6 = y - (GetControlAbsY(listView, formClientY) + num);
			if (num6 >= 0)
			{
				int num7 = num6 / num2 + num4;
				if (num7 >= num4 && num7 < count)
				{
					System.Windows.Forms.ListViewItem listViewItem = listView.Items[num7];
					RecordInput("click", $"{x},{y} listview={listView.Name} row={num7}");
					listViewItem.Selected = !listViewItem.Selected;
					RaiseMouseEvent(s_onMouseClickMethod, listView, clickedButton, clicks, x, y);
				}
			}
			return;
		}
		if (control is System.Windows.Forms.ToolStrip toolStrip)
		{
			int controlAbsX = GetControlAbsX(toolStrip, formX);
			int controlAbsY = GetControlAbsY(toolStrip, formClientY);
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
			int controlAbsX2 = GetControlAbsX(control, formX);
			int controlAbsY2 = GetControlAbsY(control, formClientY);
			System.Windows.Forms.MouseEventArgs e = new System.Windows.Forms.MouseEventArgs(clickedButton, clicks, x - controlAbsX2, y - controlAbsY2, 0);
			s_onClickMethod?.Invoke(control, new object[1] { e });
			RaiseMouseEvent(s_onMouseClickMethod, control, clickedButton, clicks, x, y);
		}
		catch (Exception ex2)
		{
			Console.WriteLine("[WinFormsAppHost] Control click error: " + ex2.GetType().Name + ": " + ex2.Message.Split('\n')[0]);
			try
			{
				Console.WriteLine("[diagstack] " + string.Join(" <- ", from f in new StackTrace(RootCause(ex2), fNeedFileInfo: false).GetFrames().Take(12)
					select f.GetMethod()));
			}
			catch
			{
			}
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
		Console.WriteLine($"[Host] ItemClick enter: '{item.Name}' at {DateTime.UtcNow:HH:mm:ss.fff}");
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
			Console.WriteLine($"[Host] ItemClick exit: '{item.Name}' at {DateTime.UtcNow:HH:mm:ss.fff}");
		}
	}

	private bool DropdownKeyNav(System.Windows.Forms.Keys keyData)
	{
		System.Windows.Forms.Keys keys = keyData & System.Windows.Forms.Keys.KeyCode;
		if (keys != System.Windows.Forms.Keys.Down && keys != System.Windows.Forms.Keys.Up && keys != System.Windows.Forms.Keys.Return && keys != System.Windows.Forms.Keys.Home && keys != System.Windows.Forms.Keys.End)
		{
			return false;
		}
		List<(System.Windows.Forms.ToolStripDropDown, int, int)> openDropdowns = GetOpenDropdowns();
		if (openDropdowns.Count == 0)
		{
			return false;
		}
		System.Windows.Forms.ToolStripDropDown item = openDropdowns[openDropdowns.Count - 1].Item1;
		List<System.Windows.Forms.ToolStripItem> list = new List<System.Windows.Forms.ToolStripItem>();
		foreach (System.Windows.Forms.ToolStripItem item2 in item.Items)
		{
			if (item2.Available && item2.Enabled && !(item2 is System.Windows.Forms.ToolStripSeparator))
			{
				list.Add(item2);
			}
		}
		if (list.Count == 0)
		{
			return true;
		}
		int num = list.FindIndex((System.Windows.Forms.ToolStripItem i) => i.Selected);
		try
		{
			if (keys == System.Windows.Forms.Keys.Return)
			{
				System.Windows.Forms.ToolStripItem toolStripItem2 = ((num >= 0) ? list[num] : list[0]);
				Console.WriteLine("[Host] Dropdown keyboard activate: '" + toolStripItem2.Name + "'");
				CloseOpenDropdowns();
				PerformItemClick(toolStripItem2);
			}
			else
			{
				if (1 == 0)
				{
				}
				int num2 = keys switch
				{
					System.Windows.Forms.Keys.Home => 0, 
					System.Windows.Forms.Keys.End => list.Count - 1, 
					System.Windows.Forms.Keys.Up => (num <= 0) ? (list.Count - 1) : (num - 1), 
					_ => (num >= 0 && num < list.Count - 1) ? (num + 1) : 0, 
				};
				if (1 == 0)
				{
				}
				int num3 = num2;
				list[num3].Select();
				Console.WriteLine($"[Host] Dropdown keyboard select: '{list[num3].Name}' ({num3 + 1}/{list.Count})");
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("[WinFormsAppHost] Dropdown key error: " + ex.GetType().Name + ": " + (ex.Message ?? "").Split('\n')[0]);
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
		foreach (var (toolStripDropDown, _, _) in GetOpenDropdowns().ToList())
		{
			try
			{
				foreach (System.Windows.Forms.ToolStripItem item in toolStripDropDown.Items.OfType<System.Windows.Forms.ToolStripItem>().ToList())
				{
					try
					{
						if (item.Selected)
						{
							s_itemUnselectMethod?.Invoke(item, Array.Empty<object>());
						}
					}
					catch
					{
					}
				}
				toolStripDropDown.Visible = false;
			}
			catch
			{
			}
		}
		_openDropdowns.Clear();
		SetHoverItem(null);
	}

	private static bool TryCloseVendorPopupForEscape()
	{
		// Combo-Escape wedge (WF-R-070): a tracked open vendor popup
		// (Syncfusion PopupHost) wedges the renderer inside its own Escape
		// handling, so the host must close it instead of raising Escape into
		// the vendor. Mirror the ShowDropDown/HidePopup/ClosePopup reflection
		// pattern: vendor close first, Hide() fallback. Never throws.
		try
		{
			System.Windows.Forms.Control[] popups;
			try { popups = System.Windows.Forms.WasmPopupHost.Snapshot(); }
			catch { popups = System.Array.Empty<System.Windows.Forms.Control>(); }
			for (int i = popups.Length - 1; i >= 0; i--)
			{
				if (ClosePopupControlForEscape(popups[i])) return true;
			}
			System.Windows.Forms.WasmShownWindow[] wins;
			try { wins = System.Windows.Forms.WasmDialogHost.Snapshot(); }
			catch { return false; }
			for (int i = wins.Length - 1; i >= 0; i--)
			{
				System.Windows.Forms.Form f;
				try { f = wins[i].Form; if (f == null || f.IsDisposed || f.Disposing || !f.Visible) continue; }
				catch { continue; }
				if (!f.GetType().Name.Contains("Popup", System.StringComparison.Ordinal)) continue;
				if (ClosePopupControlForEscape(f)) return true;
			}
		}
		catch { }
		return false;
	}

	private static bool ClosePopupControlForEscape(System.Windows.Forms.Control popup)
	{
		string typeName;
		try
		{
			if (popup.IsDisposed || popup.Disposing || !popup.Visible) return false;
			typeName = popup.GetType().Name ?? string.Empty;
		}
		catch { return false; }
		if (!typeName.Contains("Popup", System.StringComparison.Ordinal)
			&& !typeName.Contains("DropDown", System.StringComparison.Ordinal)) return false;
		System.Reflection.MethodInfo? hide = null;
		try
		{
			foreach (System.Reflection.MethodInfo m in popup.GetType().GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
			{
				if (m.ContainsGenericParameters || m.GetParameters().Length != 0) continue;
				if (m.Name == "HidePopup" || m.Name == "ClosePopup") { hide = m; break; }
			}
		}
		catch { }
		try
		{
			if (hide != null) hide.Invoke(popup, null);
			else popup.Hide();
		}
		catch { }
		try { Console.WriteLine("[Host] Escape closed vendor popup " + popup.GetType().FullName); } catch { }
		return true;
	}

	public void OnKeyDown(string key, bool shift = false, bool ctrl = false, bool alt = false)
	{
		WasmDialogHost.AdvanceTurn();
		WasmRepaintGate.MarkGlobalDirty();
		(System.Windows.Forms.Keys KeyData, char? KeyChar) tuple = MapKey(key, shift, ctrl, alt);
		System.Windows.Forms.Keys item = tuple.KeyData;
		char? item2 = tuple.KeyChar;
		System.Windows.Forms.Control control = KeyTarget();
		RecordInput("key", key + Mods(shift, ctrl, alt) + " target=" + DescribeControl(control));
		Console.WriteLine("[Host] OnKeyDown(key=" + key + Mods(shift, ctrl, alt) + " target=" + DescribeControl(control) + ")");
		if (control == null)
		{
			return;
		}
		System.Windows.Forms.KeyEventArgs e = new System.Windows.Forms.KeyEventArgs(item);
		WasmMouseState.SetModifiers(ToKeys(shift, ctrl, alt));
		if (GetOpenDropdowns().Count > 0 && DropdownKeyNav(item))
		{
			return;
		}
		bool flag = PumpKeyFilters(control, alt ? 260 : 256, VkForPump(item, shift, ctrl, alt), (!alt) ? 1 : 536870912);
		if (flag)
		{
			e.Handled = true;
			e.SuppressKeyPress = true;
		}
		if (!flag && (ctrl | alt) && PumpCommandKey(control, item, alt, shift, ctrl))
		{
			return;
		}
		if (!flag && (item & ~System.Windows.Forms.Keys.Shift) == (System.Windows.Forms.Keys.A | System.Windows.Forms.Keys.Control) && control is System.Windows.Forms.TextBoxBase textBoxBase)
		{
			textBoxBase.SelectAll();
			e.SuppressKeyPress = true;
		}
		System.Windows.Forms.Keys keys = item & System.Windows.Forms.Keys.KeyCode;
		bool flag2 = !flag && (keys == System.Windows.Forms.Keys.Escape || keys == System.Windows.Forms.Keys.Return);
		if (flag2 && TopModalWindow(out System.Windows.Forms.Form modal) && modal != null && ((keys == System.Windows.Forms.Keys.Escape) ? modal.CancelButton : modal.AcceptButton) is System.Windows.Forms.Control control2)
		{
			NoteFocus(control2);
		}
		if (flag2 && EmulateProcessDialogKey(control, item))
		{
			if (TopModalWindow(out System.Windows.Forms.Form modal2) && modal2 != null)
			{
				CloseModalIfSettled(modal2);
			}
			e.Handled = true;
			e.SuppressKeyPress = true;
			return;
		}
		if (!flag && (item & System.Windows.Forms.Keys.KeyCode) == System.Windows.Forms.Keys.Escape && TryCloseVendorPopupForEscape())
		{
			e.Handled = true;
			e.SuppressKeyPress = true;
			return;
		}
		RaiseKey(s_onKeyDownMethod, control, e, "down");
		if (e.Handled || e.SuppressKeyPress)
		{
			return;
		}
		switch (item & System.Windows.Forms.Keys.KeyCode)
		{
		case System.Windows.Forms.Keys.Escape:
			if (GetOpenDropdowns().Count > 0)
			{
				CloseOpenDropdowns();
			}
			else
			{
				if (!TopModalWindow(out System.Windows.Forms.Form modal4) || modal4 == null)
				{
					return;
				}
				try
				{
					if (modal4.Modal && modal4.DialogResult == System.Windows.Forms.DialogResult.None)
					{
						modal4.DialogResult = System.Windows.Forms.DialogResult.Cancel;
					}
					modal4.Close();
					return;
				}
				catch (Exception ex)
				{
					BrowserErrorReporter.Report("dialog-close", ex);
					return;
				}
			}
			return;
		case System.Windows.Forms.Keys.Return:
		{
			System.Windows.Forms.Control outer = OuterKeyControl(control);
			FireOuterKeyDown(outer, item);
			Console.WriteLine("[VAPI] return-route inner=" + control.GetType().Name + " par=" + ((control.Parent is null) ? "null" : control.Parent.GetType().Name) + " outer=" + outer.GetType().Name);
			try
			{
				object? ot = outer.GetType().GetProperty("Text")?.GetValue(outer);
				Console.WriteLine("[VAPI] return-outer-text='" + (ot?.ToString() ?? "?") + "'");
			}
			catch { }
			try
			{
				System.Reflection.MethodInfo? od = outer.GetType().GetMethod("OnKeyDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
				Console.WriteLine("[VAPI] return-override OnKeyDown decl=" + (od?.DeclaringType?.Name ?? "?") + " keycode=" + item);
			}
			catch { }
			if (outer != control)
				RaiseKey(s_onKeyDownMethod, outer, new System.Windows.Forms.KeyEventArgs(item), "down");
		}
		{
			System.Windows.Forms.TextBox focusedTextBox = _focusedTextBox;
			System.Windows.Forms.Form modal3;
			if (focusedTextBox != null && focusedTextBox.Multiline && focusedTextBox.AcceptsReturn)
			{
				_focusedTextBox.Text += "\r\n";
			}
			else if (TopModalWindow(out modal3) && modal3 != null)
			{
				if (modal3.AcceptButton is System.Windows.Forms.Button button)
				{
					button.PerformClick();
					CloseModalIfSettled(modal3);
				}
			}
			else if (_activeForm?.AcceptButton is System.Windows.Forms.Button button2)
			{
				button2.PerformClick();
			}
			return;
		}
		case System.Windows.Forms.Keys.Back:
			if (_focusedTextBox != null && _focusedTextBox.Text.Length > 0)
			{
				System.Windows.Forms.TextBox focusedTextBox2 = _focusedTextBox;
				string text = _focusedTextBox.Text;
				int num = Math.Max(0, Math.Min(focusedTextBox2.SelectionStart, text.Length));
				int num2 = Math.Max(0, Math.Min(focusedTextBox2.SelectionLength, text.Length - num));
				if (num2 > 0)
				{
					focusedTextBox2.Text = text.Substring(0, num) + text.Substring(num + num2);
					focusedTextBox2.SelectionStart = num;
				}
				else if (num > 0)
				{
					focusedTextBox2.Text = text.Substring(0, num - 1) + text.Substring(num);
					focusedTextBox2.SelectionStart = num - 1;
				}
				else
				{
					focusedTextBox2.Text = text.Substring(0, text.Length - 1);
				}
			}
			return;
		}
		if ((alt && !item2.HasValue && key.Length == 1 && (PumpKeyFilters(control, 262, key[0], 536870912) || PumpMenuMnemonic(key[0]))) || !item2.HasValue)
		{
			return;
		}
		char valueOrDefault = item2.GetValueOrDefault();
		if (true && !PumpKeyFilters(control, alt ? 262 : 258, valueOrDefault, (!alt) ? 1 : 536870912))
		{
			RaiseKey(s_onKeyPressMethod, control, new System.Windows.Forms.KeyPressEventArgs(valueOrDefault), "press");
			if (control == _focusedTextBox && _focusedTextBox != null && valueOrDefault >= ' ')
			{
				string text2 = _focusedTextBox.Text;
				int num3 = Math.Max(0, Math.Min(_focusedTextBox.SelectionStart, text2.Length));
				int num4 = Math.Max(0, Math.Min(_focusedTextBox.SelectionLength, text2.Length - num3));
				_focusedTextBox.Text = text2.Substring(0, num3) + valueOrDefault + text2.Substring(num3 + num4);
				_focusedTextBox.SelectionStart = num3 + 1;
			}
		}
	}

	public void OnKeyUp(string key, bool shift = false, bool ctrl = false, bool alt = false)
	{
		WasmDialogHost.AdvanceTurn();
		WasmRepaintGate.MarkGlobalDirty();
		System.Windows.Forms.Keys item = MapKey(key, shift, ctrl, alt).KeyData;
		System.Windows.Forms.Control control = KeyTarget();
		RecordInput("keyup", key + Mods(shift, ctrl, alt) + " target=" + DescribeControl(control));
		if (control != null)
		{
			WasmMouseState.SetModifiers(ToKeys(shift, ctrl, alt));
			if (!PumpKeyFilters(control, alt ? 261 : 257, VkForPump(item, shift, ctrl, alt), alt ? 536870912 : 0))
			{
				RaiseKey(s_onKeyUpMethod, control, new System.Windows.Forms.KeyEventArgs(item), "up");
			}
		}
	}

	private static bool EmulateProcessDialogKey(System.Windows.Forms.Control? target, System.Windows.Forms.Keys keyData)
	{
		try
		{
			if ((object)s_processDialogKeyMethod == null)
			{
				return false;
			}
			for (System.Windows.Forms.Control control = target; control != null; control = control.Parent)
			{
				try
				{
					if (s_processDialogKeyMethod.Invoke(control, new object[1] { keyData }) is int num && num != 0)
					{
						return true;
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
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
		for (System.Windows.Forms.Control control2 = control; control2 != null; control2 = control2.Parent)
		{
			if (control2 == form)
			{
				return true;
			}
		}
		return false;
	}

	private static int VkForPump(System.Windows.Forms.Keys keyData, bool shift, bool ctrl, bool alt)
	{
		int num = (int)(keyData & System.Windows.Forms.Keys.KeyCode);
		if (num == 0)
		{
			if (alt)
			{
				num = 18;
			}
			else
			{
				num = (ctrl ? 17 : (shift ? 16 : 0));
			}
		}
		return num;
	}

	private bool PumpCommandKey(System.Windows.Forms.Control target, System.Windows.Forms.Keys keyData, bool alt, bool shift, bool ctrl)
	{
		try
		{
			nint hWnd = IntPtr.Zero;
			try
			{
				hWnd = target.Handle;
			}
			catch
			{
			}
			int num = VkForPump(keyData, shift, ctrl, alt);
			System.Windows.Forms.Message message = System.Windows.Forms.Message.Create(hWnd, alt ? 260 : 256, num, (!alt) ? 1 : 536870912);
			for (System.Windows.Forms.Control control = target; control != null; control = control.Parent)
			{
				string key = control.GetType().AssemblyQualifiedName ?? control.GetType().FullName ?? "?";
				MethodInfo value;
				lock (s_commandKeyMethods)
				{
					if (!s_commandKeyMethods.TryGetValue(key, out value))
					{
						try
						{
							Type type = typeof(System.Windows.Forms.Message).MakeByRefType();
							value = control.GetType().GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[2]
							{
								type,
								typeof(System.Windows.Forms.Keys)
							}, null);
							if (value != null && value.ReturnType != typeof(bool))
							{
								value = null;
							}
						}
						catch
						{
							value = null;
						}
						s_commandKeyMethods[key] = value;
					}
				}
				if (!(value == null))
				{
					try
					{
						object[] array = new object[2] { message, keyData };
						bool flag = (bool)(value.Invoke(control, array) ?? ((object)false));
						message = (System.Windows.Forms.Message)array[0];
						if (flag)
						{
							return true;
						}
					}
					catch
					{
					}
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
			System.Windows.Forms.Control activeForm = _activeForm;
			if (activeForm == null)
			{
				return false;
			}
			Stack<System.Windows.Forms.Control> stack = new Stack<System.Windows.Forms.Control>();
			stack.Push(activeForm);
			List<System.Windows.Forms.Control> list = new List<System.Windows.Forms.Control>();
			while (stack.Count > 0)
			{
				System.Windows.Forms.Control control = stack.Pop();
				string key = control.GetType().AssemblyQualifiedName ?? control.GetType().FullName ?? "?";
				MethodInfo value;
				lock (s_mnemonicMethods)
				{
					if (!s_mnemonicMethods.TryGetValue(key, out value))
					{
						try
						{
							MethodInfo method = control.GetType().GetMethod("ProcessShortcut", BindingFlags.Instance | BindingFlags.Public, null, new Type[1] { typeof(char) }, null);
							value = ((method != null && method.ReturnType == typeof(bool)) ? method : null);
						}
						catch
						{
							value = null;
						}
						s_mnemonicMethods[key] = value;
					}
				}
				if (value != null && control.Visible && control.Enabled)
				{
					list.Add(control);
				}
				try
				{
					foreach (System.Windows.Forms.Control control2 in control.Controls)
					{
						stack.Push(control2);
					}
				}
				catch
				{
				}
			}
			foreach (System.Windows.Forms.Control item in list)
			{
				MethodInfo value2 = null;
				string key2 = item.GetType().AssemblyQualifiedName ?? item.GetType().FullName ?? "?";
				lock (s_mnemonicMethods)
				{
					s_mnemonicMethods.TryGetValue(key2, out value2);
				}
				if (!(value2 != null))
				{
					continue;
				}
				try
				{
					if ((bool)(value2.Invoke(item, new object[1] { c }) ?? ((object)false)))
					{
						return true;
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		return false;
	}

	private bool PumpKeyFilters(System.Windows.Forms.Control target, int msg, int wParam, int lParam)
	{
		try
		{
			nint hWnd = IntPtr.Zero;
			try
			{
				hWnd = target.Handle;
			}
			catch
			{
			}
			System.Windows.Forms.Message message = System.Windows.Forms.Message.Create(hWnd, msg, wParam, lParam);
			return System.Windows.Forms.Application.FilterMessage(ref message);
		}
		catch (Exception ex)
		{
			Console.WriteLine("[WinFormsAppHost] Key filter error: " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0] + " site=" + SiteOf(ex));
			return false;
		}
	}

	private void RaiseKey(MethodInfo? method, System.Windows.Forms.Control target, EventArgs args, string kind)
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
			try
			{
				Console.WriteLine("[diagstack] " + string.Join(" <- ", from f in new StackTrace(RootCause(ex), fNeedFileInfo: false).GetFrames().Take(12)
					select f.GetMethod()));
			}
			catch
			{
			}
			BrowserErrorReporter.Report("key", ex, SiteOf(RootCause(ex)));
		}
	}

	private static string Mods(bool shift, bool ctrl, bool alt)
	{
		return (shift ? "+shift" : "") + (ctrl ? "+ctrl" : "") + (alt ? "+alt" : "");
	}

	private static (System.Windows.Forms.Keys KeyData, char? KeyChar) MapKey(string key, bool shift, bool ctrl, bool alt)
	{
		char? item = null;
		System.Windows.Forms.Keys keys;
		switch (key)
		{
		case "Enter":
			keys = System.Windows.Forms.Keys.Return;
			item = '\r';
			break;
		case "Escape":
			keys = System.Windows.Forms.Keys.Escape;
			break;
		case "Backspace":
			keys = System.Windows.Forms.Keys.Back;
			break;
		case "Tab":
			keys = System.Windows.Forms.Keys.Tab;
			break;
		case "Delete":
			keys = System.Windows.Forms.Keys.Delete;
			break;
		case "Insert":
			keys = System.Windows.Forms.Keys.Insert;
			break;
		case "Home":
			keys = System.Windows.Forms.Keys.Home;
			break;
		case "End":
			keys = System.Windows.Forms.Keys.End;
			break;
		case "PageUp":
			keys = System.Windows.Forms.Keys.Prior;
			break;
		case "PageDown":
			keys = System.Windows.Forms.Keys.Next;
			break;
		case "ArrowLeft":
			keys = System.Windows.Forms.Keys.Left;
			break;
		case "ArrowUp":
			keys = System.Windows.Forms.Keys.Up;
			break;
		case "ArrowRight":
			keys = System.Windows.Forms.Keys.Right;
			break;
		case "ArrowDown":
			keys = System.Windows.Forms.Keys.Down;
			break;
		case " ":
			keys = System.Windows.Forms.Keys.Space;
			item = ' ';
			break;
		case "CapsLock":
			keys = System.Windows.Forms.Keys.Capital;
			break;
		case "Shift":
			keys = System.Windows.Forms.Keys.ShiftKey;
			break;
		case "Control":
			keys = System.Windows.Forms.Keys.ControlKey;
			break;
		case "Alt":
			keys = System.Windows.Forms.Keys.Menu;
			break;
		case "Meta":
			keys = System.Windows.Forms.Keys.LWin;
			break;
		default:
		{
			if (key.Length > 1 && key[0] == 'F' && int.TryParse(key.AsSpan(1), out var result) && result >= 1 && result <= 24)
			{
				keys = (System.Windows.Forms.Keys)(112 + (result - 1));
			}
			else if (key.Length == 1)
			{
				item = key[0];
				keys = (System.Windows.Forms.Keys)char.ToUpperInvariant(key[0]);
			}
			else
			{
				keys = System.Windows.Forms.Keys.None;
			}
			break;
		}
		}
		System.Windows.Forms.Keys keys2 = keys;
		if (shift)
		{
			keys2 |= System.Windows.Forms.Keys.Shift;
		}
		if (ctrl)
		{
			keys2 |= System.Windows.Forms.Keys.Control;
		}
		if (alt)
		{
			keys2 |= System.Windows.Forms.Keys.Alt;
		}
		if ((ctrl | alt) && item.HasValue)
		{
			item = ((!ctrl || !char.IsLetter(item.Value)) ? ((char?)null) : new char?((char)(char.ToUpperInvariant(item.Value) - 65 + 1)));
		}
		return (KeyData: keys2, KeyChar: item);
	}

	private static string DescribeKids(System.Windows.Forms.Control.ControlCollection controls, int depth)
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (System.Windows.Forms.Control control in controls)
		{
			if (stringBuilder.Length > 0)
			{
				stringBuilder.Append(";");
			}
			stringBuilder.Append(control.GetType().Name + " v=" + control.Visible + " e=" + control.Enabled + " b=" + control.Left + "," + control.Top + "," + control.Width + "x" + control.Height);
			if (depth < 2 && control.Controls.Count > 0)
			{
				stringBuilder.Append("[" + DescribeKids(control.Controls, depth + 1) + "]");
			}
		}
		return stringBuilder.ToString();
	}

	private System.Windows.Forms.Control? HitTestDeep(System.Windows.Forms.Control.ControlCollection controls, int parentAbsX, int parentAbsY, int x, int y)
	{
		for (int num = controls.Count - 1; num >= 0; num--)
		{
			System.Windows.Forms.Control control = controls[num];
			if (control.Visible && control.Enabled && control.Width > 0 && control.Height > 0 && !(control.GetType().Name == "CaptionControl"))
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
		int value3 = Math.Max(9, (int)Math.Round((double)font.SizeInPoints * 96.0 / 72.0));
		return $"{value2}{value}{value3}px '{font.FontFamily.Name}', sans-serif";
	}

	private static bool TryPaintRegion(ICanvas canvas, System.Windows.Forms.Control c, int absX, int absY, int w, int h, bool clearFlag)
	{
		if (!FrameDirtyRect(c, out var rc) || !rc.HasValue)
		{
			return false;
		}
		string text = LayerIdFor(c);
		Bitmap value;
		lock (s_retainedPaint)
		{
			s_retainedPaint.TryGetValue(text, out value);
		}
		if (value == null || value.Width != w || value.Height != h)
		{
			return false;
		}
		Rectangle rectangle = Rectangle.Intersect(rc.Value, new Rectangle(0, 0, w, h));
		if (rectangle.Width <= 0 || rectangle.Height <= 0)
		{
			return BlitKnownLayer(canvas, text, absX, absY, w, h);
		}
		if (TryShiftRetained(canvas, c, value, absX, absY, w, h, rc.Value, clearFlag))
		{
			return true;
		}
		// A region/full repaint below supersedes any shift the narrow math
		// rejected (or that strip painting invalidated): the retained
		// surface no longer matches the noted displacement, so drop the
		// shift instead of applying it to fresh pixels later. No-op when
		// no shift is pending. A pure re-blit above leaves it pending.
		WasmRepaintGate.TakeScrollShift(c, out _, out _);
		if ((long)rectangle.Width * (long)rectangle.Height * 10 >= (long)w * (long)h * 9)
		{
			return false;
		}
		if (!(canvas is ICanvasRenderer { SupportsBitmapLayers: not false }))
		{
			if (!PaintRegionPixels(c, value, rectangle, clearFlag))
			{
				return false;
			}
			byte[] array = ExtractBitmapRgba(value);
			if (array == null)
			{
				return false;
			}
			canvas.DrawImage(array, w, h, absX, absY, w, h);
			if (WasmFrameCounters.PerfEnabled)
			{
				WasmFrameCounters.CountRasterFull((long)w * (long)h);
			}
			return true;
		}
		if (!PaintRegionPixels(c, value, rectangle, clearFlag))
		{
			return false;
		}
		byte[] array2 = value.ToRgbaBytes(rectangle);
		if (WasmFrameCounters.PerfEnabled && array2 != null)
		{
			WasmFrameCounters.AddBytesExtracted(array2.Length);
		}
		RecordBitmapPatch(canvas, text, array2, rectangle, absX, absY, w, h);
		if (WasmFrameCounters.PerfEnabled)
		{
			WasmFrameCounters.CountRasterRegion((long)rectangle.Width * (long)rectangle.Height);
		}
		return true;
	}

	private static bool TryShiftRetained(ICanvas canvas, System.Windows.Forms.Control c, Bitmap retained, int absX, int absY, int w, int h, Rectangle dirty, bool clearFlag)
	{
		if (!WasmRepaintGate.TakeScrollShift(c, out var dx, out var dy))
		{
			return false;
		}
		if (WasmFrameCounters.PerfEnabled)
			Console.WriteLine($"[Host] ShiftTake control='{c.GetType().FullName}' dx={dx} dy={dy} dirty={dirty} wh={w}x{h}");
		Rectangle[]? narrow = ComputeScrollNarrow(w, h, dx, dy, dirty);
		if (narrow is null || narrow.Length == 0)
		{
			if (WasmFrameCounters.PerfEnabled)
				Console.WriteLine($"[Host] ShiftNarrowNull control='{c.GetType().FullName}'");
			return false;
		}
		retained.ShiftPixels(dx, dy);
		foreach (Rectangle value in narrow)
		{
			if (!PaintRegionPixels(c, retained, value, clearFlag))
			{
				return false;
			}
		}
		if (!ScrollShiftUploadDisabled &&
			canvas is ICanvasRenderer { SupportsBitmapLayers: not false } renderer)
		{
			// Strip-only upload: the browser shifts its retained layer
			// first (ShiftBitmap travels out-of-band ahead of the patch),
			// then splices just the repainted pieces. The full-bitmap hash
			// is stale after a shift, so drop it: the next full paint
			// re-registers with Def instead of risking a hash-suppressed
			// blit of shifted content.
			string id = LayerIdFor(c);
			byte[][] strips = new byte[narrow.Length][];
			for (int i = 0; i < narrow.Length; i++)
				strips[i] = retained.ToRgbaBytes(narrow[i]);
			renderer.ShiftBitmap(id, dx, dy);
			for (int i = 0; i < narrow.Length; i++)
			{
				Rectangle value = narrow[i];
				byte[] strip = strips[i];
				RecordBitmapPatch(canvas, id, strip, value, absX, absY, w, h);
				if (WasmFrameCounters.PerfEnabled)
				{
					WasmFrameCounters.CountRasterRegion((long)value.Width * (long)value.Height);
					WasmFrameCounters.AddBytesExtracted(strip.Length);
				}
			}
			lock (s_layerHash)
			{
				s_layerHash.Remove(id);
			}
			// Shift-epoch tag (WF-P-066): hash the carried surface so a later
			// whole-control repaint with stable scroll generation can prove
			// pixel identity and skip its upload. Only watched controls carry
			// a generation seq; anything else keeps today's behavior.
			try
			{
				if (s_rteScrollWatch.TryGetGenerationSeq(c, out long gen))
				{
					byte[] full = retained.ToRgbaBytes();
					if (WasmFrameCounters.PerfEnabled)
					{
						WasmFrameCounters.AddBytesHashed(full.Length);
					}
					s_shiftEpochTags.Remove(c);
					var epochSlotForTake = new ShiftEpochSlot
					{
						Tag = new ShiftEpochTag(dx, dy, HashBytes(full), gen),
					};
					if (WasmFrameCounters.PerfEnabled)
					{
						epochSlotForTake.DiagSurface = full;
					}
					s_shiftEpochTags.Add(c, epochSlotForTake);
					if (WasmFrameCounters.PerfEnabled)
					{
						Console.WriteLine("[EpochGate] TAG frame=" + WasmFrameCounters.FrameSeq + " ctrl=" + c.GetType().Name + " dx=" + dx + " dy=" + dy + " gen=" + gen + " hash=" + HashBytes(full).ToString("X16"));
					}
				}
				else
				{
					s_shiftEpochTags.Remove(c);
					if (WasmFrameCounters.PerfEnabled)
					{
						Console.WriteLine("[EpochGate] UNTAG frame=" + WasmFrameCounters.FrameSeq + " ctrl=" + c.GetType().Name + " reason=nogenseq");
					}
				}
			}
			catch
			{
			}
			if (WasmFrameCounters.PerfEnabled)
			{
				WasmFrameCounters.CountScrollShift();
			}
			return true;
		}
		byte[] array = ExtractBitmapRgba(retained);
		if (array == null)
		{
			return false;
		}
		RecordBitmap(c, canvas, array, w, h, absX, absY, w, h, keepRetained: true);
		if (WasmFrameCounters.PerfEnabled)
		{
			long narrowArea = 0;
			foreach (Rectangle piece in narrow)
				narrowArea += (long)piece.Width * (long)piece.Height;
			WasmFrameCounters.CountRasterRegion(narrowArea);
			WasmFrameCounters.AddBytesExtracted(array.Length);
			WasmFrameCounters.CountScrollShift();
		}
		return true;
	}

	private static bool ScrollShiftUploadDisabled
	{
		get
		{
			// A/B + safety kill-switch (default off): with the switch set
			// the shift path keeps strip raster but uploads full bitmaps.
			return AppContext.TryGetSwitch("WfWasm.DisableScrollShift", out var off) && off;
		}
	}

	// Retained-scroll repaint set (WF-P-054): the disjoint pieces that must
	// be repainted after shifting the retained surface by (dx,dy) — the
	// fresh exposed strip plus the prior dirty rect and its shifted position
	// (edit rects are ambiguous-frame, pre- or post-scroll, so both repaint;
	// a superset repaint is always exact). Pieces are viewport-clipped and
	// merged only when the merge adds no gap fill (union area <= summed
	// areas), so Y-disjoint dirty/strip scrolls carry as two small rects
	// instead of bounding-boxing to the full frame. The 90% gate runs on the
	// summed piece areas (== true union area: survivors are pairwise
	// disjoint), never on a bounding box. Null (or empty) means fall back to
	// the full path. Guards (zero/diagonal/oversize) are unchanged.
	internal static Rectangle[]? ComputeScrollNarrow(int w, int h, int dx, int dy, Rectangle dirty)
	{
		if ((dx == 0 && dy == 0) || (dx != 0 && dy != 0))
		{
			return null;
		}
		if (Math.Abs(dx) >= w || Math.Abs(dy) >= h)
		{
			return null;
		}
		Rectangle b;
		if (dx > 0)
		{
			b = new Rectangle(0, 0, dx, h);
		}
		else if (dx < 0)
		{
			b = new Rectangle(w + dx, 0, -dx, h);
		}
		else
		{
			b = ((dy > 0) ? new Rectangle(0, 0, w, dy) : new Rectangle(0, h + dy, w, -dy));
		}
		Rectangle viewport = new Rectangle(0, 0, w, h);
		List<Rectangle> pieces = new List<Rectangle>(3) { b };
		Rectangle clipped = Rectangle.Intersect(dirty, viewport);
		if (clipped.Width > 0 && clipped.Height > 0)
			pieces.Add(clipped);
		Rectangle shifted = dirty;
		shifted.Offset(dx, dy);
		shifted.Intersect(viewport);
		if (shifted.Width > 0 && shifted.Height > 0)
			pieces.Add(shifted);
		// Merge fixpoint: only pairs whose bounding union adds no fill.
		// Overlap -> union area < sum; edge-adjacent -> equal; anything
		// with a gap (or a mere corner touch) stays separate.
		bool merged;
		do
		{
			merged = false;
			for (int i = 0; i < pieces.Count && !merged; i++)
			{
				for (int j = i + 1; j < pieces.Count && !merged; j++)
				{
					Rectangle u = Rectangle.Union(pieces[i], pieces[j]);
					long ua = (long)u.Width * (long)u.Height;
					long sa = (long)pieces[i].Width * (long)pieces[i].Height
						+ (long)pieces[j].Width * (long)pieces[j].Height;
					if (ua <= sa)
					{
						pieces[i] = u;
						pieces.RemoveAt(j);
						merged = true;
					}
				}
			}
		} while (merged);
		long area = 0;
		foreach (Rectangle piece in pieces)
			area += (long)piece.Width * (long)piece.Height;
		if (pieces.Count == 0 || area * 10 >= (long)w * (long)h * 9)
		{
			return null;
		}
		return pieces.ToArray();
	}

	// Post-shift verify epoch (WF-P-066): when TryShiftRetained carries for a
	// control, the shifted+strip-painted surface is hashed into a tag. At a
	// later whole-control repaint with stable scroll generation, the fresh
	// full raster is hashed and compared: equal pixels mean the served layer
	// (shifted + patched on the browser) already shows them, so the upload is
	// skipped and the layer hash restored. Any pixel difference uploads as
	// today, so a wrong skip is impossible by construction.
	internal readonly record struct ShiftEpochTag(int Dx, int Dy, ulong ShiftHash, long WatchGeneration);

	// Drain facts for the verify decision. LayoutMarksOnly/NoInputMarks are
	// supplied conservatively by the pump (per-mark provenance does not exist
	// at the gate without per-mark stack walks, rejected on per-frame cost +
	// boundary grounds): non-layout or input-silent pixel changes either
	// advance the watcher generation (voids) or change pixels (the hash gate
	// voids), so correctness never depends on these flags — they bound how
	// often a wasted hash is paid, and pin the contract for future provenance.
	internal readonly record struct PostShiftDrainFacts(
		bool DirtyIsWhole, bool LayoutMarksOnly, bool GenerationStable, bool NoInputMarks);

	internal static bool ShouldAttemptPostShiftVerify(ShiftEpochTag? tag, PostShiftDrainFacts facts) =>
		tag.HasValue && facts.DirtyIsWhole && facts.LayoutMarksOnly
		&& facts.GenerationStable && facts.NoInputMarks;

	internal static bool ShouldSuppressUpload(ulong tagHash, ulong rasterHash) =>
		tagHash == rasterHash;

	private sealed class ShiftEpochSlot
	{
		public ShiftEpochTag Tag;
		public byte[]? DiagSurface;
	}

	private static readonly ConditionalWeakTable<System.Windows.Forms.Control, ShiftEpochSlot> s_shiftEpochTags = new();

	// One-shot take for the verify site: a tag fires at most once, on the
	// first whole-control full repaint after the carry. Later repaints find
	// no tag and run today's path.
	private static bool TryTakePostShiftTag(System.Windows.Forms.Control c, out ShiftEpochTag tag, out byte[]? diagSurface)
	{
		tag = default;
		diagSurface = null;
		try
		{
			if (!s_shiftEpochTags.TryGetValue(c, out ShiftEpochSlot? slot) || slot is null)
				return false;
			s_shiftEpochTags.Remove(c);
			tag = slot.Tag;
			diagSurface = slot.DiagSurface;
			slot.DiagSurface = null;
			return true;
		}
		catch
		{
			tag = default;
			diagSurface = null;
			return false;
		}
	}

	// WF-P-FIX (2b): follow-up skip for the re-render branch. When a carried
	// shift's tag is still present and the drain facts match the verify gate
	// inputs, the full repaint is redundant: the retained shifted surface is
	// re-blitted and the layer hash restored with no repaint and no upload.
	// Disabled via AppContext switch "WfWasm.DisablePostShiftSkip". The
	// paint-then-compare path below stays as the safety net (switch set, or
	// paints that reach it without a tag).
	private static bool TrySkipPostShiftRepaint(ICanvas canvas, System.Windows.Forms.Control c, int absX, int absY, int w, int h)
	{
		try
		{
			if (AppContext.TryGetSwitch("WfWasm.DisablePostShiftSkip", out bool skipDisabled) && skipDisabled)
				return false;
			if (!s_shiftEpochTags.TryGetValue(c, out ShiftEpochSlot? slot) || slot is null)
				return false;
			ShiftEpochTag epochTag = slot.Tag;
			bool wholePost = !FrameDirtyRect(c, out var wrcPost) || !wrcPost.HasValue;
			bool stablePost = s_rteScrollWatch.TryGetGenerationSeq(c, out long genPost)
				&& genPost == epochTag.WatchGeneration;
			var postFacts = new PostShiftDrainFacts(
				wholePost, !s_frameGlobal, stablePost, NoInputMarks: true);
			if (!ShouldAttemptPostShiftVerify(epochTag, postFacts))
				return false;
			if (canvas is not ICanvasRenderer { SupportsBitmapLayers: not false } postLayers)
				return false;
			string postId = LayerIdFor(c);
			s_shiftEpochTags.Remove(c);
			lock (s_liveLayers)
			{
				s_liveLayers.Add(postId);
			}
			lock (s_layerHash)
			{
				s_layerHash[postId] = epochTag.ShiftHash;
				s_layerSize[postId] = (w, h);
				if (WasmFrameCounters.PerfEnabled)
				{
					WasmFrameCounters.CountLayerHashHit(true);
				}
			}
			postLayers.BlitBitmap(postId, absX, absY, w, h);
			if (WasmFrameCounters.PerfEnabled)
			{
				Console.WriteLine("[EpochGate] SKIP frame=" + WasmFrameCounters.FrameSeq + " ctrl=" + c.GetType().Name + " tagGen=" + epochTag.WatchGeneration);
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool PaintRegionPixels(System.Windows.Forms.Control c, Bitmap retained, Rectangle rc, bool clearFlag)
	{
		if ((object)s_onPaintMethod == null || (object)s_onPaintBackgroundMethod == null)
		{
			return false;
		}
		try
		{
			int clearVersion = retained.ClearVersion;
			using (Graphics graphics = Graphics.FromImage(retained))
			{
				if (clearFlag)
				{
					using SolidBrush brush = new SolidBrush(c.BackColor);
					graphics.FillRectangle(brush, rc);
				}
				graphics.SetClip(rc);
				using System.Windows.Forms.PaintEventArgs e = new System.Windows.Forms.PaintEventArgs(graphics, rc);
				LogFirstPaint(c);
				s_onPaintBackgroundMethod.Invoke(c, new object[1] { e });
				s_onPaintMethod.Invoke(c, new object[1] { e });
			}
			return retained.ClearVersion == clearVersion;
		}
		catch
		{
			return false;
		}
	}

	private static bool BlitKnownLayer(ICanvas canvas, string id, int absX, int absY, int w, int h)
	{
		if (!(canvas is ICanvasRenderer { SupportsBitmapLayers: not false } canvasRenderer))
		{
			return false;
		}
		lock (s_liveLayers)
		{
			s_liveLayers.Add(id);
		}
		canvasRenderer.BlitBitmap(id, absX, absY, w, h);
		return true;
	}

	private static void RecordBitmapPatch(ICanvas canvas, string id, byte[] rgba, Rectangle rc, int absX, int absY, int w, int h)
	{
		ICanvasRenderer canvasRenderer = (ICanvasRenderer)canvas;
		lock (s_liveLayers)
		{
			s_liveLayers.Add(id);
		}
		lock (s_layerHash)
		{
			s_layerSize[id] = (w, h);
		}
		canvasRenderer.PatchBitmap(id, rc.X, rc.Y, rc.Width, rc.Height, rgba);
		canvasRenderer.BlitBitmap(id, absX, absY, w, h);
	}

	private static void StoreRetainedPaint(object layerKey, Bitmap bitmap)
	{
		string key = LayerIdFor(layerKey);
		Bitmap value;
		lock (s_retainedPaint)
		{
			s_retainedPaint.TryGetValue(key, out value);
			s_retainedPaint[key] = (Bitmap)bitmap.Clone();
			if (WasmFrameCounters.PerfEnabled)
			{
				WasmFrameCounters.AddBytesCloned((long)bitmap.Width * (long)bitmap.Height * 4);
			}
		}
		value?.Dispose();
	}

	private static void OnTilePainted(object? sender, System.Windows.Forms.PaintEventArgs e)
	{
		try
		{
			string text = "?";
			try
			{
				text = e.ClipRectangle.ToString();
			}
			catch
			{
			}
			string text2 = "?";
			try
			{
				string text3 = (sender?.GetType().GetProperty("Text")?.GetValue(sender))?.ToString();
				if (!string.IsNullOrEmpty(text3))
				{
					text2 = ((text3.Length <= 18) ? text3 : text3.Substring(0, 18));
				}
			}
			catch
			{
			}
			string text4 = (sender?.GetType().Name ?? "?") + "|" + text + "|" + text2;
			try
			{
				string text5 = sender?.GetType().Name;
				if (text5 == "DockLayoutPanel" || text5 == "VerticalTileElement")
				{
					text4 = text4 + "|zc=" + SafeZOrderedCount(sender);
				}
			}
			catch
			{
			}
			s_paintFire.AddOrUpdate(text4, 1, (string _, int v) => v + 1);
		}
		catch
		{
		}
	}

	internal static Dictionary<string, int> TakePaintFire()
	{
		return new Dictionary<string, int>(s_paintFire);
	}

	internal static int SubscribeTilePaintFire(object? root)
	{
		int num = s_paintSubs;
		try
		{
			EventInfo eventInfo = root?.GetType().GetEvent("ElementPainted");
			if ((object)eventInfo != null && s_paintSubs < 500)
			{
				try
				{
					System.Windows.Forms.PaintEventHandler handler = OnTilePainted;
					if (eventInfo.EventHandlerType == typeof(System.Windows.Forms.PaintEventHandler))
					{
						eventInfo.AddEventHandler(root, handler);
					}
					else
					{
						MethodInfo method = typeof(WinFormsAppHost).GetMethod("OnTilePainted", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
						eventInfo.AddEventHandler(root, Delegate.CreateDelegate(eventInfo.EventHandlerType, method));
					}
					s_paintSubs++;
				}
				catch
				{
				}
			}
			if (root is IEnumerable enumerable && !(root is string))
			{
				foreach (object item in enumerable)
				{
					if (item != null)
					{
						if (s_paintSubs >= 500)
						{
							break;
						}
						SubscribeTilePaintFire(item);
					}
				}
				return s_paintSubs - num;
			}
			object[] array = new object[4]
			{
				SafeGetObj(root, "Children"),
				SafeGetObj(root, "Groups"),
				SafeGetObj(root, "Items"),
				SafeGetObj(root, "PanoramaElement")
			};
			object[] array2 = array;
			foreach (object obj2 in array2)
			{
				if (obj2 != null)
				{
					if (s_paintSubs >= 500)
					{
						break;
					}
					SubscribeTilePaintFire(obj2);
				}
			}
			if (root is System.Windows.Forms.Control control)
			{
				foreach (System.Windows.Forms.Control control2 in control.Controls)
				{
					if (s_paintSubs >= 500)
					{
						break;
					}
					SubscribeTilePaintFire(control2);
				}
			}
		}
		catch
		{
		}
		if (num == 0)
		{
			try
			{
				Console.WriteLine($"[TILE-VIS] paint-subs {s_paintSubs}");
			}
			catch
			{
			}
		}
		return s_paintSubs - num;
	}

	private static void TracePanoPaint(System.Windows.Forms.Control c)
	{
		try
		{
			if (!s_tileLateDumpDone || s_panoPaintTrace >= 6)
			{
				return;
			}
			string? fullName = c.GetType().FullName;
			if (fullName == null || !fullName.Contains("Panorama", StringComparison.Ordinal))
			{
				return;
			}
			s_panoPaintTrace++;
			bool value = IsFrameDirty(c);
			FrameDirtyRect(c, out var rc);
			StringBuilder stringBuilder = new StringBuilder();
			try
			{
				foreach (KeyValuePair<string, int> item in TakePaintFire())
				{
					if (stringBuilder.Length > 0)
					{
						stringBuilder.Append(',');
					}
					stringBuilder.Append(item.Key).Append('=').Append(item.Value);
					if (stringBuilder.Length > 2000)
					{
						break;
					}
				}
			}
			catch
			{
			}
			Console.WriteLine($"[TILE-VIS] pano-paint dirty={value} rect={rc?.ToString() ?? "whole"} fires=[{stringBuilder}]");
			try
			{
				Console.WriteLine("[TILE-VIS] " + FormatPanoScrollState(c));
			}
			catch
			{
			}
			try
			{
				Console.WriteLine("[TILE-VIS] " + FormatLayoutOffset(c));
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	internal static string FormatLayoutOffset(object? panoramaControl)
	{
		try
		{
			object obj = SafeGetObj(panoramaControl, "PanoramaElement");
			if (obj == null)
			{
				return "layoff=n/a";
			}
			string text = "zoom=?";
			try
			{
				FieldInfo fieldInfo = null;
				Type type = obj.GetType();
				while ((object)type != null && (object)fieldInfo == null)
				{
					fieldInfo = type.GetField("zoomedIn", BindingFlags.Instance | BindingFlags.NonPublic);
					type = type.BaseType;
				}
				if ((object)fieldInfo != null)
				{
					text = "zoom=" + fieldInfo.GetValue(obj);
				}
			}
			catch
			{
			}
			if (SafeGetObj(obj, "Children") is IEnumerable enumerable)
			{
				foreach (object item in enumerable)
				{
					if (item != null)
					{
						string name = item.GetType().Name;
						if (name.Contains("LayoutPanel", StringComparison.Ordinal) || name.Contains("Layout", StringComparison.Ordinal))
						{
							string value = SafeProp(item, "PositionOffset");
							string value2 = SafeProp(item, "Bounds");
							string value3 = SafeProp(obj, "ScrollBarAlignment");
							string value4 = SafeProp(obj, "ScrollBarThickness");
							string value5 = SafeProp(obj, "Padding");
							return $"layoff panel={name} pos={value} bounds={value2} {text} align={value3} thick={value4} pad={value5}";
						}
					}
				}
			}
			return "layoff=nopanel " + text;
		}
		catch
		{
			return "layoff=error";
		}
	}

	internal static string FormatPanoScrollState(object? panoramaControl)
	{
		try
		{
			if (!(panoramaControl is System.Windows.Forms.Control control))
			{
				return "pano-scroll n/a";
			}
			System.Windows.Forms.Form form = control.FindForm();
			string value = ((form == null) ? "form=null" : $"form={form.GetType().Name} bounds={form.Bounds} client={form.ClientSize} ws={form.WindowState} loc={form.Location}");
			object obj = SafeGetObj(control, "PanoramaElement");
			string value2 = ((obj == null) ? "sb=n/a" : $"sb=V{SafeSubProp(obj, "ScrollBar", "Value")}/M{SafeSubProp(obj, "ScrollBar", "Maximum")}/L{SafeSubProp(obj, "ScrollBar", "LargeChange")}/vis{SafeSubProp(obj, "ScrollBar", "Visibility")}");
			string value3 = $"auto={SafeProp(control, "AutoScroll")} vvis={SafeSubProp(control, "VerticalScroll", "Visible")} vval={SafeSubProp(control, "VerticalScroll", "Value")}/{SafeSubProp(control, "VerticalScroll", "Maximum")}";
			StringBuilder stringBuilder = new StringBuilder();
			try
			{
				if (form != null)
				{
					foreach (System.Windows.Forms.Control control2 in form.Controls)
					{
						if (stringBuilder.Length > 0)
						{
							stringBuilder.Append(' ');
						}
						stringBuilder.Append(control2.Name).Append('=').Append(control2.Bounds)
							.Append(control2.Visible ? "" : "?hid");
						if (stringBuilder.Length > 600)
						{
							break;
						}
					}
				}
			}
			catch
			{
			}
			string value4 = $"lq={WasmLayoutQueue.TotalPosted}/{WasmLayoutQueue.TotalDrained}/{WasmLayoutQueue.TotalFaulted}";
			return $"pano-scroll {value} pano={control.Name} bounds={control.Bounds} {value2} {value3} strips=[{stringBuilder}] {value4}";
		}
		catch
		{
			return "pano-scroll error";
		}
	}

	private static bool TryRenderSelfPainted(ICanvas canvas, System.Windows.Forms.Control c, int absX, int absY, int w, int h)
	{
		TracePanoPaint(c);
		if (w <= 0 || h <= 0 || w > 2048 || h > 2048)
		{
			return false;
		}
		Type type = c.GetType();
		if (!TypeSelfPaints(type) && c.BackgroundImage == null)
		{
			return false;
		}
		if (s_selfPaintPoisoned.ContainsKey(type))
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
		if (TrySkipPostShiftRepaint(canvas, c, absX, absY, w, h))
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
			bool perfEnabled = WasmFrameCounters.PerfEnabled;
			long num = (perfEnabled ? Stopwatch.GetTimestamp() : 0);
			long num2 = (WasmFrameCounters.PerfEnabled ? Stopwatch.GetTimestamp() : 0);
			EnsureElementLayout(c);
			if (num2 != 0)
			{
				WasmFrameCounters.AddEnsureLayoutTicks(Stopwatch.GetTimestamp() - num2);
			}
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
				long num3 = (WasmFrameCounters.PerfEnabled ? Stopwatch.GetTimestamp() : 0);
				TextRasterCounters.Snapshot snapshot = default;
				if (num3 != 0)
				{
					snapshot = TextRasterCounters.Peek();
				}
				LogFirstPaint(c);
				s_onPaintBackgroundMethod.Invoke(c, new object[1] { e });
				if (num3 != 0)
				{
					WasmFrameCounters.AddBgInvokeTicks(Stopwatch.GetTimestamp() - num3);
				}
				s_onPaintMethod.Invoke(c, new object[1] { e });
				if (num3 != 0)
				{
					long num4 = Stopwatch.GetTimestamp() - num3;
					WasmFrameCounters.AddPaintInvokeTicks(num4);
					long num5;
					do
					{
						num5 = s_slowTicks;
					}
					while (num4 > num5 && Interlocked.CompareExchange(ref s_slowTicks, num4, num5) != num5);
					if (num4 > num5)
					{
						s_slowType = c.GetType().Name;
						TextRasterCounters.Snapshot snapshot2 = TextRasterCounters.Peek();
						s_slowDraws = snapshot2.DrawCalls - snapshot.DrawCalls;
						s_slowPx = snapshot2.DrawPx - snapshot.DrawPx;
					}
				}
			}
			if (perfEnabled)
			{
				WasmFrameCounters.AddOnPaintTicks(Stopwatch.GetTimestamp() - num);
			}
			if (perfEnabled)
			{
				WasmFrameCounters.CountSelfPaint();
			}
			byte[] array = ExtractBitmapRgba(bitmap);
			if (array == null)
			{
				return false;
			}
			// Post-shift verify (WF-P-066): a carried shift's pixels may
			// already be on screen (browser shifted + patched). On a
			// whole-control repaint with stable scroll generation, compare
			// the fresh raster against the tagged shifted-surface hash:
			// identical => re-blit without upload (and restore the layer
			// hash); different => upload as today. NoInputMarks is supplied
			// conservatively true: per-mark provenance does not exist at the
			// gate, and any input-silent pixel change fails the hash compare
			// (uploads as today), so correctness never depends on the flag.
			bool tookEpochTag = TryTakePostShiftTag(c, out ShiftEpochTag epochTag, out byte[]? diagSurfacePostShift);
			if (tookEpochTag)
			{
				bool wholePost = !FrameDirtyRect(c, out var wrcPost) || !wrcPost.HasValue;
				bool haveGenPost = s_rteScrollWatch.TryGetGenerationSeq(c, out long genPost);
				bool stablePost = haveGenPost && genPost == epochTag.WatchGeneration;
				var postFacts = new PostShiftDrainFacts(
					wholePost, !s_frameGlobal, stablePost, NoInputMarks: true);
				bool attemptPostShift = ShouldAttemptPostShiftVerify(epochTag, postFacts);
				ICanvasRenderer? postLayersForVerify = canvas as ICanvasRenderer;
				bool layersOkPostShift = postLayersForVerify is { SupportsBitmapLayers: not false };
				ulong rasterHashPostShift = 0;
				bool hashEqPostShift = false;
				if (attemptPostShift)
				{
					if (WasmFrameCounters.PerfEnabled)
					{
						WasmFrameCounters.AddBytesHashed(array.Length);
					}
					if (layersOkPostShift)
					{
						rasterHashPostShift = HashBytes(array);
						hashEqPostShift = ShouldSuppressUpload(epochTag.ShiftHash, rasterHashPostShift);
					}
				}
				if (WasmFrameCounters.PerfEnabled)
				{
					string diagDiffPostShift = "-";
				if (attemptPostShift && layersOkPostShift && !hashEqPostShift && diagSurfacePostShift is not null && diagSurfacePostShift.Length == array.Length)
				{
					long diffBytesPostShift = 0;
					int diffBlocksPostShift = 0;
					long firstDiffPostShift = -1;
					long[] rowBandsPostShift = new long[10];
					long[] colBandsPostShift = new long[8];
					for (int slash = 0; slash < array.Length; slash += 4096)
					{
						int blen = Math.Min(4096, array.Length - slash);
						bool blockDiff = false;
						for (int bi = 0; bi < blen; bi++)
						{
							if (array[slash + bi] != diagSurfacePostShift[slash + bi]) { diffBytesPostShift++; blockDiff = true; if (firstDiffPostShift < 0) firstDiffPostShift = slash + bi; int px = (slash + bi) / 4; int y = px / w; int x = px % w; rowBandsPostShift[Math.Min(9, y * 10 / h)]++; colBandsPostShift[Math.Min(7, x * 8 / w)]++; }
						}
						if (blockDiff) diffBlocksPostShift++;
					}
					diagDiffPostShift = "diffBytes=" + diffBytesPostShift + "/" + array.Length + " blocks=" + diffBlocksPostShift + " first=0x" + firstDiffPostShift.ToString("X") + " rows=[" + string.Join(",", rowBandsPostShift) + "] cols=[" + string.Join(",", colBandsPostShift) + "]";
				string diagOffsetPostShift = "off=none";
				if (diagSurfacePostShift is not null && diagSurfacePostShift.Length == array.Length)
				{
					long bestOffDiff = long.MaxValue;
					int bestOx = 0;
					int bestOy = 0;
					System.Text.StringBuilder offTable = new System.Text.StringBuilder();
					for (int oy = -3; oy <= 3; oy++)
					{
						for (int ox = -3; ox <= 3; ox++)
						{
							long od = 0;
							int y0 = oy > 0 ? oy : 0;
							int y1 = oy > 0 ? h : h + oy;
							int x0 = ox > 0 ? ox : 0;
							int x1 = ox > 0 ? w : w + ox;
							for (int yy = y0; yy < y1; yy += 2)
							{
								int rowF = yy * w + x0;
								int rowT = (yy - oy) * w + (x0 - ox);
								for (int xx = 0; xx < x1 - x0; xx += 2)
								{
									int pf = (rowF + xx) * 4;
									int pt = (rowT + xx) * 4;
									int d0 = array[pf] - diagSurfacePostShift[pt];
									od += d0 < 0 ? -d0 : d0;
								}
							}
							if (ox != -3 || oy != -3) offTable.Append(",");
							offTable.Append(ox).Append(":").Append(oy).Append("=").Append(od);
							if (od < bestOffDiff) { bestOffDiff = od; bestOx = ox; bestOy = oy; }
						}
					}
					diagOffsetPostShift = "best=" + bestOx + "," + bestOy + " sad=" + bestOffDiff + " table=[" + offTable.ToString() + "]";
				}
				diagDiffPostShift += " " + diagOffsetPostShift;
				}
				Console.WriteLine("[EpochGate] DRAIN frame=" + WasmFrameCounters.FrameSeq + " ctrl=" + c.GetType().Name + " tag=hit tagGen=" + epochTag.WatchGeneration + " tagHash=" + epochTag.ShiftHash.ToString("X16") + " whole=" + wholePost + " rect=" + (wrcPost.HasValue ? wrcPost.Value.ToString() : "none") + " global=" + s_frameGlobal + " genPost=" + (haveGenPost ? genPost.ToString() : "nogen") + " attempt=" + attemptPostShift + " layers=" + layersOkPostShift + " hashEq=" + hashEqPostShift + " raster=" + rasterHashPostShift.ToString("X16") + " " + diagDiffPostShift);
				}
				if (attemptPostShift && layersOkPostShift && hashEqPostShift
					&& postLayersForVerify is not null)
					{
						string postId = LayerIdFor(c);
						lock (s_liveLayers)
						{
							s_liveLayers.Add(postId);
						}
						lock (s_layerHash)
						{
							s_layerHash[postId] = epochTag.ShiftHash;
							s_layerSize[postId] = (w, h);
							if (WasmFrameCounters.PerfEnabled)
							{
								WasmFrameCounters.CountLayerHashHit(true);
							}
						}
						postLayersForVerify.BlitBitmap(postId, absX, absY, w, h);
						StoreRetainedPaint(c, bitmap);
						if (WasmFrameCounters.PerfEnabled)
						{
							WasmFrameCounters.CountRasterFull((long)w * (long)h);
						}
						return true;
					}
			}
			else if (WasmFrameCounters.PerfEnabled)
			{
				Console.WriteLine("[EpochGate] DRAIN frame=" + WasmFrameCounters.FrameSeq + " ctrl=" + c.GetType().Name + " tag=miss global=" + s_frameGlobal);
			}
			RecordBitmap(c, canvas, array, w, h, absX, absY, w, h);
			if (WasmFrameCounters.PerfEnabled)
			{
				WasmFrameCounters.CountRasterFull((long)w * (long)h);
			}
			StoreRetainedPaint(c, bitmap);
			return true;
		}
		catch (Exception ex)
		{
			if (ex.InnerException is TypeLoadException || ex.InnerException is MissingMethodException || ex.InnerException is MissingMemberException)
			{
				s_selfPaintPoisoned.TryAdd(type, 0);
			}
			string text = type.FullName ?? type.Name;
			if (s_selfPaintLogged.TryAdd(text, 1))
			{
				string value = ((ex.InnerException == null) ? "none" : (ex.InnerException.GetType().FullName + ":" + ex.InnerException.Message + ((ex.InnerException is TypeLoadException ex2 && !string.IsNullOrEmpty(ex2.TypeName)) ? (" TYPENAME=" + ex2.TypeName) : "") + StackHead(ex.InnerException, 6) + " OSTACK=" + StackHead(ex, 14).Replace(" STACK=", "")));
				Console.WriteLine($"[WinFormsAppHost] Self-paint failed for {text}: TYPE={ex.GetType().FullName} MSG={ex.Message} site={SiteOf(ex)} INNER={value}");
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
		ElementLayoutMembers orAdd = s_layoutMembers.GetOrAdd(type, (Type type2) =>
		{
			PropertyInfo tree = type2.GetProperty("ElementTree", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			MethodInfo pump = tree?.PropertyType.GetMethod("PerformLayout", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
			if ((object)tree != null && (object)pump != null)
			{
				return new ElementLayoutMembers((System.Windows.Forms.Control obj3) => tree.GetValue(obj3), null, (object obj3) =>
				{
					pump.Invoke(obj3, null);
				});
			}
			PropertyInfo root = type2.GetProperty("RootElement", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			Type type3 = root?.PropertyType;
			PropertyInfo valid = type3?.GetProperty("IsArrangeValid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			PropertyInfo never = type3?.GetProperty("NeverArranged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			MethodInfo update = type3?.GetMethod("UpdateLayout", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			return ((object)root == null || (object)valid == null || (object)never == null || (object)update == null) ? null : new ElementLayoutMembers((System.Windows.Forms.Control obj3) => root.GetValue(obj3), (object obj3) => !(bool)(valid.GetValue(obj3) ?? ((object)true)) || (bool)(never.GetValue(obj3) ?? ((object)false)), (object obj3) =>
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
			byte[] array = bmp.ToRgbaBytes();
			if (WasmFrameCounters.PerfEnabled && array != null)
			{
				WasmFrameCounters.AddBytesExtracted(array.Length);
			}
			return array;
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
		if (owner != null && !IsFrameDirty(owner) && canvas is ICanvasRenderer { SupportsBitmapLayers: not false } canvasRenderer)
		{
			string text = LayerIdFor(bmp);
			bool flag = true;
			bool flag2;
			lock (s_layerHash)
			{
				flag2 = s_layerHash.ContainsKey(text);
				if (flag2 && s_layerSize.TryGetValue(text, out (int, int) value))
				{
					flag = value.Item1 == bmp.Width && value.Item2 == bmp.Height;
				}
			}
			if (flag2 & flag)
			{
				lock (s_liveLayers)
				{
					s_liveLayers.Add(text);
				}
				canvasRenderer.BlitBitmap(text, x + (w - (float)bmp.Width) / 2f, y + (h - (float)bmp.Height) / 2f, bmp.Width, bmp.Height);
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
			byte[] array = bmp.ToRgbaBytes();
			if (WasmFrameCounters.PerfEnabled && array != null)
			{
				WasmFrameCounters.AddBytesExtracted(array.Length);
			}
			return array;
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
			PropertyInfo[] array = properties;
			foreach (PropertyInfo propertyInfo in array)
			{
				if (propertyInfo.Name.EndsWith("ScaleRatio", StringComparison.Ordinal))
				{
					object value = propertyInfo.GetValue(activeDw);
					double value2;
					if (value is double num)
					{
						value2 = num;
					}
					else
					{
						value2 = ((value is float num2) ? ((double)num2) : 1.0);
					}
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
			PropertyInfo[] array = properties;
			foreach (PropertyInfo propertyInfo in array)
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
			PropertyInfo[] array = properties;
			foreach (PropertyInfo propertyInfo in array)
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
					PropertyInfo[] array = properties;
					foreach (PropertyInfo propertyInfo in array)
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
		System.Windows.Forms.Control control = FindControlByTypeName(_activeForm, "AppWorkspace");
		return control?.GetType().GetProperty("ActiveDocumentWorkspace", BindingFlags.Instance | BindingFlags.Public)?.GetValue(control);
	}

	private static System.Windows.Forms.Control? FindControlByTypeName(System.Windows.Forms.Control root, string typeName)
	{
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

	private static int GetDocumentLayerCount(object? activeDw)
	{
		try
		{
			object obj = activeDw?.GetType().GetProperty("Document", BindingFlags.Instance | BindingFlags.Public)?.GetValue(activeDw);
			object obj2 = obj?.GetType().GetProperty("Layers", BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj);
			if (obj2 is IList { Count: var count })
			{
				return count;
			}
		}
		catch
		{
		}
		return 1;
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
			object activeDocumentSurface = GetActiveDocumentSurface(activeDocumentWorkspaceFromActiveForm, out var docW, out var docH);
			if (activeDocumentSurface == null)
			{
				return "none";
			}
			object obj = activeDocumentSurface.GetType().GetProperty("Scan0", BindingFlags.Instance | BindingFlags.Public)?.GetValue(activeDocumentSurface);
			nint num = (nint)(obj?.GetType().GetProperty("Pointer", BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj) ?? ((object)IntPtr.Zero));
			if (num != IntPtr.Zero && (nuint)num > (nuint)65535u)
			{
				int num2 = 0;
				nint num3 = num;
				uint* ptr = (uint*)((IntPtr)num3).ToPointer();
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

	private unsafe static byte[]? ExtractSurfaceRgba(object surface, int w, int h, out int blankKind)
	{
		blankKind = 0;
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
			bool flag2 = true;
			bool flag3 = true;
			nint num4 = num;
			byte* ptr = (byte*)((IntPtr)num4).ToPointer();
			int num5 = w * h;
			uint* ptr2 = (uint*)ptr;
			for (int i = 0; i < num5; i++)
			{
				if (ptr2[i] != uint.MaxValue && ptr2[i] != 0)
				{
					flag = true;
					break;
				}
				if (ptr2[i] != uint.MaxValue)
				{
					flag2 = false;
				}
				if (ptr2[i] != 0)
				{
					flag3 = false;
				}
			}
			if (!flag)
			{
				blankKind = (flag2 ? 1 : (flag3 ? 2 : 0));
				return null;
			}
			byte[] array = new byte[w * h * 4];
			num4 = num;
			byte* ptr3 = (byte*)((IntPtr)num4).ToPointer();
			fixed (byte* ptr4 = array)
			{
				for (int j = 0; j < h; j++)
				{
					byte* ptr5 = ptr3 + j * num3;
					byte* ptr6 = ptr4 + j * w * 4;
					for (int k = 0; k < w; k++)
					{
						int num6 = k * 4;
						int num7 = k * 4;
						ptr6[num7] = ptr5[num6 + 2];
						ptr6[num7 + 1] = ptr5[num6 + 1];
						ptr6[num7 + 2] = ptr5[num6];
						ptr6[num7 + 3] = ptr5[num6 + 3];
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

	private unsafe static byte[]? ExtractCompositedRgba(object workspace, int w, int h)
	{
		try
		{
			object obj = workspace.GetType().GetProperty("DocumentCanvas", BindingFlags.Instance | BindingFlags.Public)?.GetValue(workspace);
			object obj2 = obj?.GetType().GetProperty("DocumentCanvasLayer", BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj);
			object obj3 = obj2?.GetType().GetProperty("DocumentRenderer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj2);
			obj3 = obj3 ?? obj2?.GetType().GetField("documentRenderer", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(obj2);
			object obj4 = obj3?.GetType().GetMethod("GetSnapshotBitmap", BindingFlags.Instance | BindingFlags.Public)?.Invoke(obj3, null);
			if (_compositedDiagCount < 3)
			{
				_compositedDiagCount++;
				Console.WriteLine($"[PaintDotNet] Composited diag #{_compositedDiagCount}: canvas={((obj == null) ? "null" : obj.GetType().Name)} layer={((obj2 == null) ? "null" : obj2.GetType().Name)} renderer={((obj3 == null) ? "null" : obj3.GetType().Name)} snapshot={((obj4 == null) ? "null" : obj4.GetType().Name)}");
			}
			if (obj4 == null || w <= 0 || h <= 0)
			{
				return null;
			}
			MethodInfo methodInfo = null;
			MethodInfo[] methods = obj4.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public);
			foreach (MethodInfo methodInfo2 in methods)
			{
				if (!(methodInfo2.Name != "CopyPixels"))
				{
					ParameterInfo[] parameters = methodInfo2.GetParameters();
					if (parameters.Length == 4 && parameters[0].ParameterType.IsPointer && parameters[1].ParameterType == typeof(int) && parameters[2].ParameterType == typeof(uint))
					{
						methodInfo = methodInfo2;
						break;
					}
				}
			}
			if (methodInfo == null)
			{
				return null;
			}
			byte[] array = new byte[w * h * 4];
			fixed (byte* ptr = array)
			{
				object obj5 = Pointer.Box(ptr, typeof(void).MakePointerType());
				methodInfo.Invoke(obj4, new object[4]
				{
					obj5,
					w * 4,
					(uint)(w * h * 4),
					null
				});
			}
			for (int j = 0; j < array.Length; j += 4)
			{
				byte b = array[j];
				array[j] = array[j + 2];
				array[j + 2] = b;
			}
			return array;
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
				MethodInfo[] array = methods;
				foreach (MethodInfo methodInfo2 in array)
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
					MethodInfo[] array2 = methods2;
					foreach (MethodInfo methodInfo3 in array2)
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
			Assembly[] array = assemblies;
			foreach (Assembly assembly in array)
			{
				if (assembly.GetName().Name == "PaintDotNet.Windows.Core")
				{
					Console.WriteLine("[D2DDBG2] core asm: " + assembly.FullName + " loc=" + assembly.Location);
				}
			}
			Console.WriteLine("[D2DDBG2] core scan done");
			bool flag = false;
			Assembly[] assemblies2 = AppDomain.CurrentDomain.GetAssemblies();
			Assembly[] array2 = assemblies2;
			foreach (Assembly assembly2 in array2)
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
		string[] array3 = new string[2] { "PaintDotNet.Primitives", "PaintDotNet.Windows.Core" };
		string[] array4 = array3;
		foreach (string assemblyString in array4)
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
		Assembly[] array5 = assemblies3;
		foreach (Assembly assembly3 in array5)
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
		Assembly[] array6 = assemblies4;
		foreach (Assembly assembly4 in array6)
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
			ConstructorInfo[] array7 = constructors;
			foreach (ConstructorInfo constructorInfo in array7)
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
			ConstructorInfo[] array8 = constructors2;
			foreach (ConstructorInfo constructorInfo2 in array8)
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
				Assembly[] array9 = assemblies5;
				Assembly[] array10 = array9;
				foreach (Assembly assembly5 in array10)
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
				FieldInfo[] array = fields;
				foreach (FieldInfo fieldInfo in array)
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
		// rev292c: LayersStrip renders through the CPU path now (its
		// blacklist predates the strip-composite + UI-upload fixes; a
		// blank strip is no longer an acceptable fail-closed shape).
		// ToleranceSliderControl stays blacklisted (unproven).
		lock (s_d2dRenderFailed)
		{
			s_d2dRenderFailed.Add("PaintDotNet.Controls.ToleranceSliderControl");
		}
		try
		{
			string text = c.GetType().FullName ?? "";
			if (text.Contains("Strip") && !text.Contains("DocumentStrip-NONE"))
			{
				Type type = c.GetType();
				FieldInfo fieldInfo = null;
				while (type != null && fieldInfo == null)
				{
					fieldInfo = type.GetField("drawShadow", BindingFlags.Instance | BindingFlags.NonPublic);
					type = type.BaseType;
				}
				if (fieldInfo != null && fieldInfo.FieldType == typeof(bool))
				{
					if (s_d2dRenderFailed.Add("noshadow:" + text))
					{
						Console.WriteLine("[NOSHADOW] " + text);
					}
					fieldInfo.SetValue(c, false);
				}
			}
		}
		catch
		{
		}
		if (!IsD2DControl(c) || (long)w * (long)h > 2000000)
		{
			return;
		}
		string text2 = c.GetType().FullName ?? "?";
		lock (s_d2dRenderFailed)
		{
			if (s_d2dRenderFailed.Contains(text2))
			{
				return;
			}
		}
		try
		{
			lock (s_d2dRenderFailed)
			{
				if (s_d2dRenderFailed.Add("try:" + text2))
				{
					Console.WriteLine("[D2DTRY] " + text2);
				}
			}
			lock (s_d2dRenderFailed)
			{
				if (s_d2dRenderFailed.Add("nulls:" + text2))
				{
					TempD2DNullProbe(c, text2);
				}
			}
			RenderD2DControlCore(canvas, c, absX, absY, w, h);
		}
		catch (Exception ex)
		{
			lock (s_d2dRenderFailed)
			{
				s_d2dRenderFailed.Add(text2);
			}
			Exception ex2 = ex;
			while (ex2 is TargetInvocationException && ex2.InnerException != null)
			{
				ex2 = ex2.InnerException;
			}
			string value = ex2.Message.Split('\n')[0];
			Console.WriteLine($"[D2D] {text2} CPU render failed: {ex2.GetType().Name}: {value}");
			try
			{
				StackFrame stackFrame = null;
				if (stackFrame != null)
				{
					Console.WriteLine($"[D2DIL] {text2} il=0x{stackFrame.GetILOffset():x}");
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
				string[] array3 = array2;
				foreach (string text3 in array3)
				{
					string text4 = text3.Trim();
					if (text4.Length != 0)
					{
						Console.WriteLine($"[D2DSTACK] {text2} #{num}: " + ((text4.Length > 220) ? text4.Substring(0, 220) : text4));
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
				string[] array2 = array;
				foreach (string text in array2)
				{
					type = assembly.GetType(text);
					string[] array3 = new string[3] { "PaintDotNet.Framework", "PaintDotNet.Core", "paintdotnet" };
					string[] array4 = array3;
					foreach (string text2 in array4)
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
						object[] array5 = new object[parameters.Length];
						for (int num = 0; num < parameters.Length; num++)
						{
							Type parameterType = parameters[num].ParameterType;
							if (parameterType.IsInstanceOfType(cpuCtx))
							{
								array5[num] = cpuCtx;
							}
							else if (parameterType == typeof(bool))
							{
								array5[num] = true;
							}
							else if (parameterType.IsValueType)
							{
								array5[num] = Activator.CreateInstance(parameterType);
							}
							else
							{
								array5[num] = null;
							}
						}
						methodInfo.Invoke(null, array5);
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
				string[] array6 = (ex3.StackTrace ?? "").Split('\n');
				string[] array7 = array6;
				foreach (string text3 in array7)
				{
					string text4 = text3.Trim();
					if (text4.Length != 0)
					{
						Console.WriteLine("[D2DDEEPST] " + tn + " " + text4.Substring(0, Math.Min(200, text4.Length)));
					}
				}
			}
			(string, string)[] array8 = new (string, string)[2]
			{
				("checkers", "CheckerboardEffect"),
				("colormgmt", "ColorManagementEffect")
			};
			for (int num3 = 0; num3 < array8.Length; num3++)
			{
				(string, string) tuple = array8[num3];
				try
				{
					Type type2 = null;
					string[] array9 = new string[2]
					{
						"PaintDotNet.VisualStyling." + tuple.Item2,
						"PaintDotNet.Direct2D1.Effects." + tuple.Item2
					};
					string[] array10 = array9;
					foreach (string text5 in array10)
					{
						type2 = assembly.GetType(text5);
						string[] array11 = new string[4] { "PaintDotNet.Core", "PaintDotNet.Windows.Core", "PaintDotNet.Framework", "paintdotnet" };
						string[] array12 = array11;
						foreach (string text6 in array12)
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
				string[] array13 = new string[4] { "PaintDotNet.Windows.Core", "PaintDotNet.Core", "PaintDotNet.Framework", "paintdotnet" };
				string[] array14 = array13;
				foreach (string text7 in array14)
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
						if (methodInfo2 == null)
						{
							text8 = "no-getter";
						}
						else
						{
							text8 = ((methodInfo2.Invoke(obj8, null) == null) ? "null" : "ok");
						}
					}
					catch (Exception ex6)
					{
						text8 = "THROW:" + ex6.GetType().Name;
					}
					try
					{
						MethodInfo methodInfo3 = obj8?.GetType().GetMethod("get_Factory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						if (methodInfo3 == null)
						{
							text9 = "no-getter";
						}
						else
						{
							text9 = ((methodInfo3.Invoke(obj8, null) == null) ? "null" : "ok");
						}
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
				if (methodInfo == null)
				{
					text = "no-getter";
				}
				else
				{
					text = ((methodInfo.Invoke(obj, null) == null) ? "null" : "ok");
				}
			}
			catch (Exception ex2)
			{
				text = "THROW:" + ex2.GetType().Name;
			}
			try
			{
				MethodInfo methodInfo2 = obj?.GetType().GetMethod("get_Factory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (methodInfo2 == null)
				{
					text2 = "no-getter";
				}
				else
				{
					text2 = ((methodInfo2.Invoke(obj, null) == null) ? "null" : "ok");
				}
			}
			catch (Exception ex3)
			{
				text2 = "THROW:" + ex3.GetType().Name;
			}
			try
			{
				FieldInfo fieldInfo = value.GetType().Assembly.GetType("PaintDotNet.Direct2D1.Effects.DeviceEffect")?.GetField("effect", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				object obj2 = fieldInfo?.GetValue(value);
				if (fieldInfo == null)
				{
					text3 = "no-field";
				}
				else
				{
					text3 = ((obj2 == null) ? "null" : obj2.GetType().FullName);
				}
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
		int num = checked(w * h * 4);
		byte[] array = new byte[num + 64];
		for (int i = 0; i < 64; i++)
		{
			array[num + i] = 171;
		}
		GCHandle gCHandle = GCHandle.Alloc(array, GCHandleType.Pinned);
		try
		{
			long num2 = CpuPaint.CreateTarget(gCHandle.AddrOfPinnedObject(), w, h, w * 4, 4);
			if (num2 == 0)
			{
				throw new InvalidOperationException("CreateTarget failed");
			}
			object obj = null;
			try
			{
				CpuPaint.ClearTarget(num2, 0f, 0f, 0f, 0f, hasColor: false);
				obj = (((object)s_cpuCtxCtor != null) ? s_cpuCtxCtor.Invoke(new object[1] { num2 }) : Activator.CreateInstance(s_cpuCtxType, num2));
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
					bool flag = false;
					for (int j = 0; j < 64; j++)
					{
						if (array[num + j] != 171)
						{
							flag = true;
							break;
						}
					}
					lock (s_d2dRenderFailed)
					{
						if (s_d2dRenderFailed.Add("canary:" + text))
						{
							Console.WriteLine("[D2DCANARY] " + text + " " + w + "x" + h + (flag ? " TRASHED" : " ok"));
						}
					}
				}
				finally
				{
					(obj as IDisposable)?.Dispose();
				}
			}
			finally
			{
				if (obj == null)
				{
					CpuPaint.FreeContext(num2);
				}
			}
		}
		finally
		{
			gCHandle.Free();
		}
		for (int k = 0; k < array.Length; k += 4)
		{
			byte b = array[k];
			array[k] = array[k + 2];
			array[k + 2] = b;
		}
		RecordBitmap(c, canvas, array, w, h, absX, absY, w, h);
		if (WasmFrameCounters.PerfEnabled)
		{
			WasmFrameCounters.CountRasterFull((long)w * (long)h);
		}
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
		bool flag = false;
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
		int num2 = num;
		bool flag2 = false;
		int item2 = num2;
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
				(int, int) tuple = ToPointerButtonInts(buttons, kind);
				int item = tuple.Item1;
				int item2 = tuple.Item2;
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