# Third-party notices

The root [MIT license](LICENSE) applies to original project code. Existing copyright headers and component licenses continue to apply to adapted or vendored material.

## Vendored source and browser libraries

| Component | Version or source | License and notice |
| --- | --- | --- |
| .NET Windows Forms | [`dotnet/winforms` at `a84cf1f1da5530adca2598d57b58063228e3353f`](https://github.com/dotnet/winforms/tree/a84cf1f1da5530adca2598d57b58063228e3353f) | [MIT](runtimes/winforms/LICENSE.TXT), [.NET third-party notices](runtimes/winforms/THIRD-PARTY-NOTICES.TXT). Source snapshot used by the fork; not an unmodified upstream build. |
| Bootstrap | 5.3.3 | [MIT](licenses/bootstrap-LICENSE.txt), [upstream](https://github.com/twbs/bootstrap/blob/v5.3.3/LICENSE). |
| PixiJS | 7.4.3 | [MIT](licenses/pixijs-LICENSE.txt), [upstream](https://github.com/pixijs/pixijs/blob/v7.4.3/LICENSE). |
| PixiJS Filters | 5.3.0 | [MIT](licenses/pixi-filters-LICENSE.txt), [upstream](https://github.com/pixijs/filters/blob/v5.3.0/LICENSE). |

The repository preserves the license headers in the browser bundles. Adapted .NET source files outside the vendored tree also retain their original notices and are covered by the applicable .NET license.

## Restored dependencies

The project files declare NuGet dependencies, including Mono.Cecil, .NET browser hosting packages, and test tooling. Package restore obtains them separately; their own licenses apply. Consult the package metadata and license files for the versions selected by each project. The source distribution does not include restored package binaries.

## Applications in screenshots

- Paint.NET: [Paint.NET](https://www.getpaint.net/), version 5.1.12 in the capture. Its name, interface, and artwork remain the property of their respective owners. The screenshot shows a separately obtained local copy with incomplete browser compatibility; no Paint.NET application files or converted assemblies are distributed here.
- Calculator: [albisnesstz/csharp-win-calculator](https://github.com/albisnesstz/csharp-win-calculator). Its source repository carries an MIT license; the screenshot shows a separately built local copy.
- Photo Album and MedicalApp: Telerik UI for WinForms demo applications. Telerik/Progress names, components, photographs, and other artwork remain the property of their respective owners and are not covered by this project's MIT license.

- Syncfusion WinForms demos: [syncfusion/winforms-demos](https://github.com/syncfusion/winforms-demos). Map Selection, Barcode and Diagram Nodes captures show separately obtained/built samples and their control libraries. Their names, interfaces and artwork remain subject to their owners' terms; no sample application, commercial control assembly or sample asset is distributed here.

Screenshots document local interoperability experiments. They do not grant rights to redistribute the applications, their libraries, or their assets, and do not imply vendor affiliation or endorsement. No third-party application, executable, installer, converted assembly, or commercial demo library is included in this repository.
