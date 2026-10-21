import { virtualPath } from './platform-client.mjs';

const style = `
:host{font:14px system-ui,sans-serif;color:#202d41}dialog{border:1px solid #d5deea;border-radius:16px;padding:0;width:min(760px,92vw);box-shadow:0 20px 80px #13213940;background:#fff;color:inherit}dialog::backdrop{background:#17273f66}
header,footer{padding:18px 22px;display:flex;gap:10px;align-items:center}header{border-bottom:1px solid #e5eaf2}h2{font-size:19px;margin:0;flex:1}button,select,input{font:inherit;border:1px solid #ccd6e5;border-radius:8px;padding:9px 12px;background:white;color:inherit}button{cursor:pointer}button:hover{background:#edf3fc}button:disabled{opacity:.45;cursor:default}.primary{background:#235cdc;color:white;border-color:#235cdc}.primary:hover{background:#1748b8}.tools{padding:14px 22px;display:flex;gap:8px;flex-wrap:wrap}.body{padding:0 22px}.files{height:290px;overflow:auto;border:1px solid #e0e7f1;border-radius:10px} .entry{display:flex;align-items:center;gap:10px;padding:10px;border-bottom:1px solid #edf1f7}.entry button{border:0;padding:2px;text-align:left;flex:1;border-radius:3px}.muted{font-size:12px;color:#65758b;margin:8px 0} .error{color:#a52336;min-height:18px;margin:8px 0}.spacer{flex:1}input[type=text]{flex:1;min-width:120px}footer{border-top:1px solid #e5eaf2;margin-top:16px}iframe{width:100%;height:55vh;border:0;background:#e7edf5}.path{overflow-wrap:anywhere}
`;
function node(tag, text, parent) { const n = document.createElement(tag); if (text !== undefined) n.textContent = text; parent?.append(n); return n; }
function button(text, parent, click, primary = false) { const n = node('button', text, parent); n.type = 'button'; if (primary) n.className = 'primary'; n.onclick = click; return n; }
function modal(title) {
    const host = node('div', undefined, document.body), root = host.attachShadow({ mode: 'open' });
    node('style', style, root); const dialog = node('dialog', undefined, root);
    const header = node('header', undefined, dialog); node('h2', title, header);
    dialog.showModal(); return { host, dialog, root };
}

export function filePatterns(filter = 'All files|*.*', filterIndex = 1) {
    const parts = filter.split('|');
    const pattern = parts.length > 1 ? parts[Math.max(0, filterIndex - 1) * 2 + 1] ?? '*.*' : filter;
    return pattern.split(';').map(s => s.trim()).filter(Boolean);
}
export function matchesFilter(name, patterns) {
    return patterns.some(p => p === '*' || p === '*.*' || new RegExp('^' + p.replace(/[.+^${}()|[\]\\]/g, '\\$&').replaceAll('*', '.*').replaceAll('?', '.') + '$', 'i').test(name));
}

// Async by design. Legacy ShowDialog needs a continuation/worker bridge, never a main-thread busy wait.
export function showFileDialog(client, { title = 'Open a file', mode = 'open', multiple = false, filter = 'All files|*.*', filterIndex = 1, fileName = '', signal } = {}) {
    if (!['open', 'save', 'folder'].includes(mode)) throw new Error('Unknown dialog mode');
    if (signal?.aborted) return Promise.resolve({ cancelled: true, items: [] });
    return new Promise(resolve => {
        const { host, dialog } = modal(title), tools = node('div', undefined, dialog); tools.className = 'tools';
        const source = node('select', undefined, tools); source.setAttribute('aria-label', 'File location');
        const typeParts = filter.split('|');
        const typeSelect = typeParts.length > 1 ? node('select', undefined, tools) : null;
        if (typeSelect) { typeSelect.setAttribute('aria-label', 'File type'); for (let i = 0; i + 1 < typeParts.length; i += 2) { const o = node('option', typeParts[i], typeSelect); o.value = String(i / 2 + 1); } typeSelect.value = String(filterIndex); }
        const up = button('↑ Up', tools, () => { const p = virtualPath(location); const parts = p.path.split('/'); parts.pop(); load(p.mount + ':/' + parts.filter(Boolean).join('/')); });
        const body = node('div', undefined, dialog), pathLabel = node('p', '', body); body.className = 'body'; pathLabel.className = 'path muted';
        const files = node('div', undefined, body); files.className = 'files'; files.setAttribute('role', 'list');
        const error = node('p', '', body); error.className = 'error'; error.setAttribute('role', 'status');
        const filename = mode === 'save' ? node('input', undefined, body) : null;
        if (filename) { filename.type = 'text'; filename.value = fileName; filename.setAttribute('aria-label', 'File name'); }
        const footer = node('footer', undefined, dialog);
        const native = !!globalThis.showOpenFilePicker;
        const device = button(mode === 'save' ? 'Save on my device…' : mode === 'folder' ? 'Choose device folder…' : 'Choose device files…', footer, chooseDevice);
        if (mode === 'folder' && !globalThis.showDirectoryPicker) device.textContent = 'Import device folder…';
        node('span', '', footer).className = 'spacer'; button('Cancel', footer, () => finish(null));
        const accept = button(mode === 'save' ? 'Save here' : mode === 'folder' ? 'Use this folder' : 'Open', footer, chooseCurrent, true);
        let location = 'workspace:/', mounts = [], selected = new Map(), ended = false, generation = 0;
        let patterns = filePatterns(filter, filterIndex);
        const abort = () => finish(null); signal?.addEventListener('abort', abort, { once: true });
        dialog.addEventListener('cancel', e => { e.preventDefault(); finish(null); });
        function finish(items) { if (ended) return; ended = true; generation++; signal?.removeEventListener('abort', abort); dialog.close(); host.remove(); resolve({ cancelled: !items, items: items ?? [], filterIndex }); }
        function update() { const m = mounts.find(m => m.id === virtualPath(location).mount); accept.disabled = mode === 'save' ? !m?.writable : mode === 'folder' ? !!m?.isFile : selected.size === 0; }
        async function load(path) {
            const gen = ++generation; location = path; selected.clear(); error.textContent = ''; files.replaceChildren(); pathLabel.textContent = path; accept.disabled = true;
            up.disabled = !virtualPath(path).path;
            try {
                let offset = 0;
                do {
                    const page = await client.list(path, offset); if (ended || gen !== generation) return;
                    for (const entry of page.entries) {
                        if (!entry.isDirectory && (mode === 'folder' || !matchesFilter(entry.name, patterns))) continue;
                        const row = node('div', undefined, files); row.className = 'entry'; row.setAttribute('role', 'listitem');
                        const full = virtualPath(path).mount + ':/' + entry.path;
                        if (!entry.isDirectory && mode === 'open') {
                            const check = node('input', undefined, row); check.type = 'checkbox'; check.setAttribute('aria-label', 'Select ' + entry.name);
                            check.onchange = () => {
                                if (!multiple) { selected.clear(); for (const n of files.querySelectorAll('input')) if (n !== check) n.checked = false; }
                                if (check.checked) selected.set(full, { path: full, name: entry.name, isDirectory: false, source: mounts.find(m => m.id === virtualPath(path).mount)?.device ? 'device' : 'server' }); else selected.delete(full);
                                update();
                            };
                        }
                        button((entry.isDirectory ? '▸ ' : '') + entry.name, row, () => {
                            if (entry.isDirectory) load(full);
                            else if (mode === 'save') filename.value = entry.name;
                            else { const check = row.querySelector('input'); check.checked = !check.checked; check.onchange(); }
                        });
                        if (!entry.isDirectory) node('small', entry.length.toLocaleString() + ' bytes', row);
                    }
                    offset = page.nextOffset;
                } while (offset !== null);
                if (!files.children.length) node('p', 'No matching files in this folder.', files).className = 'muted';
                update();
            } catch (e) { if (!ended && gen === generation) error.textContent = e.message; }
        }
        async function chooseCurrent() {
            try {
                if (mode === 'folder') return finish([{ path: location, name: location, isDirectory: true, source: 'server' }]);
                if (mode === 'save') {
                    const name = filename.value; if (!name || name.includes('/') || name.includes('\\')) throw new Error('Enter a file name.');
                    const path = location.replace(/\/$/, '') + '/' + name; virtualPath(path);
                    let offset = 0, exists = false;
                    do { const page = await client.list(location, offset); exists ||= page.entries.some(e => e.name.toUpperCase() === name.toUpperCase()); offset = page.nextOffset; } while (offset !== null && !exists);
                    if (ended) return;
                    if (exists && !globalThis.confirm('Replace ' + name + '?')) return;
                    return finish([{ path, name, isDirectory: false, source: 'server' }]);
                }
                finish([...selected.values()]);
            } catch (e) { error.textContent = e.message; }
        }
        async function chooseDevice() {
            try {
                // Native picker invocation must precede any await unrelated to that picker.
                if (mode === 'save' && globalThis.showSaveFilePicker) {
                    const handle = await globalThis.showSaveFilePicker({ suggestedName: filename.value || 'document' });
                    if (ended) return;
                    const id = 'device-' + crypto.randomUUID(); client.devices.set(id, handle);
                    return finish([{ path: id + ':/' + handle.name, name: handle.name, isDirectory: false, source: 'device' }]);
                }
                if (mode !== 'save' && (mode === 'folder' ? globalThis.showDirectoryPicker : native)) {
                    const extensions = patterns.filter(p => /^\*\.[a-z0-9]+$/i.test(p)).map(p => p.slice(1));
                    const pickerOptions = extensions.length ? { types: [{ description: typeParts[(filterIndex - 1) * 2] || 'Files', accept: { 'application/octet-stream': extensions } }] } : {};
                    const items = await client.grantDevice(mode === 'folder' ? 'directory' : 'file', multiple, pickerOptions);
                    if (ended) { for (const item of items) client.revokeDevice(item.path); return; }
                    return finish(items.map(i => ({ ...i, source: 'device' })));
                }
                if (mode === 'save') throw new Error('Direct device saving is unavailable. Save to My workspace, then download the file.');
                error.textContent = 'Files you choose will be copied into your workspace.';
                const input = node('input', undefined, body); input.type = 'file'; input.multiple = multiple || mode === 'folder'; input.hidden = true;
                if (mode === 'folder') input.setAttribute('webkitdirectory', '');
                input.accept = patterns.filter(p => /^\*\.[a-z0-9]+$/i.test(p)).map(p => p.slice(1)).join(',');
                input.addEventListener('cancel', () => input.remove(), { once: true });
                input.onchange = async () => {
                    try {
                        const batch = [...input.files]; if (ended || !batch.length) return;
                        const prefix = 'workspace:/Imports/' + crypto.randomUUID(), items = [];
                        for (const file of batch) {
                            if (ended) return;
                            if (file.size > 16 * 1024 * 1024) throw new Error('File exceeds transfer limit');
                            const name = file.webkitRelativePath || file.name, path = prefix + '/' + name; virtualPath(path);
                            await client.write(path, new Uint8Array(await file.arrayBuffer()));
                            items.push({ path, name: file.name, isDirectory: false, source: 'workspace-import' });
                        }
                        if (mode === 'folder') finish([{ path: prefix + '/' + batch[0].webkitRelativePath.split('/')[0], name: 'Imported folder', isDirectory: true, source: 'workspace-import' }]);
                        else finish(items);
                    } catch (e) { error.textContent = e.message; } finally { input.remove(); }
                };
                input.click();
            } catch (e) { if (e.name !== 'AbortError' && !ended) error.textContent = e.message; }
        }
        source.onchange = () => load(source.value + ':/');
        if (typeSelect) typeSelect.onchange = () => { filterIndex = Number(typeSelect.value); patterns = filePatterns(filter, filterIndex); load(location); };
        client.mounts().then(result => {
            if (ended) return; mounts = result;
            for (const m of mounts) { const o = node('option', m.label + (m.writable ? '' : ' · read only'), source); o.value = m.id; }
            load(location);
        }).catch(e => { if (!ended) error.textContent = e.message; });
    });
}

// Print pages are images, never application-supplied HTML. Physical dimensions are explicit.
export function validatePrintJob(pages) {
    if (!Array.isArray(pages) || !pages.length || pages.length > 100) throw new Error('Expected 1–100 print pages');
    let totalPixels = 0;
    for (const p of pages) {
        if (!Number.isInteger(p.width) || !Number.isInteger(p.height) || p.width <= 0 || p.height <= 0 ||
            p.width * p.height > 16_000_000 || !Number.isFinite(p.widthMm) || !Number.isFinite(p.heightMm) ||
            p.widthMm < 10 || p.heightMm < 10 || p.widthMm > 1200 || p.heightMm > 1200) throw new Error('Invalid print page size');
        totalPixels += p.width * p.height;
        if (p.rgba && (!(p.rgba instanceof Uint8Array) || p.rgba.length !== p.width * p.height * 4)) throw new Error('Invalid print pixels');
        if (!p.rgba && (typeof p.png !== 'string' || p.png.length > 24_000_000 || !/^data:image\/png;base64,[A-Za-z0-9+/]+=*$/.test(p.png))) throw new Error('Expected PNG data or RGBA pixels');
        if (!p.rgba) {
            const header = atob(p.png.slice('data:image/png;base64,'.length).slice(0, 44));
            const bytes = Uint8Array.from(header, c => c.charCodeAt(0));
            if (bytes.length < 24 || [...bytes.slice(0, 8)].join(',') !== '137,80,78,71,13,10,26,10' || header.slice(12, 16) !== 'IHDR') throw new Error('Invalid PNG header');
            const view = new DataView(bytes.buffer);
            if (view.getUint32(16) !== p.width || view.getUint32(20) !== p.height) throw new Error('Print page dimensions do not match PNG header');
        }
    }
    if (totalPixels > 64_000_000) throw new Error('Print job exceeds pixel budget');
}

export async function preparePrint({ title = 'Document', pages, signal } = {}) {
    validatePrintJob(pages);
    if (typeof title !== 'string' || title.length > 256) throw new Error('Invalid print title');
    if (signal?.aborted) throw new DOMException('Cancelled', 'AbortError');
    const { host, dialog } = modal(title), urls = [], footer = node('footer', undefined, dialog);
    const status = node('span', 'Preparing pages…', footer); status.setAttribute('role', 'status'); status.className = 'spacer';
    const frame = document.createElement('iframe'); frame.title = 'Print preview'; frame.setAttribute('sandbox', 'allow-same-origin allow-modals'); dialog.insertBefore(frame, footer);
    let closed = false, invoked = false; const abort = () => close();
    function close() { if (closed) return; closed = true; signal?.removeEventListener('abort', abort); for (const u of urls) URL.revokeObjectURL(u); dialog.close(); host.remove(); }
    const print = () => { if (closed) throw new Error('Print preview closed'); invoked = true; status.textContent = 'Browser print dialog requested'; frame.contentWindow.focus(); frame.contentWindow.print(); };
    button('Close', footer, close); const printButton = button('Print…', footer, print, true); printButton.disabled = true;
    dialog.addEventListener('cancel', e => { e.preventDefault(); close(); }); signal?.addEventListener('abort', abort, { once: true });
    try {
        const doc = frame.contentDocument;
        doc.open(); doc.write('<!doctype html><html><head><meta charset="utf-8"><title></title></head><body></body></html>'); doc.close();
        doc.title = title;
        const css = node('style', 'html,body{margin:0;padding:0}body{background:#e7edf5}section{margin:12px auto;background:white;overflow:hidden}img{width:100%;height:100%;display:block;object-fit:contain}@media print{body{background:white}section{margin:0;break-after:page}section:last-child{break-after:auto}}', doc.head);
        const images = [];
        for (let i = 0; i < pages.length; i++) {
            if (closed) throw new DOMException('Cancelled', 'AbortError');
            const p = pages[i], page = node('section', undefined, doc.body);
            page.style.cssText = `width:${p.widthMm}mm;height:${p.heightMm}mm;page:wfpage${i}`;
            css.textContent += `@page wfpage${i}{size:${p.widthMm}mm ${p.heightMm}mm;margin:0}`;
            const image = node('img', undefined, page); image.alt = 'Page ' + (i + 1);
            if (p.rgba) {
                const canvas = document.createElement('canvas'); canvas.width = p.width; canvas.height = p.height;
                canvas.getContext('2d').putImageData(new ImageData(new Uint8ClampedArray(p.rgba), p.width, p.height), 0, 0);
                const blob = await new Promise((resolve, reject) => canvas.toBlob(b => b ? resolve(b) : reject(new Error('Page encoding failed')), 'image/png'));
                if (closed) throw new DOMException('Cancelled', 'AbortError');
                const url = URL.createObjectURL(blob); urls.push(url); image.src = url;
            } else image.src = p.png;
            images.push(image.decode().then(() => { if (image.naturalWidth !== p.width || image.naturalHeight !== p.height) throw new Error('Print page dimensions do not match pixels'); }));
        }
        await Promise.all(images); if (closed) throw new DOMException('Cancelled', 'AbortError');
        frame.contentWindow.addEventListener('afterprint', () => { if (!closed) status.textContent = 'Print dialog closed. Printer delivery cannot be confirmed here.'; });
        status.textContent = pages.length + (pages.length === 1 ? ' page ready' : ' pages ready'); printButton.disabled = false;
        return { print, close, get closed() { return closed; }, get dialogRequested() { return invoked; }, element: host };
    } catch (e) { close(); throw e; }
}
