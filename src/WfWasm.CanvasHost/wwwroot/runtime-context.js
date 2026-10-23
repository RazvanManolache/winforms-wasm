(() => {
    const params = new URLSearchParams(location.search);
    const pathApp = /^\/app\/([^/]+)/.exec(location.pathname);
    const app = (params.get('app') || (pathApp && decodeURIComponent(pathApp[1])) || (params.has('page') ? 'workspace' : 'smoke')).slice(0,120);
    const session = (params.get('session') || crypto.randomUUID()).slice(0,120);
    const label = (params.get('label') || app).slice(0,200);
    let parentOrigin = null, failed = false;
    try {
        const requested = new URL(params.get('parentOrigin'));
        if (window.parent !== window && requested.origin === new URL(document.referrer).origin && requested.origin !== location.origin)
            parentOrigin = requested.origin;
    } catch { }
    const send = (type, payload = {}) => {
        if (parentOrigin) window.parent.postMessage({...payload, channel:'wfwasm-runtime-v1', app, session, type}, parentOrigin);
    };
    window.wfwasmRuntime = Object.freeze({app, session, label, send, fail(message) {
        if (failed) return;
        failed = true;
        send('failed', {message: String(message || 'App runtime failed').slice(0,500)});
    }});
    if (parentOrigin) document.documentElement.classList.add('embedded-runtime');
    if (location.pathname === '/runtime.html' && !params.has('page')) {
        params.delete('app');
        params.set('runtime','1');
        history.replaceState(null, '', '/app/' + encodeURIComponent(app) + '?' + params);
    } else if (location.pathname === '/runtime.html' && ['/dashboard','/fxlab'].includes(params.get('page'))) {
        history.replaceState(null, '', params.get('page'));
    }
    send('phase', {phase:'Loading browser runtime'});
    setInterval(() => send('heartbeat'), 500);
    window.addEventListener('message', event => {
        const d = event.data;
        if (!parentOrigin || event.origin !== parentOrigin || event.source !== window.parent || !d ||
            d.channel !== 'wfwasm-runtime-v1' || d.session !== session || d.app !== app) return;
        if (d.type === 'snapshot') window.preview?.snapshot();
        if (d.type === 'reference') document.getElementById('runtime-reference-toggle')?.click();
        if (d.type === 'diagnostics') {
            // Shell diagnostics toggle: explicit enable/disable for A/B
            // runs. _dotNet is the Home component ref stored by fitCanvas.
            try { window.wfwasm._dotNet?.invokeMethodAsync('PerfSetEnabled', !!d.enabled); } catch { }
        }
    });
})();
