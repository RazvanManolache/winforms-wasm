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

public sealed class WinFormsAppHost
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

	private static readonly ConcurrentDictionary<Type, ElementLayoutMembers?> s_layoutMembers = new ConcurrentDictionary<Type, ElementLayoutMembers>();

	private static readonly MethodInfo? s_onClickMethod = typeof(System.Windows.Forms.Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static readonly MethodInfo? s_onMouseDownMethod = typeof(System.Windows.Forms.Control).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static readonly MethodInfo? s_onMouseMoveMethod = typeof(System.Windows.Forms.Control).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static readonly MethodInfo? s_onMouseUpMethod = typeof(System.Windows.Forms.Control).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static readonly MethodInfo? s_onMouseEnterMethod = typeof(System.Windows.Forms.Control).GetMethod("OnMouseEnter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static readonly MethodInfo? s_onMouseLeaveMethod = typeof(System.Windows.Forms.Control).GetMethod("OnMouseLeave", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static readonly MethodInfo? s_itemMouseEnterMethod = typeof(System.Windows.Forms.ToolStripItem).GetMethod("OnMouseEnter", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly MethodInfo? s_itemMouseMoveMethod = typeof(System.Windows.Forms.ToolStripItem).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly MethodInfo? s_itemMouseLeaveMethod = typeof(System.Windows.Forms.ToolStripItem).GetMethod("OnMouseLeave", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly MethodInfo? s_itemUnselectMethod = typeof(System.Windows.Forms.ToolStripItem).GetMethod("Unselect", BindingFlags.Instance | BindingFlags.NonPublic);

	private readonly Dictionary<AppScene, System.Windows.Forms.Form> _forms = new Dictionary<AppScene, System.Windows.Forms.Form>();

	private readonly HashSet<System.Windows.Forms.Form> _autoCentered = new HashSet<System.Windows.Forms.Form>();

	private readonly HashSet<System.Windows.Forms.Form> _userPlaced = new HashSet<System.Windows.Forms.Form>();

	private System.Windows.Forms.Form? _activeForm;

	private System.Windows.Forms.TextBox? _focusedTextBox;

	private System.Windows.Forms.Control? _pressedButton;

	private System.Windows.Forms.Control? _mouseTarget;

	private System.Windows.Forms.MouseButtons _mouseButtons = System.Windows.Forms.MouseButtons.Left;

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

	public int ViewWidth { get; set; } = 1200;

	public int ViewHeight { get; set; } = 950;

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

	public WinFormsAppHost()
	{
		InitializeAppDomainResolver();
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
	private static int s_globalFirstChanceCount;

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

	private static System.Windows.Forms.Form? CreateAppForm(string typeName, string assemblyName)
	{
		try
		{
			Type? type = Type.GetType(typeName + ", " + assemblyName);
			type ??= AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault((Assembly a) => string.Equals(a.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))?.GetType(typeName);
			if (type == null)
			{
				Console.WriteLine($"[WinFormsAppHost] Form type not found: {typeName} in {assemblyName}");
				return null;
			}
			Exception? firstChance = null;
		void FirstChanceHandler(object? sender, FirstChanceExceptionEventArgs args) => firstChance ??= args.Exception;
		AppDomain.CurrentDomain.FirstChanceException += FirstChanceHandler;
		try
		{
			return (System.Windows.Forms.Form)Activator.CreateInstance(type);
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
			string site = SiteChain(ex);
			Console.WriteLine($"[WinFormsAppHost] Error creating {typeName}: {ex.GetType().Name}: {ex.Message.Split('\n')[0]} site={site}");
			if (s_lastFirstChance is not null && !ReferenceEquals(s_lastFirstChance, RootCause(ex)))
				Console.WriteLine($"[WinFormsAppHost] First-chance during create: {s_lastFirstChance.GetType().Name} site={SiteOf(s_lastFirstChance)} msg={(s_lastFirstChance.Message ?? "").Split('\n')[0]}");
			s_lastFirstChance = null;
			BrowserErrorReporter.Report("form-create", ex, SiteOf(RootCause(ex)));
			return null;
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
				form = CreateAppForm("ToastNotificationManagerExamples.Form1", "ToastNotificationManagerExamples");
				break;
			}
			if (form != null)
			{
				form.Visible = true;
				try
				{
					typeof(System.Windows.Forms.Form).GetMethod("OnLoad", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(form, new object[1] { EventArgs.Empty });
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[WinFormsAppHost] Error invoking OnLoad for {scene}: {ex}");
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
			Console.WriteLine($"[WinFormsAppHost] Error creating form for {scene}: {ex2}");
			BrowserErrorReporter.Report("form-create", ex2);
		}
		return form;
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
			CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
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
			if (!string.IsNullOrEmpty(ex2.StackTrace))
			{
				Console.WriteLine(prefix + " StackTrace: " + ex2.StackTrace);
			}
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

	private void CenterInViewport(System.Windows.Forms.Form form)
	{
		int num = Math.Max(form.Width, form.ClientSize.Width);
		int num2 = Math.Max(form.Height, form.ClientSize.Height);
		int num3 = Math.Max(300, (num > 0) ? num : 600);
		int num4 = Math.Max(200, (num2 > 0) ? num2 : 450);
		int x = Math.Max(10, (ViewWidth - num3) / 2);
		int y = Math.Max(10, (ViewHeight - num4) / 2);
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

	public void SelectScene(AppScene scene)
	{
		System.Windows.Forms.WasmRepaintGate.MarkGlobalDirty();
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
		_focusedTextBox = null;
		_pressedButton = null;
		_mouseTarget = null;
		_isDraggingTitle = false;
		_isDrawingOnDocument = false;
		if (!s_globalFirstChanceHooked)
		{
			// TEMP-VFS: global first-chance trace to catch swallowed load
			// exceptions (BookShelf's DeserializeData swallows everything).
			// REMOVE after triage (durable-test rule: no temp probes ship).
			s_globalFirstChanceHooked = true;
			AppDomain.CurrentDomain.FirstChanceException += (sender, args) =>
			{
				try
				{
					Exception ex = args.Exception;
					string msg = (ex.Message ?? "").Split('\n')[0];
					if (msg.Contains("UITypeEditor") || msg.Contains("System.Diagnostics.Process"))
						return;
					if (s_globalFirstChanceCount >= 80)
						return;
					System.Threading.Interlocked.Increment(ref s_globalFirstChanceCount);
					string stack = System.Environment.StackTrace ?? string.Empty;
					string[] frames = stack.Split('\n');
					System.Text.StringBuilder sb = new();
					int kept = 0;
					foreach (string raw in frames)
					{
						string fr = raw.Trim();
						if (fr.Contains("FirstChance") || fr.Contains("WinFormsAppHost") || fr.Length == 0)
							continue;
						if (fr.Length > 170) fr = fr.Substring(0, 170);
						if (sb.Length > 0) sb.Append(" <- ");
						sb.Append(fr);
						if (++kept >= 8) break;
					}
					string top = sb.ToString();
					Console.WriteLine($"[1ST-GLOBAL] {ex.GetType().FullName}: {msg} site={ex.TargetSite?.DeclaringType?.FullName}.{ex.TargetSite?.Name} chain={top}");
				}
				catch
				{
				}
			};
		}
		_activeForm = GetOrCreateForm(scene);
		Console.WriteLine($"[Scene] Selected {scene}, Form={_activeForm?.GetType().FullName}, Size={_activeForm?.Size}, ClientSize={_activeForm?.ClientSize}, Controls={_activeForm?.Controls.Count}");
		if (_activeForm != null)
		{
			DumpControlTree(_activeForm, 0);
		}
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

	public void Paint(ICanvas canvas)
	{
		ArgumentNullException.ThrowIfNull(canvas, "canvas");
		// Dirty-gated frame: drain once; a fully-idle frame (no app
		// Invalidate, no host marking) emits nothing (RepaintAsync skips
		// the interop round trip for empty command lists).
		s_frameGlobal = System.Windows.Forms.WasmRepaintGate.DrainDirty(out var frameDirty);
		s_frameDirty = frameDirty;
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
		int num = 30;
		int x = _activeForm.Location.X;
		int y = _activeForm.Location.Y;
		int width = _activeForm.Width;
		int height = _activeForm.Height;
		uint value = ToRgba(_activeForm.BackColor, 4042322175u);
		int paintLog = System.Threading.Interlocked.Increment(ref s_paintLogCount);
		if (paintLog <= 3 || paintLog % 64 == 0)
			Console.WriteLine($"[Paint] Form: X={x}, Y={y}, W={width}, H={height}, BackColor={_activeForm.BackColor.Name} (0x{value:X8}), Controls={_activeForm.Controls.Count}");
		canvas.FillRectangle(x + 4, y + 4, width, height + num, 64u);
		canvas.FillGradientVertical(x, y, width, num, 7919615u, 5938943u);
		canvas.DrawTextAligned(_activeForm.Text ?? "Window", x + 10, y + num / 2, "bold 13px 'Segoe UI', sans-serif", uint.MaxValue, TextHAlign.Left, TextVAlign.Middle);
		int num2 = x + width - 28;
		int num3 = y + 6;
		canvas.FillRectangle(num2, num3, 18f, 18f, 3893437439u);
		canvas.DrawTextAligned("×", num2 + 9, num3 + 9, "bold 14px sans-serif", uint.MaxValue, TextHAlign.Center, TextVAlign.Middle);
		value = ToRgba(_activeForm.BackColor, 4042322175u);
		int num4 = y + num;
		canvas.FillRectangle(x, num4, width, height, value);
		RenderControls(canvas, _activeForm.Controls, x, num4);
		RenderShownWindows(canvas);
		RenderOpenDropdowns(canvas);
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
			bool flag;
			lock (s_layerHash)
			{
				flag = s_layerHash.TryGetValue(text, out var value) && value == num;
				s_layerHash[text] = num;
				s_layerSize[text] = (w, h);
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
		foreach (var (toolStripDropDown, num, num2) in GetOpenDropdowns())
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
				canvas.DrawTextAligned("✓", num + 12, (float)num2 + (float)item.Height / 2f, "12px sans-serif", rgba, TextHAlign.Center, TextVAlign.Middle);
			}
			canvas.DrawTextAligned(text, num + 28, (float)num2 + (float)item.Height / 2f, "12px 'Segoe UI', sans-serif", rgba, TextHAlign.Left, TextVAlign.Middle);
			if (item is System.Windows.Forms.ToolStripMenuItem toolStripMenuItem2 && !string.IsNullOrEmpty(toolStripMenuItem2.ShortcutKeyDisplayString))
			{
				canvas.DrawTextAligned(toolStripMenuItem2.ShortcutKeyDisplayString, num + item.Width - 8, (float)num2 + (float)item.Height / 2f, "11px 'Segoe UI', sans-serif", 1431655935u, TextHAlign.Right, TextVAlign.Middle);
			}
			else if (item is System.Windows.Forms.ToolStripMenuItem { HasDropDownItems: not false })
			{
				canvas.DrawTextAligned("▶", num + item.Width - 12, (float)num2 + (float)item.Height / 2f, "9px sans-serif", rgba, TextHAlign.Center, TextVAlign.Middle);
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
				if (form.Width <= 0 || form.Height <= 0)
				{
					form.Size = new Size(Math.Max(form.Width, 360), Math.Max(form.Height, 240));
				}
				if (form.Location.X == 0 && form.Location.Y == 0)
				{
					form.Location = new Point(Math.Max(0, (ViewWidth - form.Width) / 2), Math.Max(0, (ViewHeight - form.Height - 30) / 2));
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
					if (num7 + num5 + 30 > ViewHeight)
					{
						num7 = Math.Max(0, ViewHeight - num5 - 30);
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
			canvas.FillRectangle(x + 4, y + 4, width, height + 30, 96u);
			canvas.FillGradientVertical(x, y, width, 30f, 7919615u, 5938943u);
			canvas.DrawTextAligned(form.Text ?? "Dialog", x + 10, y + 15, "bold 13px 'Segoe UI', sans-serif", uint.MaxValue, TextHAlign.Left, TextVAlign.Middle);
			int num8 = x + width - 28;
			int num9 = y + 6;
			canvas.FillRectangle(num8, num9, 18f, 18f, 3893437439u);
			canvas.DrawTextAligned("x", num8 + 9, num9 + 9, "bold 14px sans-serif", uint.MaxValue, TextHAlign.Center, TextVAlign.Middle);
			uint rgba = ToRgba(form.BackColor, 4042322175u);
			canvas.FillRectangle(x, y + 30, width, height, rgba);
			RenderControls(canvas, form.Controls, x, y + 30);
		}
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
				if (x >= x2 && x <= x2 + form.Width && y >= y2 && y <= y2 + 30 + form.Height)
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
		if (x >= x2 && x <= x2 + dlg.Width && y >= y2 && y < y2 + 30)
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
		_mouseTarget = HitTestDeep(dlg.Controls, dlg.Location.X, dlg.Location.Y + 30, x, y);
		if (_mouseTarget is System.Windows.Forms.TextBox focusedTextBox)
		{
			_focusedTextBox = focusedTextBox;
		}
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

	private void ModalOnClick(System.Windows.Forms.Form modal, int x, int y, string tag = "modal")
	{
		int x2 = modal.Location.X;
		int y2 = modal.Location.Y;
		if (x >= x2 + modal.Width - 28 && x <= x2 + modal.Width - 10 && y >= y2 + 6 && y <= y2 + 24)
		{
			RecordInput("click", $"{x},{y} {tag}-close dlg={modal.Name}");
			try
			{
				modal.Close();
				return;
			}
			catch (Exception ex)
			{
				BrowserErrorReporter.Report("dialog-close", ex);
				return;
			}
		}
		System.Windows.Forms.Control control = HitTestDeep(modal.Controls, x2, y2 + 30, x, y);
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
				button.PerformClick();
				CloseModalIfSettled(modal);
				return;
			}
			catch (Exception ex2)
			{
				Console.WriteLine($"[WinFormsAppHost] Modal button click error: {ex2}");
				BrowserErrorReporter.Report("button-click", ex2);
				return;
			}
		}
		if (control is System.Windows.Forms.TextBox textBox)
		{
			_focusedTextBox = textBox;
			RecordInput("click", $"{x},{y} {tag}-textbox={textBox.Name}");
			return;
		}
		if (control is System.Windows.Forms.ToolStrip toolStrip)
		{
			int controlAbsX = GetControlAbsX(toolStrip, x2);
			int controlAbsY = GetControlAbsY(toolStrip, y2 + 30);
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
				canvas.DrawTextAligned(text, num + 5, (float)num2 + (float)height / 2f, fontCss2, rgba5, TextHAlign.Left, TextVAlign.Middle);
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
				for (int k = 0; k < listView.Items.Count; k++)
				{
					if (num5 + num6 > num2 + height)
					{
						break;
					}
					System.Windows.Forms.ListViewItem listViewItem = listView.Items[k];
					if (listViewItem.Selected)
					{
						canvas.FillRectangle(num + 1, num5, width - 2, num6, 865730559u);
						canvas.DrawTextAligned(listViewItem.Text, num + 6, (float)num5 + (float)num6 / 2f, "12px sans-serif", uint.MaxValue, TextHAlign.Left, TextVAlign.Middle);
					}
					else
					{
						canvas.DrawTextAligned(listViewItem.Text, num + 6, (float)num5 + (float)num6 / 2f, "12px sans-serif", 286331391u, TextHAlign.Left, TextVAlign.Middle);
					}
					num5 += num6;
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
						canvas.DrawTextAligned("▾", num7 + width3 - 6, (float)num8 + (float)height2 / 2f, "9px sans-serif", 1145324799u, TextHAlign.Center, TextVAlign.Middle);
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
						byte[] array = ExtractSurfaceRgba(activeDocumentSurface, docW, docH);
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
		}
	}

	public void OnMouseDown(int x, int y, System.Windows.Forms.MouseButtons buttons = System.Windows.Forms.MouseButtons.Left)
	{
		WasmDialogHost.AdvanceTurn();
		Console.WriteLine($"[Host] OnMouseDown(x={x}, y={y}, buttons={buttons})");
        System.Windows.Forms.WasmMouseState.Update(x, y, buttons);
		if (_activeForm == null)
		{
			return;
		}
		_mouseButtons = buttons;
		if (TopModalWindow(out System.Windows.Forms.Form modal) && modal != null)
		{
			if (!BeginDialogDrag(modal, x, y, "modal"))
			{
				RecordInput("down", $"{x},{y} {buttons} modal={modal.Name}");
				_mouseTarget = HitTestDeep(modal.Controls, modal.Location.X, modal.Location.Y + 30, x, y);
				System.Windows.Forms.WasmRepaintGate.MarkDirtyChain(_mouseTarget);
				if (_mouseTarget is System.Windows.Forms.TextBox focusedTextBox)
				{
					_focusedTextBox = focusedTextBox;
				}
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
		int num = 30;
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
			if (control is System.Windows.Forms.Button pressedButton)
			{
				_pressedButton = pressedButton;
			}
			else if (control is System.Windows.Forms.TextBox focusedTextBox2)
			{
				_focusedTextBox = focusedTextBox2;
			}
			else
			{
				_focusedTextBox = null;
			}
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

	public void OnMouseUp(int x, int y, System.Windows.Forms.MouseButtons buttons = System.Windows.Forms.MouseButtons.Left)
	{
		WasmDialogHost.AdvanceTurn();
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
		_mouseButtons = buttons;
		if (_mouseTarget != null)
		{
			System.Windows.Forms.WasmRepaintGate.MarkDirtyChain(_mouseTarget);
			RaiseMouseEvent(s_onMouseUpMethod, _mouseTarget, buttons, 1, x, y);
			_mouseTarget = null;
		}
		System.Windows.Forms.WasmMouseState.Update(x, y, _mouseButtons & ~buttons);
        RecordInput("up", isDrawingOnDocument ? $"{x},{y} {buttons} doc" : $"{x},{y} {buttons}");
		UpdateHover(x, y);
	}

	public void OnMouseMove(int x, int y)
	{
		WasmDialogHost.AdvanceTurn();
		if (_isDrawingOnDocument)
		{
			System.Windows.Forms.WasmMouseState.Update(x, y, _mouseButtons);
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
			System.Windows.Forms.WasmMouseState.Update(x, y, _mouseButtons);
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
		int ay = GetControlAbsY(target, _activeForm.Location.Y + 30);
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
		int num = y2 + 30;
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
			System.Windows.Forms.Control control = HitTestDeep(win.Controls, win.Location.X, win.Location.Y + 30, x, y);
			if (control is System.Windows.Forms.ToolStrip toolStrip)
			{
				int controlAbsX = GetControlAbsX(toolStrip, win.Location.X);
				int controlAbsY = GetControlAbsY(toolStrip, win.Location.Y + 30);
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
			System.Windows.Forms.Control control2 = HitTestDeep(modal.Controls, modal.Location.X, modal.Location.Y + 30, x, y);
			if (control2 is System.Windows.Forms.ToolStrip toolStrip2)
			{
				int controlAbsX2 = GetControlAbsX(toolStrip2, modal.Location.X);
				int controlAbsY2 = GetControlAbsY(toolStrip2, modal.Location.Y + 30);
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
		try
		{
			method.Invoke(target, new object[1] { EventArgs.Empty });
		}
		catch (Exception ex)
		{
			Console.WriteLine("[WinFormsAppHost] Mouse enter/leave error: " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]);
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

	private void RaiseMouseEvent(MethodInfo? method, System.Windows.Forms.Control target, System.Windows.Forms.MouseButtons buttons, int clicks, int x, int y)
	{
		if (method == null || _activeForm == null)
		{
			return;
		}
		try
		{
			int controlAbsX = GetControlAbsX(target, _activeForm.Location.X);
			int controlAbsY = GetControlAbsY(target, _activeForm.Location.Y + 30);
			System.Windows.Forms.MouseEventArgs e = new System.Windows.Forms.MouseEventArgs(buttons, clicks, x - controlAbsX, y - controlAbsY, 0);
			method.Invoke(target, new object[1] { e });
		}
		catch (Exception ex)
		{
			Console.WriteLine("[WinFormsAppHost] Mouse event error: " + ex.GetType().Name + ": " + ex.Message);
		}
	}

	public void OnClick(int x, int y)
	{
		WasmDialogHost.AdvanceTurn();
		Console.WriteLine($"[Host] OnClick(x={x}, y={y})");
		System.Windows.Forms.WasmRepaintGate.MarkGlobalDirty();
		if (_activeForm == null || ClickOpenDropdown(x, y))
		{
			return;
		}
		if (TopModalWindow(out System.Windows.Forms.Form modal) && modal != null)
		{
			ModalOnClick(modal, x, y);
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
		int num = 30;
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
				return;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[WinFormsAppHost] Button click error: {ex}");
				BrowserErrorReporter.Report("button-click", ex);
				return;
			}
		}
		if (control is System.Windows.Forms.ListView listView)
		{
			int num3 = 24;
			int num4 = 22;
			int num5 = y - (GetControlAbsY(listView, num2) + num3);
			if (num5 >= 0)
			{
				int num6 = num5 / num4;
				if (num6 >= 0 && num6 < listView.Items.Count)
				{
					System.Windows.Forms.ListViewItem listViewItem = listView.Items[num6];
					RecordInput("click", $"{x},{y} listview={listView.Name} row={num6}");
					listViewItem.Selected = !listViewItem.Selected;
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
			s_onClickMethod?.Invoke(control, new object[1] { EventArgs.Empty });
		}
		catch (Exception ex2)
		{
			Console.WriteLine("[WinFormsAppHost] Control click error: " + ex2.GetType().Name + ": " + ex2.Message.Split('\n')[0]);
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
		try
		{
			item.PerformClick();
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[WinFormsAppHost] ToolStripItem click: {ex}");
			BrowserErrorReporter.Report("toolstrip-click", ex);
		}
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
		foreach (var openDropdown in GetOpenDropdowns())
		{
			System.Windows.Forms.ToolStripDropDown item = openDropdown.Drop;
			try
			{
				foreach (System.Windows.Forms.ToolStripItem item2 in item.Items)
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

	public void OnKeyDown(string key)
	{
		WasmDialogHost.AdvanceTurn();
		RecordInput("key", key + " focused=" + DescribeControl(_focusedTextBox));
		System.Windows.Forms.WasmRepaintGate.MarkGlobalDirty();
		if (_focusedTextBox == null)
		{
			return;
		}
		switch (key)
		{
		case "Backspace":
			if (_focusedTextBox.Text.Length > 0)
			{
				System.Windows.Forms.TextBox? focusedTextBox = _focusedTextBox;
				string text = _focusedTextBox.Text;
				focusedTextBox.Text = text.Substring(0, text.Length - 1);
			}
			break;
		case "Escape":
		{
			if (TopModalWindow(out System.Windows.Forms.Form modal2) && modal2 != null)
			{
				try
				{
					modal2.Close();
					break;
				}
				catch (Exception ex)
				{
					BrowserErrorReporter.Report("dialog-close", ex);
					break;
				}
			}
			break;
		}
		case "Enter":
		{
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
			break;
		}
		default:
			if (key.Length == 1)
			{
				_focusedTextBox.Text += key;
			}
			break;
		}
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
			return "•";
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
			return "✂";
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
			return "✂";
		}
		if (text.Contains("deselect"))
		{
			return "✕";
		}
		if (text.Contains("delete"))
		{
			return "\ud83d\uddd1";
		}
		if (text.Contains("undo"))
		{
			return "↶";
		}
		if (text.Contains("redo"))
		{
			return "↷";
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
			return "⚙";
		}
		if (text.Contains("help"))
		{
			return "❓";
		}
		return "▪";
	}

	private static string GetFontCss(Font? font)
	{
		if (font == null)
		{
			return "12px 'Segoe UI', sans-serif";
		}
		string value = (font.Bold ? "bold " : "");
		string value2 = (font.Italic ? "italic " : "");
		int value3 = Math.Max(9, (int)font.SizeInPoints);
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
		RecordBitmapPatch(canvas, id, after, rc, absX, absY, w, h);
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
		}
		old?.Dispose();
	}
	
	private static bool TryRenderSelfPainted(ICanvas canvas, System.Windows.Forms.Control c, int absX, int absY, int w, int h)
	{
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
            EnsureElementLayout(c);
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
				s_onPaintBackgroundMethod.Invoke(c, new object[1] { e });
				s_onPaintMethod.Invoke(c, new object[1] { e });
			}
            byte[] array = ExtractBitmapRgba(bitmap);
			if (array == null)
			{
				return false;
			}
			RecordBitmap(c, canvas, array, w, h, absX, absY, w, h);
			StoreRetainedPaint(c, bitmap);
			return true;
		}
		catch (Exception ex)
		{
			string text = type.FullName ?? type.Name;
			if (s_selfPaintLogged.TryAdd(text, 1))
			{
				Console.WriteLine($"[WinFormsAppHost] Self-paint failed for {text}: TYPE={ex.GetType().FullName} MSG={ex.Message} ST={ex.StackTrace} INNER={ex.InnerException?.GetType().FullName}:{ex.InnerException?.Message} IST={ex.InnerException?.StackTrace}");
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
			return bmp.ToRgbaBytes();
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
			return bmp.ToRgbaBytes();
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
		foreach (System.Windows.Forms.Control control in _activeForm.Controls)
		{
			if (control.GetType().Name == "AppWorkspace")
			{
				return control.GetType().GetProperty("ActiveDocumentWorkspace", BindingFlags.Instance | BindingFlags.Public)?.GetValue(control);
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
