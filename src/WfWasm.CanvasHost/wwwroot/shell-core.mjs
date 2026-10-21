export const channel = 'wfwasm-runtime-v1';

// A different port is still the same site. Loopback aliases let Chromium
// isolate the runtime's synchronous assembly resolution from the menu.
export function runtimeOriginFor(shellOrigin, configured, appSlug) {
    const shell = new URL(shellOrigin);
    let target;
    if (configured) target = new URL(configured);
    else {
        target = new URL(shell.origin);
        if (shell.hostname === '127.0.0.1' || shell.hostname === '[::1]') target.hostname = 'localhost';
        else if (shell.hostname === 'localhost') target.hostname = '127.0.0.1';
        else throw new Error('Configure Preview:RuntimeOrigin on a separate site to isolate app execution.');
    }
    if (!['http:', 'https:'].includes(target.protocol) || target.hostname === shell.hostname ||
        target.username || target.password || target.pathname !== '/' || target.search || target.hash ||
        (shell.protocol === 'https:' && target.protocol !== 'https:'))
        throw new Error('The app runtime must use a separate HTTP(S) site, without a path or credentials.');
    // Each loopback app gets its own site as well as its own runtime. A
    // spinning detached iframe must not occupy the next app's renderer.
    if (appSlug && ['localhost','127.0.0.1','[::1]'].includes(target.hostname)) {
        if (!/^[a-z0-9-]{1,63}$/.test(appSlug)) throw new Error('Invalid app runtime site.');
        target.hostname = appSlug + '.localhost';
    }
    return target.origin;
}

export class AppSessions {
    constructor({runtimeOrigin, parentOrigin, mount, unmount, changed, now = () => performance.now(), id = () => crypto.randomUUID()}) {
        Object.assign(this, {runtimeOrigin, parentOrigin, mount, unmount, changed, now, id});
        this.current = null;
        this.quarantined = new Set();
    }
    select(app) {
        if (this.isBusy()) this.quarantined.add(this.current.origin);
        let origin = typeof this.runtimeOrigin === 'function' ? this.runtimeOrigin(app.slug) : this.runtimeOrigin;
        const sessionId = this.id();
        if (this.quarantined.has(origin)) {
            const retry = new URL(origin);
            if (retry.hostname.endsWith('.localhost')) {
                retry.hostname = 'retry-' + sessionId.replace(/[^a-z0-9]/gi,'').slice(0,8) + '--' + app.slug.slice(0,40) + '.localhost';
                origin = retry.origin;
            }
        }
        this.stop();
        const s = this.current = {app, id: sessionId, status: 'loading', phase: 'Starting isolated runtime',
            started: this.now(), heartbeat: this.now(), metrics: null, frame: null, origin};
        const url = new URL('/runtime.html', origin);
        for (const [key, value] of Object.entries({app: app.slug, label: app.label, session: s.id, parentOrigin: this.parentOrigin})) url.searchParams.set(key, value);
        s.frame = this.mount(url.href, app);
        this.changed(s);
        return s;
    }
    stop() {
        const s = this.current;
        if (this.isBusy()) this.quarantined.add(s.origin);
        this.current = null; // Reject late messages before detaching the old frame.
        if (s?.frame) this.unmount(s.frame);
        this.changed(null);
    }
    accept(event) {
        const s = this.current, d = event.data;
        if (!s || event.origin !== s.origin || event.source !== s.frame?.contentWindow ||
            !d || d.channel !== channel || d.session !== s.id || d.app !== s.app.slug ||
            !['heartbeat', 'phase', 'metrics', 'ready', 'failed'].includes(d.type)) return false;
        s.heartbeat = this.now();
        if (d.type === 'metrics' && d.metrics && typeof d.metrics === 'object') {
            s.metrics = Object.fromEntries(['bootMs','loadMs','bytes','wireBytes','httpCacheHits','memoryReuse','requests'].map(key =>
                [key, Number.isFinite(d.metrics[key]) && d.metrics[key] >= 0 ? Math.min(d.metrics[key], 1e15) : 0]));
        }
        if (d.type === 'phase' && s.status === 'loading') s.phase = String(d.phase ?? '').slice(0, 300);
        if (d.type === 'ready' && s.status !== 'failed') { s.status = 'ready'; s.phase = 'App ready'; s.ended = this.now(); }
        if (d.type === 'failed') { s.status = 'failed'; s.phase = String(d.message || 'App failed').slice(0, 500); s.ended = this.now(); }
        this.changed(s);
        return true;
    }
    isBusy() { return !!this.current && this.current.status !== 'failed' && this.now() - this.current.heartbeat > 8000; }
}
