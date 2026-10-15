// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Variant;

namespace Windows.Win32.Web.MsHtml
{
    [Guid("332C4425-26CB-11D0-B483-00C04FD90119")]
    internal unsafe partial struct IHTMLDocument : IComIID, IVTable<IHTMLDocument, IHTMLDocument.Vtbl>
    {
        private static readonly Guid s_guid = new("332C4425-26CB-11D0-B483-00C04FD90119");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT QueryInterface(Guid* riid, void** ppvObject) => HRESULT.E_NOTIMPL;
        public HRESULT QueryInterface(in Guid riid, out void* ppvObject) { fixed (void** p = &ppvObject) fixed (Guid* g = &riid) return QueryInterface(g, p); }
        public HRESULT QueryInterface(in Guid riid, void** ppvObject) { fixed (Guid* g = &riid) return QueryInterface(g, ppvObject); }
        public uint AddRef() => 1;
        public uint Release() => 1;

        public HRESULT get_Script(void** p) => HRESULT.E_NOTIMPL;

        public interface Interface { }
    }

    [Guid("332C4425-26CB-11D0-B483-00C04FD90119")]
    internal unsafe partial struct IHTMLDocument2 : IComIID, IVTable<IHTMLDocument2, IHTMLDocument2.Vtbl>
    {
        private static readonly Guid s_guid = new("332C4425-26CB-11D0-B483-00C04FD90119");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT QueryInterface(Guid* riid, void** ppvObject) => HRESULT.E_NOTIMPL;
        public HRESULT QueryInterface(in Guid riid, out void* ppvObject) { fixed (void** p = &ppvObject) fixed (Guid* g = &riid) return QueryInterface(g, p); }
        public HRESULT QueryInterface(in Guid riid, void** ppvObject) { fixed (Guid* g = &riid) return QueryInterface(g, ppvObject); }
        public uint AddRef() => 1;
        public uint Release() => 1;

        public HRESULT get_activeElement(IHTMLElement** p) => HRESULT.E_NOTIMPL;
        public HRESULT get_body(IHTMLElement** p) => HRESULT.E_NOTIMPL;
        public HRESULT get_domain(BSTR* p) => HRESULT.E_NOTIMPL;
        public HRESULT put_domain(BSTR p) => HRESULT.E_NOTIMPL;
        public HRESULT get_title(BSTR* p) => HRESULT.E_NOTIMPL;
        public HRESULT put_title(BSTR p) => HRESULT.E_NOTIMPL;
        public HRESULT get_location(void** p) => HRESULT.E_NOTIMPL;
        public HRESULT get_parentWindow(IHTMLWindow2** p) => HRESULT.E_NOTIMPL;
        public HRESULT get_bgColor(VARIANT* p) => HRESULT.E_NOTIMPL;
        public HRESULT put_bgColor(VARIANT p) => HRESULT.E_NOTIMPL;
        public HRESULT get_fgColor(VARIANT* p) => HRESULT.E_NOTIMPL;
        public HRESULT put_fgColor(VARIANT p) => HRESULT.E_NOTIMPL;
        public HRESULT get_linkColor(VARIANT* p) => HRESULT.E_NOTIMPL;
        public HRESULT get_mimeType(BSTR* p) => HRESULT.E_NOTIMPL;
        public HRESULT put_linkColor(VARIANT p) => HRESULT.E_NOTIMPL;
        public HRESULT get_alinkColor(VARIANT* p) => HRESULT.E_NOTIMPL;
        public HRESULT put_alinkColor(VARIANT p) => HRESULT.E_NOTIMPL;
        public HRESULT get_vlinkColor(VARIANT* p) => HRESULT.E_NOTIMPL;
        public HRESULT put_vlinkColor(VARIANT p) => HRESULT.E_NOTIMPL;
        public HRESULT get_cookie(BSTR* p) => HRESULT.E_NOTIMPL;
        public HRESULT put_cookie(BSTR p) => HRESULT.E_NOTIMPL;
        public HRESULT get_charset(BSTR* p) => HRESULT.E_NOTIMPL;
        public HRESULT put_charset(BSTR p) => HRESULT.E_NOTIMPL;
        public HRESULT get_defaultCharset(BSTR* p) => HRESULT.E_NOTIMPL;
        public HRESULT get_all(IHTMLElementCollection** p) => HRESULT.E_NOTIMPL;
        public HRESULT get_links(IHTMLElementCollection** p) => HRESULT.E_NOTIMPL;
        public HRESULT get_images(IHTMLElementCollection** p) => HRESULT.E_NOTIMPL;
        public HRESULT get_forms(IHTMLElementCollection** p) => HRESULT.E_NOTIMPL;
        public HRESULT write(SAFEARRAY* psarray) => HRESULT.S_OK;
        public HRESULT execCommand(BSTR cmdID, VARIANT_BOOL showUI, VARIANT value, VARIANT_BOOL* pfRet) => HRESULT.E_NOTIMPL;
        public HRESULT elementFromPoint(int x, int y, IHTMLElement** p) => HRESULT.E_NOTIMPL;
        public HRESULT open(BSTR url, VARIANT name, VARIANT features, VARIANT replace, void** pomWindowResult) => HRESULT.E_NOTIMPL;
        public HRESULT createElement(BSTR eTag, IHTMLElement** p) => HRESULT.E_NOTIMPL;
        public HRESULT get_Script(void** p) => HRESULT.E_NOTIMPL;

        public interface Interface { }
    }

    [Guid("3050F1FF-98B5-11CF-BB82-00AA00BDCE0B")]
    internal unsafe partial struct IHTMLElement : IComIID, IVTable<IHTMLElement, IHTMLElement.Vtbl>
    {
        private static readonly Guid s_guid = new("3050F1FF-98B5-11CF-BB82-00AA00BDCE0B");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT get_all(void** p) => HRESULT.E_NOTIMPL;
        public HRESULT get_children(void** p) => HRESULT.E_NOTIMPL;
        public HRESULT get_document(void** p) => HRESULT.E_NOTIMPL;
        public HRESULT get_id(BSTR* p) => HRESULT.E_NOTIMPL;
        public HRESULT put_id(BSTR p) => HRESULT.E_NOTIMPL;
        public HRESULT get_innerHTML(BSTR* p) => HRESULT.E_NOTIMPL;
        public HRESULT put_innerHTML(BSTR p) => HRESULT.E_NOTIMPL;
        public HRESULT get_innerText(BSTR* p) => HRESULT.E_NOTIMPL;
        public HRESULT put_innerText(BSTR p) => HRESULT.E_NOTIMPL;
        public HRESULT get_offsetLeft(int* p) => HRESULT.E_NOTIMPL;
        public HRESULT get_offsetTop(int* p) => HRESULT.E_NOTIMPL;
        public HRESULT get_offsetWidth(int* p) => HRESULT.E_NOTIMPL;
        public HRESULT get_offsetHeight(int* p) => HRESULT.E_NOTIMPL;
        public HRESULT get_offsetParent(IHTMLElement** p) => HRESULT.E_NOTIMPL;
        public HRESULT get_outerHTML(BSTR* p) => HRESULT.E_NOTIMPL;
        public HRESULT put_outerHTML(BSTR p) => HRESULT.E_NOTIMPL;
        public HRESULT get_outerText(BSTR* p) => HRESULT.E_NOTIMPL;
        public HRESULT put_outerText(BSTR p) => HRESULT.E_NOTIMPL;
        public HRESULT get_parentElement(IHTMLElement** p) => HRESULT.E_NOTIMPL;
        public HRESULT get_style(void** p) => HRESULT.E_NOTIMPL;
        public HRESULT get_tagName(BSTR* p) => HRESULT.E_NOTIMPL;
        public HRESULT getAttribute(BSTR strAttributeName, int lFlags, VARIANT* pvarsAttributeValue) => HRESULT.E_NOTIMPL;
        public HRESULT setAttribute(BSTR strAttributeName, VARIANT AttributeValue, int lFlags) => HRESULT.E_NOTIMPL;
        public HRESULT scrollIntoView(VARIANT varargStart) => HRESULT.S_OK;

        public interface Interface { }
    }

    [Guid("3050F21F-98B5-11CF-BB82-00AA00BDCE0B")]
    internal unsafe partial struct IHTMLElementCollection : IComIID, IVTable<IHTMLElementCollection, IHTMLElementCollection.Vtbl>
    {
        private static readonly Guid s_guid = new("3050F21F-98B5-11CF-BB82-00AA00BDCE0B");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT item(VARIANT name, VARIANT index, void** pdisp) => HRESULT.E_NOTIMPL;
        public HRESULT get_length(int* p) => HRESULT.E_NOTIMPL;

        public interface Interface { }
    }

    [Guid("332C4427-26CB-11D0-B483-00C04FD90119")]
    internal unsafe partial struct IHTMLWindow2 : IComIID, IVTable<IHTMLWindow2, IHTMLWindow2.Vtbl>
    {
        private static readonly Guid s_guid = new("332C4427-26CB-11D0-B483-00C04FD90119");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT get_document(void** p) => HRESULT.E_NOTIMPL;
        public HRESULT get_frames(IHTMLFramesCollection2** p) => HRESULT.E_NOTIMPL;
        public HRESULT get_history(IOmHistory** p) => HRESULT.E_NOTIMPL;
        public HRESULT get_closed(VARIANT_BOOL* p) => HRESULT.E_NOTIMPL;
        public HRESULT get_name(BSTR* p) => HRESULT.E_NOTIMPL;
        public HRESULT put_name(BSTR name) => HRESULT.E_NOTIMPL;
        public HRESULT get_opener(VARIANT* p) => HRESULT.E_NOTIMPL;
        public HRESULT get_parent(IHTMLWindow2** p) => HRESULT.E_NOTIMPL;
        public HRESULT get_status(BSTR* p) => HRESULT.E_NOTIMPL;
        public HRESULT put_status(BSTR status) => HRESULT.E_NOTIMPL;
        public HRESULT get_location(void** p) => HRESULT.E_NOTIMPL;
        public HRESULT alert(BSTR message) => HRESULT.S_OK;
        public HRESULT close() => HRESULT.S_OK;
        public HRESULT confirm(BSTR message, VARIANT_BOOL* p) => HRESULT.S_OK;
        public HRESULT focus() => HRESULT.S_OK;
        public HRESULT moveTo(int x, int y) => HRESULT.S_OK;
        public HRESULT navigate(BSTR url) => HRESULT.S_OK;
        public HRESULT open(BSTR url, BSTR name, BSTR features, VARIANT_BOOL replace, IHTMLWindow2** pomWindowResult) => HRESULT.E_NOTIMPL;
        public HRESULT prompt(BSTR message, BSTR defstr, VARIANT* pvar) => HRESULT.E_NOTIMPL;
        public HRESULT blur() => HRESULT.S_OK;
        public HRESULT resizeTo(int x, int y) => HRESULT.S_OK;
        public HRESULT scrollTo(int x, int y) => HRESULT.S_OK;

        public interface Interface { }
    }

    [Guid("FECEAAA2-5146-11D0-B4D4-00A0C9055F88")]
    internal unsafe partial struct IOmHistory : IComIID, IVTable<IOmHistory, IOmHistory.Vtbl>
    {
        private static readonly Guid s_guid = new("FECEAAA2-5146-11D0-B4D4-00A0C9055F88");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public short length => 0;
        public HRESULT go(VARIANT* pvargdistance) => HRESULT.S_OK;

        public interface Interface { }
    }

    [Guid("332C4426-26CB-11D0-B483-00C04FD90119")]
    internal unsafe partial struct IHTMLFramesCollection2 : IComIID, IVTable<IHTMLFramesCollection2, IHTMLFramesCollection2.Vtbl>
    {
        private static readonly Guid s_guid = new("332C4426-26CB-11D0-B483-00C04FD90119");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT item(VARIANT* pvarIndex, VARIANT* pvarResult) => HRESULT.E_NOTIMPL;
        public HRESULT get_length(int* p) => HRESULT.E_NOTIMPL;

        public interface Interface { }
    }

    [Guid("3050F485-98B5-11CF-BB82-00AA00BDCE0B")]
    internal unsafe partial struct IHTMLDocument3 : IComIID, IVTable<IHTMLDocument3, IHTMLDocument3.Vtbl>
    {
        private static readonly Guid s_guid = new("3050F485-98B5-11CF-BB82-00AA00BDCE0B");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT getElementById(BSTR v, IHTMLElement** pel) => HRESULT.E_NOTIMPL;
        public HRESULT getElementsByTagName(BSTR v, IHTMLElementCollection** pelColl) => HRESULT.E_NOTIMPL;
        public HRESULT attachEvent(BSTR @event, IDispatch* pDisp, VARIANT_BOOL* pfResult) => HRESULT.S_OK;
        public HRESULT detachEvent(BSTR @event, IDispatch* pDisp) => HRESULT.S_OK;
        public HRESULT get_dir(BSTR* p) => HRESULT.E_NOTIMPL;
        public HRESULT put_dir(BSTR p) => HRESULT.S_OK;

        public interface Interface { }
    }

    [Guid("3050F327-98B5-11CF-BB82-00AA00BDCE0B")]
    internal unsafe partial struct IHTMLEventObj : IComIID, IVTable<IHTMLEventObj, IHTMLEventObj.Vtbl>
    {
        private static readonly Guid s_guid = new("3050F327-98B5-11CF-BB82-00AA00BDCE0B");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public interface Interface { }
    }

    [Guid("3050F69A-98B5-11CF-BB82-00AA00BDCE0B")]
    internal unsafe partial struct IHTMLDocument4 : IComIID, IVTable<IHTMLDocument4, IHTMLDocument4.Vtbl>
    {
        private static readonly Guid s_guid = new("3050F69A-98B5-11CF-BB82-00AA00BDCE0B");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT createEventObject(VARIANT* pvarPassedIn, IHTMLEventObj** ppEventObj) => HRESULT.E_NOTIMPL;
        public HRESULT fireEvent(BSTR bstrEventName, VARIANT* pvarEventObject, VARIANT_BOOL* pfCancelled) => HRESULT.S_OK;
        public HRESULT hasFocus(VARIANT_BOOL* pfFocus) => HRESULT.E_NOTIMPL;
        public HRESULT focus() => HRESULT.S_OK;

        public interface Interface { }
    }

    [Guid("3050F5DA-98B5-11CF-BB82-00AA00BDCE0B")]
    internal unsafe partial struct IHTMLDOMNode : IComIID, IVTable<IHTMLDOMNode, IHTMLDOMNode.Vtbl>
    {
        private static readonly Guid s_guid = new("3050F5DA-98B5-11CF-BB82-00AA00BDCE0B");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT QueryInterface(Guid* riid, void** ppvObject) => HRESULT.E_NOTIMPL;
        public HRESULT QueryInterface(in Guid riid, out void* ppvObject) { fixed (void** p = &ppvObject) fixed (Guid* g = &riid) return QueryInterface(g, p); }
        public HRESULT QueryInterface(in Guid riid, void** ppvObject) { fixed (Guid* g = &riid) return QueryInterface(g, ppvObject); }
        public uint AddRef() => 1;
        public uint Release() => 1;

        public HRESULT get_firstChild(IHTMLDOMNode** p) => HRESULT.E_NOTIMPL;
        public HRESULT get_lastChild(IHTMLDOMNode** p) => HRESULT.E_NOTIMPL;
        public HRESULT get_previousSibling(IHTMLDOMNode** p) => HRESULT.E_NOTIMPL;
        public HRESULT get_nextSibling(IHTMLDOMNode** p) => HRESULT.E_NOTIMPL;
        public HRESULT hasChildNodes(VARIANT_BOOL* p) => HRESULT.E_NOTIMPL;
        public HRESULT get_childNodes(IDispatch** p) => HRESULT.E_NOTIMPL;
        public HRESULT cloneNode(VARIANT_BOOL fDeep, IHTMLDOMNode** clonedNode) => HRESULT.E_NOTIMPL;
        public HRESULT removeNode(VARIANT_BOOL fDeep, IHTMLDOMNode** removedNode) => HRESULT.E_NOTIMPL;
        public HRESULT replaceNode(IHTMLDOMNode* replacement, IHTMLDOMNode** replacedNode) => HRESULT.E_NOTIMPL;
        public HRESULT appendChild(IHTMLDOMNode* newChild, IHTMLDOMNode** node) => HRESULT.E_NOTIMPL;
        public HRESULT insertBefore(IHTMLDOMNode* newChild, VARIANT refChild, IHTMLDOMNode** node) => HRESULT.E_NOTIMPL;

        public interface Interface { }
    }

    [Guid("3050F618-98B5-11CF-BB82-00AA00BDCE0B")]
    internal unsafe partial struct IHTMLElement2 : IComIID, IVTable<IHTMLElement2, IHTMLElement2.Vtbl>
    {
        private static readonly Guid s_guid = new("3050F618-98B5-11CF-BB82-00AA00BDCE0B");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT QueryInterface(Guid* riid, void** ppvObject) => HRESULT.E_NOTIMPL;
        public HRESULT QueryInterface(in Guid riid, out void* ppvObject) { fixed (void** p = &ppvObject) fixed (Guid* g = &riid) return QueryInterface(g, p); }
        public HRESULT QueryInterface(in Guid riid, void** ppvObject) { fixed (Guid* g = &riid) return QueryInterface(g, ppvObject); }
        public uint AddRef() => 1;
        public uint Release() => 1;

        public HRESULT get_clientTop(int* p) => HRESULT.E_NOTIMPL;
        public HRESULT get_clientLeft(int* p) => HRESULT.E_NOTIMPL;
        public HRESULT get_clientWidth(int* p) => HRESULT.E_NOTIMPL;
        public HRESULT get_clientHeight(int* p) => HRESULT.E_NOTIMPL;
        public HRESULT get_scrollLeft(int* p) => HRESULT.E_NOTIMPL;
        public HRESULT put_scrollLeft(int v) => HRESULT.S_OK;
        public HRESULT get_scrollTop(int* p) => HRESULT.E_NOTIMPL;
        public HRESULT put_scrollTop(int v) => HRESULT.S_OK;
        public HRESULT get_scrollWidth(int* p) => HRESULT.E_NOTIMPL;
        public HRESULT get_scrollHeight(int* p) => HRESULT.E_NOTIMPL;
        public HRESULT get_canHaveChildren(VARIANT_BOOL* p) => HRESULT.E_NOTIMPL;
        public HRESULT get_tabIndex(short* p) => HRESULT.E_NOTIMPL;
        public HRESULT put_tabIndex(short v) => HRESULT.S_OK;
        public HRESULT focus() => HRESULT.S_OK;
        public HRESULT blur() => HRESULT.S_OK;
        public HRESULT attachEvent(BSTR @event, IDispatch* pDisp, VARIANT_BOOL* pfResult) => HRESULT.S_OK;
        public HRESULT detachEvent(BSTR @event, IDispatch* pDisp) => HRESULT.S_OK;
        public HRESULT getElementsByTagName(BSTR v, IHTMLElementCollection** pelColl) => HRESULT.E_NOTIMPL;
        public HRESULT insertAdjacentElement(BSTR where, IHTMLElement* insertedElement, IHTMLElement** adjElement) => HRESULT.S_OK;

        public interface Interface { }
    }

    [Guid("3050F673-98B5-11CF-BB82-00AA00BDCE0B")]
    internal unsafe partial struct IHTMLElement3 : IComIID, IVTable<IHTMLElement3, IHTMLElement3.Vtbl>
    {
        private static readonly Guid s_guid = new("3050F673-98B5-11CF-BB82-00AA00BDCE0B");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT QueryInterface(Guid* riid, void** ppvObject) => HRESULT.E_NOTIMPL;
        public HRESULT QueryInterface(in Guid riid, out void* ppvObject) { fixed (void** p = &ppvObject) fixed (Guid* g = &riid) return QueryInterface(g, p); }
        public HRESULT QueryInterface(in Guid riid, void** ppvObject) { fixed (Guid* g = &riid) return QueryInterface(g, ppvObject); }
        public uint AddRef() => 1;
        public uint Release() => 1;

        public HRESULT get_disabled(VARIANT_BOOL* p) => HRESULT.E_NOTIMPL;
        public HRESULT put_disabled(VARIANT_BOOL v) => HRESULT.S_OK;
        public HRESULT fireEvent(BSTR bstrEventName, VARIANT* pvarEventObject, VARIANT_BOOL* pfContinue) => HRESULT.S_OK;

        public interface Interface { }
    }

    [Guid("3050F311-98B5-11CF-BB82-00AA00BDCE0B")]
    internal unsafe partial struct IHTMLFrameBase : IComIID, IVTable<IHTMLFrameBase, IHTMLFrameBase.Vtbl>
    {
        private static readonly Guid s_guid = new("3050F311-98B5-11CF-BB82-00AA00BDCE0B");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT QueryInterface(Guid* riid, void** ppvObject) => HRESULT.E_NOTIMPL;
        public HRESULT QueryInterface(in Guid riid, out void* ppvObject) { fixed (void** p = &ppvObject) fixed (Guid* g = &riid) return QueryInterface(g, p); }
        public HRESULT QueryInterface(in Guid riid, void** ppvObject) { fixed (Guid* g = &riid) return QueryInterface(g, ppvObject); }
        public uint AddRef() => 1;
        public uint Release() => 1;

        public HRESULT get_name(BSTR* p) => HRESULT.E_NOTIMPL;
        public HRESULT put_name(BSTR v) => HRESULT.S_OK;

        public interface Interface { }
    }

    [Guid("1630FD0F-876E-11CF-BE87-00AA0051FE20")]
    internal unsafe partial struct IHTMLLocation : IComIID, IVTable<IHTMLLocation, IHTMLLocation.Vtbl>
    {
        private static readonly Guid s_guid = new("1630FD0F-876E-11CF-BE87-00AA0051FE20");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT get_href(BSTR* p) => HRESULT.E_NOTIMPL;
        public HRESULT put_href(BSTR v) => HRESULT.S_OK;

        public interface Interface { }
    }

    [Guid("3050F25E-98B5-11CF-BB82-00AA00BDCE0B")]
    internal unsafe partial struct IHTMLStyle : IComIID, IVTable<IHTMLStyle, IHTMLStyle.Vtbl>
    {
        private static readonly Guid s_guid = new("3050F25E-98B5-11CF-BB82-00AA00BDCE0B");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT get_cssText(BSTR* p) => HRESULT.E_NOTIMPL;
        public HRESULT put_cssText(BSTR v) => HRESULT.S_OK;

        public interface Interface { }
    }

    [Guid("3050F4AE-98B5-11CF-BB82-00AA00BDCE0B")]
    internal unsafe partial struct IHTMLWindow3 : IComIID, IVTable<IHTMLWindow3, IHTMLWindow3.Vtbl>
    {
        private static readonly Guid s_guid = new("3050F4AE-98B5-11CF-BB82-00AA00BDCE0B");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT attachEvent(BSTR @event, IDispatch* pDisp, VARIANT_BOOL* pfResult) => HRESULT.S_OK;
        public HRESULT detachEvent(BSTR @event, IDispatch* pDisp) => HRESULT.S_OK;
        public HRESULT get_screenLeft(int* p) => HRESULT.E_NOTIMPL;
        public HRESULT get_screenTop(int* p) => HRESULT.E_NOTIMPL;

        public interface Interface { }
    }

    [Guid("3050F695-98B5-11CF-BB82-00AA00BDCE0B")]
    internal unsafe partial struct IHTMLWindow4 : IComIID, IVTable<IHTMLWindow4, IHTMLWindow4.Vtbl>
    {
        private static readonly Guid s_guid = new("3050F695-98B5-11CF-BB82-00AA00BDCE0B");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT get_frameElement(IHTMLFrameBase** p) => HRESULT.E_NOTIMPL;

        public interface Interface { }
    }
}
