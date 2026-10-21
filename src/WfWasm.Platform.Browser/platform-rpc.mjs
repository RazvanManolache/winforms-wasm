import { showFileDialog, preparePrint } from './platform-ui.mjs';

// Shell owns the client/cookie and browser UI; the untrusted app receives only this capability.
// Call once per iframe/runtime lifetime. No wildcard origins, shared sessions or process-global client.
export function bindPlatformHost({ client, childWindow, childOrigin, session, hostWindow = window, onError = console.error }) {
    if (!childWindow || !/^https?:\/\//.test(childOrigin) || new URL(childOrigin).origin !== childOrigin || typeof session !== 'string' || session.length < 16)
        throw new Error('Exact child origin and a fresh session nonce are required');
    const abort = new AbortController(), previews = new Set(); let disposed = false, lastId = 0, inFlight = 0, modalBusy = false;
    async function execute(op, a) {
        for (const preview of previews) if (preview.closed) previews.delete(preview);
        switch (op) {
            case 'registry.snapshot': return { keys: [...client.registry.keys], values: [...client.registry.values.values()].map(v => structuredClone(v)) };
            case 'registry.set': client.registry.setValue(a.key, a.name, a.kind, a.data); await client.registry.flush(); return null;
            case 'registry.createKey': client.registry.createKey(a.key); await client.registry.flush(); return null;
            case 'registry.deleteValue': client.registry.deleteValue(a.key, a.name, a.throwIfMissing); await client.registry.flush(); return null;
            case 'registry.deleteKey': client.registry.deleteKey(a.key, a.recursive, a.throwIfMissing); await client.registry.flush(); return null;
            case 'registry.flush': await client.registry.flush(); return null;
            case 'files.mounts': return client.mounts();
            case 'files.list': return client.list(a.path, a.offset ?? 0);
            case 'files.read': return client.read(a.path);
            case 'files.write':
                if (!(a.bytes instanceof Uint8Array) || a.bytes.byteLength > 16 * 1024 * 1024) throw new Error('Invalid file bytes');
                await client.write(a.path, a.bytes); return null;
            case 'files.mkdir': await client.mkdir(a.path); return null;
            case 'files.delete': await client.delete(a.path); return null;
            case 'dialog.open':
                if (modalBusy || previews.size) throw new Error('Another platform dialog is already open');
                modalBusy = true;
                try { return await showFileDialog(client, { ...a, signal: abort.signal }); }
                finally { modalBusy = false; }
            case 'print.preview':
                if (modalBusy || previews.size) throw new Error('Another platform dialog is already open');
                // The preview stays user-owned until close/disposal; do not announce a printed job.
                if (previews.size >= 4) throw new Error('Too many print previews');
                {
                    modalBusy = true;
                    try { const preview = await preparePrint({ title: a.title, pages: a.pages, signal: abort.signal }); previews.add(preview); return { previewReady: true, printerDeliveryConfirmed: false }; }
                    finally { modalBusy = false; }
                }
            default: throw new Error('Unsupported platform operation');
        }
    }
    const listener = async e => {
        const m = e.data;
        if (disposed || e.source !== childWindow || e.origin !== childOrigin || m?.type !== 'wf-platform-request' || m.version !== 1 || m.session !== session) return;
        if (!Number.isSafeInteger(m.id) || m.id <= lastId) return;
        lastId = m.id;
        const reply = result => { if (!disposed) childWindow.postMessage({ type: 'wf-platform-response', version: 1, session, id: m.id, ...result }, childOrigin); };
        if (inFlight >= 32) { reply({ ok: false, error: 'Too many pending platform requests' }); return; }
        inFlight++;
        try { reply({ ok: true, value: await execute(m.op, m.args ?? {}) }); }
        catch (error) { onError(error); reply({ ok: false, error: error.message }); }
        finally { inFlight--; }
    };
    hostWindow.addEventListener('message', listener);
    return {
        async dispose() {
            if (disposed) return; disposed = true; hostWindow.removeEventListener('message', listener); abort.abort();
            for (const p of previews) p.close(); previews.clear();
            try { await client.dispose(); } catch (error) { onError(error); client.abortSession(); throw error; }
        }
    };
}

// Runtime-side asynchronous transport. The shell provides its exact origin and session nonce at boot.
export class PlatformRpcClient {
    constructor({ parentWindow, parentOrigin, session, runtimeWindow = window, timeoutMs = 120000 }) {
        if (!parentWindow || !/^https?:\/\//.test(parentOrigin) || new URL(parentOrigin).origin !== parentOrigin || typeof session !== 'string' || session.length < 16) throw new Error('Invalid platform parent binding');
        this.parent = parentWindow; this.origin = parentOrigin; this.session = session; this.runtime = runtimeWindow; this.timeoutMs = timeoutMs;
        this.id = 0; this.pending = new Map(); this.disposed = false;
        this.listener = e => {
            const m = e.data;
            if (e.source !== this.parent || e.origin !== this.origin || m?.type !== 'wf-platform-response' || m.version !== 1 || m.session !== this.session) return;
            const p = this.pending.get(m.id); if (!p) return; this.pending.delete(m.id); clearTimeout(p.timer);
            m.ok ? p.resolve(m.value) : p.reject(new Error(m.error ?? 'Platform request failed'));
        };
        runtimeWindow.addEventListener('message', this.listener);
    }
    call(op, args = {}) {
        if (this.disposed) return Promise.reject(new Error('Platform runtime disposed'));
        if (this.pending.size >= 32) return Promise.reject(new Error('Too many pending platform requests'));
        const id = ++this.id;
        return new Promise((resolve, reject) => {
            const timer = setTimeout(() => { this.pending.delete(id); reject(new Error('Platform request timed out')); }, this.timeoutMs);
            this.pending.set(id, { resolve, reject, timer });
            this.parent.postMessage({ type: 'wf-platform-request', version: 1, session: this.session, id, op, args }, this.origin);
        });
    }
    dispose() { this.disposed = true; this.runtime.removeEventListener('message', this.listener); for (const p of this.pending.values()) { clearTimeout(p.timer); p.reject(new Error('Platform runtime disposed')); } this.pending.clear(); }
}

// Optional Blazor entry point: import this module, then request an IJSObjectReference.
// Resolve parentOrigin/session from the shell's already-validated boot context.
export function createRuntimePlatform(parentOrigin, session) {
    if (!globalThis.DotNet?.createJSObjectReference) throw new Error('Blazor JS interop is not available');
    const rpc = new PlatformRpcClient({ parentWindow: window.parent, parentOrigin, session });
    return globalThis.DotNet.createJSObjectReference({ call: (op, args) => rpc.call(op, args), dispose: () => rpc.dispose() });
}
