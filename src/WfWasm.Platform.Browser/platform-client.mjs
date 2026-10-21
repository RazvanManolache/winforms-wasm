export function registryKey(key) {
    if (typeof key !== 'string' || !key || key.length > 1024 || /[\x00-\x1f\x7f]/.test(key)) throw new Error('Invalid registry key');
    const parts = key.replaceAll('/', '\\').replace(/\\+$/, '').split('\\');
    const aliases = { HKEY_CURRENT_USER: 'HKCU', HKEY_LOCAL_MACHINE: 'HKLM', HKEY_CLASSES_ROOT: 'HKCR', HKEY_USERS: 'HKU', HKEY_CURRENT_CONFIG: 'HKCC' };
    parts[0] = aliases[parts[0].toUpperCase()] ?? parts[0].toUpperCase();
    if (!['HKCU', 'HKLM', 'HKCR', 'HKU', 'HKCC'].includes(parts[0]) || parts.some(p => !p || p === '.' || p === '..')) throw new Error('Invalid registry key');
    return parts.join('\\').toUpperCase();
}

export function virtualPath(path) {
    const match = /^([a-zA-Z0-9_-]{1,100}):\/(.*)$/.exec(path);
    if (!match) throw new Error('Expected a virtual path such as workspace:/report.txt');
    const relative = match[2].replaceAll('\\', '/');
    if (relative.length > 1024 || relative && relative.split('/').some(p => !p || p === '.' || p === '..' || p.length > 180 || /[\x00-\x1f\x7f:%<>"|?*]/.test(p) || /[. ]$/.test(p) || /^(con|prn|aux|nul|com[0-9]|lpt[0-9])(\.|$)/i.test(p))) throw new Error('Invalid virtual path');
    return { mount: match[1], path: relative };
}

function valueName(name) {
    if (typeof name !== 'string' || name.length > 256 || /[\x00-\x1f\x7f]/.test(name)) throw new Error('Invalid value name');
    return name.toUpperCase();
}

export function validateRegistryValue(kind, data) {
    const valid = kind === 'String' || kind === 'ExpandString' ? typeof data === 'string'
        : kind === 'DWord' ? Number.isInteger(data) && data >= -2147483648 && data <= 4294967295
        : kind === 'QWord' ? typeof data === 'string' && /^-?\d+$/.test(data) && BigInt(data) >= -9223372036854775808n && BigInt(data) <= 9223372036854775807n
        : kind === 'MultiString' ? Array.isArray(data) && data.every(s => typeof s === 'string')
        : kind === 'Binary' || kind === 'None' ? typeof data === 'string' && /^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(data)
        : false;
    if (!valid || new TextEncoder().encode(JSON.stringify(data)).length > 65536) throw new Error('Invalid or oversized registry value');
}

export class RegistryMirror {
    constructor(snapshot, send, onError = () => {}) {
        this.keys = new Set(snapshot.keys.map(registryKey));
        this.values = new Map(snapshot.values.map(v => [this.id(v.key, v.name), structuredClone(v)]));
        this.send = send; this.onError = onError; this.queue = []; this.inFlight = null; this.closed = false;
    }
    id(key, name) { return registryKey(key) + '\0' + valueName(name); }
    get pendingWrites() { return this.queue.length; }
    hasKey(key) { return this.keys.has(registryKey(key)); }
    getValue(key, name = '', defaultValue = null) { return structuredClone(this.values.get(this.id(key, name))?.data ?? defaultValue); }
    getValueKind(key, name = '') { return this.values.get(this.id(key, name))?.kind ?? null; }
    getValueNames(key) { key = registryKey(key); return [...this.values.values()].filter(v => v.key === key).map(v => v.name); }
    getSubKeyNames(key) {
        const prefix = registryKey(key) + '\\';
        return [...this.keys].filter(k => k.startsWith(prefix) && !k.slice(prefix.length).includes('\\')).map(k => k.slice(prefix.length));
    }
    createKey(key) {
        this.checkOpen(); key = registryKey(key);
        const parts = key.split('\\'); for (let i = 1; i <= parts.length; i++) this.keys.add(parts.slice(0, i).join('\\'));
        this.enqueue('PUT', '/registry/key?key=' + encodeURIComponent(key));
        return key;
    }
    setValue(key, name, kind, data) {
        this.checkOpen(); key = registryKey(key); valueName(name); validateRegistryValue(kind, data);
        this.createKey(key);
        const entry = { key, name, kind, data: structuredClone(data) };
        this.values.set(this.id(key, name), entry);
        this.enqueue('PUT', '/registry/value?key=' + encodeURIComponent(key), { name, kind, data });
    }
    deleteValue(key, name = '', throwIfMissing = false) {
        this.checkOpen(); const found = this.values.delete(this.id(key, name));
        if (!found && throwIfMissing) throw new Error('Registry value not found');
        if (found) this.enqueue('DELETE', '/registry/value?key=' + encodeURIComponent(registryKey(key)) + '&name=' + encodeURIComponent(name), undefined, true);
    }
    deleteKey(key, recursive = false, throwIfMissing = false) {
        this.checkOpen(); key = registryKey(key);
        if (!key.includes('\\')) throw new Error('Cannot delete a hive');
        if (!this.keys.has(key)) { if (throwIfMissing) throw new Error('Registry key not found'); return; }
        if (!recursive && this.getSubKeyNames(key).length) throw new Error('Registry key has subkeys');
        for (const k of this.keys) if (k === key || k.startsWith(key + '\\')) this.keys.delete(k);
        for (const [id, value] of this.values) if (value.key === key || value.key.startsWith(key + '\\')) this.values.delete(id);
        this.enqueue('DELETE', '/registry/key?key=' + encodeURIComponent(key) + '&recursive=' + recursive, undefined, true);
    }
    checkOpen() { if (this.closed) throw new Error('Registry mirror is closed'); }
    enqueue(method, path, body, missingOK = false) {
        this.queue.push({ method, path, body: structuredClone(body), missingOK });
        queueMicrotask(() => { if (!this.closed) this.flush().catch(e => this.onError(e)); });
    }
    flush() {
        if (this.inFlight) return this.inFlight;
        this.inFlight = (async () => {
            while (this.queue.length) {
                const op = this.queue[0]; await this.send(op.path, op.method, op.body, op.missingOK); this.queue.shift();
            }
        })().finally(() => { this.inFlight = null; });
        return this.inFlight;
    }
}

export class PlatformClient {
    constructor(app, { baseUrl = '/api/platform', fetchImpl = globalThis.fetch.bind(globalThis), onStorageError = console.error } = {}) {
        if (!/^[a-zA-Z0-9_-]{1,100}$/.test(app)) throw new Error('Invalid app identifier');
        this.app = app; this.baseUrl = baseUrl.replace(/\/$/, ''); this.fetchImpl = fetchImpl;
        this.onStorageError = onStorageError; this.token = null; this.devices = new Map(); this.disposed = false;
        this.abort = new AbortController();
    }
    async start() {
        if (this.disposed || this.token) throw new Error('Platform session already started or disposed');
        const session = await this.request('/session', 'POST', { app: this.app });
        this.token = session.token; this.user = session.user;
        this.registry = new RegistryMirror(await this.request('/registry'), (...args) => this.request(...args), this.onStorageError);
        return this;
    }
    async request(path, method = 'GET', body, missingOK = false) {
        if (this.disposed) throw new Error('Platform client disposed');
        const headers = { 'X-Wf-Platform': '1' }; if (this.token) headers['X-Wf-Session'] = this.token;
        if (body !== undefined) headers['Content-Type'] = 'application/json';
        const r = await this.fetchImpl(this.baseUrl + path, { method, headers, credentials: 'same-origin', cache: 'no-store',
            signal: this.abort.signal, body: body === undefined ? undefined : JSON.stringify(body) });
        if (missingOK && r.status === 404 || r.status === 204) return null;
        if (!r.ok) throw new Error('Platform request failed (' + r.status + '): ' + path.split('?')[0]);
        return r.json();
    }
    async mounts() {
        const mounts = await this.request('/mounts');
        for (const [id, handle] of this.devices) mounts.push({ id, label: 'My device · ' + handle.name, isFile: handle.kind === 'file',
            writable: !!handle.queryPermission && await handle.queryPermission({ mode: 'readwrite' }) === 'granted', device: true });
        return mounts;
    }
    query(path) { const p = virtualPath(path); return '?mount=' + encodeURIComponent(p.mount) + '&path=' + encodeURIComponent(p.path); }
    async list(path, offset = 0) {
        const p = virtualPath(path);
        if (this.devices.has(p.mount)) {
            const root = this.devices.get(p.mount); await this.permission(root, 'read');
            if (root.kind === 'file') return { entries: [{ name: root.name, path: root.name, isDirectory: false, length: (await root.getFile()).size }], nextOffset: null };
            let dir = root;
            for (const name of p.path.split('/').filter(Boolean)) dir = await dir.getDirectoryHandle(name);
            const entries = [];
            for await (const [name, handle] of dir.entries()) {
                entries.push({ name, path: p.path ? p.path + '/' + name : name, isDirectory: handle.kind === 'directory', length: 0 });
                if (entries.length > 10000) throw new Error('Folder has too many entries');
            }
            entries.sort((a, b) => Number(b.isDirectory) - Number(a.isDirectory) || a.name.localeCompare(b.name));
            return { entries: entries.slice(offset, offset + 256), nextOffset: offset + 256 < entries.length ? offset + 256 : null };
        }
        return this.request('/files/list' + this.query(path) + '&offset=' + offset);
    }
    async permission(handle, mode) {
        if (handle.queryPermission && await handle.queryPermission({ mode }) !== 'granted') throw new Error('Device permission required. Use the device access button again.');
    }
    async deviceFile(path, create = false) {
        const p = virtualPath(path), root = this.devices.get(p.mount);
        if (!root) throw new Error('Device grant not found');
        await this.permission(root, create ? 'readwrite' : 'read');
        if (root.kind === 'file') { if (p.path !== root.name) throw new Error('File not granted'); return root; }
        const parts = p.path.split('/'); const leaf = parts.pop(); if (!leaf) throw new Error('File name required');
        let dir = root; for (const name of parts) dir = await dir.getDirectoryHandle(name, { create });
        return dir.getFileHandle(leaf, { create });
    }
    async read(path) {
        const p = virtualPath(path);
        if (this.devices.has(p.mount)) {
            const file = await (await this.deviceFile(path)).getFile();
            if (file.size > 16 * 1024 * 1024) throw new Error('File exceeds transfer limit');
            return new Uint8Array(await file.arrayBuffer());
        }
        const r = await this.fetchImpl(this.baseUrl + '/files/content' + this.query(path), { headers: { 'X-Wf-Platform': '1', 'X-Wf-Session': this.token }, credentials: 'same-origin', cache: 'no-store', signal: this.abort.signal });
        if (!r.ok) throw new Error('File read failed (' + r.status + ')');
        return new Uint8Array(await r.arrayBuffer());
    }
    async write(path, bytes) {
        const p = virtualPath(path);
        if (bytes.byteLength > 16 * 1024 * 1024) throw new Error('File exceeds transfer limit');
        if (this.devices.has(p.mount)) {
            const writer = await (await this.deviceFile(path, true)).createWritable();
            try { await writer.write(bytes); await writer.close(); } catch (e) { await writer.abort(); throw e; }
            return;
        }
        const r = await this.fetchImpl(this.baseUrl + '/files/content' + this.query(path), { method: 'PUT', headers: { 'X-Wf-Platform': '1', 'X-Wf-Session': this.token, 'Content-Type': 'application/octet-stream' }, credentials: 'same-origin', signal: this.abort.signal, body: bytes });
        if (!r.ok) throw new Error('File write failed (' + r.status + ')');
    }
    mkdir(path) { return this.request('/files/directory' + this.query(path), 'POST'); }
    delete(path) { return this.request('/files/content' + this.query(path), 'DELETE'); }
    // Invoke directly from a DOM click; no network await may precede the native picker.
    async grantDevice(kind = 'file', multiple = false, pickerOptions = {}) {
        const handles = kind === 'directory' ? [await globalThis.showDirectoryPicker({ mode: 'read' })]
            : await globalThis.showOpenFilePicker({ ...pickerOptions, multiple });
        if (this.disposed) throw new Error('Platform session ended while choosing device files');
        return handles.map(handle => {
            const id = 'device-' + crypto.randomUUID(); this.devices.set(id, handle);
            return { path: id + ':/' + (handle.kind === 'file' ? handle.name : ''), name: handle.name, isDirectory: handle.kind === 'directory' };
        });
    }
    async requestDeviceWrite(path) {
        const handle = this.devices.get(virtualPath(path).mount); if (!handle?.requestPermission) throw new Error('Device grant unavailable');
        return await handle.requestPermission({ mode: 'readwrite' }) === 'granted';
    }
    revokeDevice(path) { this.devices.delete(virtualPath(path).mount); }
    // Explicit emergency teardown for a crashed/replaced runtime. Dirty writes are NOT acknowledged as saved.
    abortSession() { this.registry && (this.registry.closed = true); this.devices.clear(); this.disposed = true; this.abort.abort(); }
    async dispose() {
        if (this.disposed) return;
        // A failed flush remains observable; do not claim successful durable shutdown.
        await this.registry?.flush();
        await this.request('/session', 'DELETE');
        this.registry && (this.registry.closed = true); this.devices.clear(); this.disposed = true; this.abort.abort();
    }
}
