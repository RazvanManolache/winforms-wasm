using WfWasm.Analyzer;
using WfWasm.Packager;
using WfWasm.Renderer;
using WfWasm.Shim;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: WfWasm.SmokeDemo <app.dll> <out-dir>");
    return 2;
}

string appDll = args[0];
string outDir = args[1];

// Stage 1: analyze the unmodified app.
ScanResult scan = AssemblyScanner.Scan(appDll);
Console.WriteLine($"assembly-refs: {scan.AssemblyReferences.Count}");
int shimRefs = scan.TypeReferences
    .Where(t => t.FullName.StartsWith("System.Windows.Forms", StringComparison.Ordinal)
        || t.FullName.StartsWith("System.Drawing", StringComparison.Ordinal))
    .Sum(t => t.Count);
Console.WriteLine($"forms-drawing-typerefs: {shimRefs}");
Console.WriteLine($"pinvoke: {(scan.PInvokeModules.Count == 0 ? "(none)" : string.Join(", ", scan.PInvokeModules))}");

// Stage 4 (plan first): warn before building.
PackageManifest manifest = Packager.CreatePlan(appDll);
foreach (string warning in manifest.Warnings)
    Console.WriteLine($"warning: {warning}");

// Stage 2: mirror the smoke form (Form1, 800x450, "Hello .NET" label) as a dialog page.
// Stock System.Windows.Forms controls need the future Control-tree shim;
// the content (title + label) is what the baseline actually shows.
var page = new DialogPageModel { Heading = "Form1", Text = "Hello .NET" };
page.Buttons.Add(new DialogButtonModel("ok", "OK") { IsDefault = true });
var stack = new ModalDialogStack<DialogPageModel, string>();
Task<string> pending = stack.ShowAsync(page);

// Stage 3: render the overlay and persist it.
var svg = new SvgRenderer();
DialogOverlayPainter.Paint(svg, stack.Top, "Form1");
Directory.CreateDirectory(outDir);
File.WriteAllText(Path.Combine(outDir, "smoke.svg"), svg.GetSvg());

// Simulate the OK click, then stage the app files.
DialogClickOutcome click = page.ClickButton("ok");
if (click.DialogClosed && click.ClickedId is not null)
    stack.CloseTop(click.ClickedId);
Console.WriteLine($"dialog-result: {await pending}");
Packager.Stage(manifest, appDll, outDir);
Console.WriteLine($"staged: {outDir}");
return 0;
