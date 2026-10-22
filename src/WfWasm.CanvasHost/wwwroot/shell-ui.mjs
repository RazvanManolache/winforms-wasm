import {AppSessions, channel, runtimeOriginFor} from './shell-core.mjs';

const $ = id => document.getElementById(id);
const time = ms => (ms / 1000).toFixed(2) + ' s';
const mb = bytes => (bytes / 1e6).toFixed(2) + ' MB';
let catalog = [], group = 'all', selected = null, sessions;

function status(title, message, kind = '', actions = false) {
    $('session-status').hidden = false;
    $('session-status').className = 'stage-overlay ' + kind;
    $('status-title').textContent = title;
    $('status-message').textContent = message;
    $('recovery-actions').hidden = !actions;
}
function showPreparation(app) {
    const manual = app.setupKind === 'manual';
    const title = `${app.label} ${manual ? 'needs app files' : 'needs preparation'}`;
    const message = manual ? app.setupNote : 'Prepare this optional benchmark locally and reload. ' + (app.setupNote ?? '');
    status(title, message + ' You can choose another app.');
}
function renderSession(s) {
    const m = s?.metrics;
    $('shell-load').textContent = s ? time((s.ended ?? performance.now()) - s.started) : '—';
    $('shell-phase').textContent = s?.phase ?? 'Stopped';
    $('shell-data').textContent = mb(m?.bytes ?? 0);
    $('shell-network').textContent = mb(m?.wireBytes ?? 0);
    $('shell-cache').textContent = `${m?.httpCacheHits ?? 0} HTTP · ${m?.memoryReuse ?? 0} memory`;
    $('shell-boot').textContent = m?.bootMs ? time(m.bootMs) : '—';
    if (selected?.available === false) {
        showPreparation(selected);
        return;
    }
    if (!s) { status('App stopped', 'Choose an app from your library, or restart this one.', '', !!selected); return; }
    if (s.status === 'failed') status(`${s.app.label} could not continue`, s.phase + ' You can restart it or choose another app.', 'error', true);
    else if (sessions.isBusy()) status(`${s.app.label} is busy`, 'The app is not responding. Your library is still available; switch apps or restart this session.', 'busy', true);
    else if (s.status === 'ready') $('session-status').hidden = true;
    else status(`Opening ${s.app.label}`, s.phase + ' · You can choose another app at any time.');
}
function renderLibrary() {
    const query = $('app-search').value.trim().toLowerCase();
    const entries = catalog.filter(a => (group === 'all' || (a.category === 'benchmarks' ? 'benchmarks' : a.slug.startsWith('syncfusion-') ? 'syncfusion' : a.slug.startsWith('telerik-') ? 'telerik' : 'everyday') === group) &&
        `${a.label} ${a.sourceLabel ?? ''} ${a.family ?? ''} ${a.purpose ?? ''} ${(a.components ?? []).join(' ')}`.toLowerCase().includes(query));
    $('app-list').replaceChildren(...entries.map(app => {
        const button = document.createElement('button');
        button.className = 'app-item' + (selected?.slug === app.slug ? ' active' : '');
        button.setAttribute('aria-label', app.label);
        button.setAttribute('aria-current', selected?.slug === app.slug ? 'page' : 'false');
        const icon = document.createElement('span'); icon.className = 'app-icon ' + (app.slug.startsWith('telerik-') || app.slug.startsWith('syncfusion-') ? 'violet' : 'teal'); icon.textContent = app.label.replace(/^(Telerik|Syncfusion): /, '').slice(0,1);
        const copy = document.createElement('span'); copy.className = 'app-item-copy';
        const title = document.createElement('strong'); title.textContent = app.label.replace(/^(Telerik|Syncfusion): /, '');
        const detail = document.createElement('small'); detail.textContent = app.category === 'benchmarks' ? `${app.setupKind === 'manual' ? 'App files not prepared' : app.available === false ? 'Needs preparation' : 'Prepared · unverified'} · ${app.family}` : app.slug.startsWith('syncfusion-') ? `${app.family ?? 'Syncfusion'} · experimental` : app.slug.startsWith('telerik-') ? 'Telerik demo' : app.slug === 'smoke' ? 'Built-in demo' : 'WinForms application';
        if (app.purpose) button.title = app.purpose;
        copy.append(title, detail); button.append(icon, copy);
        button.addEventListener('click', () => select(app));
        return button;
    }));
    if (!entries.length) { const empty = document.createElement('p'); empty.className = 'empty-library'; empty.textContent = 'No apps match your search.'; $('app-list').append(empty); }
}
function select(app, push = true) {
    selected = app;
    $('app-title').textContent = app.label.replace(/^(Telerik|Syncfusion): /, '');
    document.title = app.label + ' · App Studio';
    $('app-source').textContent = app.sourceLabel ?? '';
    $('source-link').hidden = !app.sourceUrl;
    if (app.sourceUrl) $('source-link').href = app.sourceUrl;
    $('error-link').href = '/errors?app=' + encodeURIComponent(app.slug);
    if (push) history.pushState(null, '', '/app/' + encodeURIComponent(app.slug));
    if (matchMedia('(max-width:760px)').matches) { $('app').classList.remove('menu-open'); $('toggle-menu').setAttribute('aria-expanded','false'); }
    renderLibrary();
    if (app.available === false) {
        sessions.stop();
        showPreparation(app);
        return;
    }
    try { sessions.select(app); } catch (error) { status('App could not start', error.message, 'error', true); }
}
function fromUrl() {
    const raw = /^\/app\/([^/]+)/.exec(location.pathname)?.[1];
    let slug;
    try { slug = raw ? decodeURIComponent(raw).toLowerCase() : 'smoke'; } catch { slug = ''; }
    const app = catalog.find(a => a.slug === slug || a.appFolder === slug || a.slug.replaceAll('-','') === slug.replaceAll('-',''));
    if (app) select(app, false);
    else { sessions.stop(); selected = null; renderLibrary(); status('Unknown app', 'Choose an app from your library.'); }
}
function command(type) {
    const s = sessions?.current;
    s?.frame.contentWindow.postMessage({channel, type, session:s.id, app:s.app.slug}, s.origin);
}
$('app-search').addEventListener('input', renderLibrary);
for (const tab of document.querySelectorAll('[data-group]')) tab.addEventListener('click', () => {
    group = tab.dataset.group;
    for (const item of document.querySelectorAll('[data-group]')) { item.classList.toggle('selected', item === tab); item.setAttribute('aria-pressed', String(item === tab)); }
    renderLibrary();
});
$('toggle-menu').addEventListener('click', () => {
    $('app').classList.toggle('menu-open');
    const open = getComputedStyle(document.querySelector('.app-sidebar')).display !== 'none';
    $('toggle-menu').setAttribute('aria-expanded', String(open));
});
for (const id of ['retry-app','reload-app']) $(id).addEventListener('click', () => { if (selected && sessions) select(selected, false); });
for (const id of ['stop-app','close-app']) $(id).addEventListener('click', () => sessions?.stop());
$('compare-app').addEventListener('click', () => command('reference'));
$('snapshot-app').addEventListener('click', () => command('snapshot'));
window.addEventListener('message', event => sessions?.accept(event));
window.addEventListener('popstate', () => { if (sessions) fromUrl(); });

// Utility routes retain the standalone Blazor UI; a runtime deep-link reload
// returns to the embedded entry point rather than nesting another shell.
const params = new URLSearchParams(location.search);
if (['/dashboard','/fxlab'].includes(location.pathname)) {
    location.replace('/runtime.html?page=' + encodeURIComponent(location.pathname));
} else if (params.get('runtime') === '1') {
    params.set('app', decodeURIComponent(location.pathname.split('/')[2] || 'smoke'));
    location.replace('/runtime.html?' + params);
} else {
    try {
        const response = await fetch('/api/preview-config');
        if (!response.ok) throw new Error(`Library request failed (HTTP ${response.status}). Reload to try again.`);
        const config = await response.json();
        catalog = config.apps;
        $('app-count').textContent = String(catalog.length);
        $('app-list').setAttribute('aria-busy','false');
        runtimeOriginFor(location.origin, config.runtimeOrigin); // Validate before enabling the library.
        sessions = new AppSessions({runtimeOrigin:slug => runtimeOriginFor(location.origin, config.runtimeOrigin, slug), parentOrigin:location.origin,
            mount(url, app) {
                const frame = document.createElement('iframe');
                frame.className = 'app-runtime'; frame.title = app.label + ' app session';
                frame.referrerPolicy = 'origin';
                // Separate runtime/storage; top-level navigation and popups stay disabled.
                frame.setAttribute('sandbox','allow-scripts allow-same-origin allow-downloads');
                frame.src = url; $('runtime-host').replaceChildren(frame); return frame;
            }, unmount: frame => frame.remove(), changed: renderSession});
        fromUrl();
        setInterval(() => renderSession(sessions.current), 500);
    } catch (error) { renderLibrary(); status('Workspace could not start', error.message, 'error'); }
}
