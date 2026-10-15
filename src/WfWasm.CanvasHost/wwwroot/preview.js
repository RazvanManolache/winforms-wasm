(() => {
    const p = window.preview = {};
    const started = performance.now();
    let boot = null, bootBytes = 0, selection = null, timer;
    const totals = { wire: 0, body: 0, cached: 0, files: 0 };
    const mb = n => (n / 1000000).toFixed(2) + " MB";
    const time = n => (n / 1000).toFixed(2) + " s";
    const put = (id, value) => { const e = document.getElementById(id); if (e && e.textContent !== value) e.textContent = value; };
    performance.setResourceTimingBufferSize(3000);
    function resource(e) {
        if (!e.name.startsWith(location.origin)) return;
        totals.wire += e.transferSize || 0;
        totals.body += e.decodedBodySize || 0;
        totals.files++;
        if (e.transferSize === 0 && e.decodedBodySize > 0) totals.cached++;
    }
    const observer = new PerformanceObserver(list => { for (const e of list.getEntries()) resource(e); update(); });
    observer.observe({type:"resource",buffered:true});
    const flush = () => { for (const e of observer.takeRecords()) resource(e); };
    function update() {
        const s = selection;
        put("metric-boot", boot === null ? time(performance.now()) : time(boot));
        if (boot !== null) put("metric-boot-detail", mb(bootBytes) + " runtime & shell");
        if (!s) {
            put("metric-load", time(performance.now() - started));
            put("metric-data", mb(totals.wire));
            put("metric-phase", "Loading browser runtime");
            put("metric-network", mb(totals.wire));
            return;
        }
        put("metric-load", time((s.end ?? performance.now()) - s.start));
        put("metric-phase", s.phase);
        put("metric-data", mb(s.bytes));
        put("metric-files", s.files + " files · " + s.reused + " reused in memory");
        const measured = s.measured ?? totals;
        put("metric-network", mb(measured.wire - s.wire));
        const cache = measured.cached - s.cached;
        put("metric-cache", (cache + s.reused) + " files");
        put("metric-cache-detail", cache + " HTTP · " + s.reused + " in memory");
        put("metric-network-detail", (measured.files - s.requests) + " completed requests");
        const overlay = document.getElementById("load-stage-message");
        if (overlay) overlay.textContent = s.phase;
    }
    function tick() { clearInterval(timer); timer = setInterval(update, 200); update(); }
    p.begin = (slug, label) => {
        flush();
        if (boot === null) { boot = performance.now(); bootBytes = totals.body; }
        selection = {slug,label,start:performance.now(),end:null,phase:"Reading app manifest",bytes:0,files:0,reused:0,wire:totals.wire,cached:totals.cached,requests:totals.files};
        tick();
    };
    p.phase = value => { if(selection) selection.phase = value; update(); };
    p.asset = (bytes, reused) => { if(selection) { selection.bytes += bytes; selection.files++; if(reused) selection.reused++; } update(); };
    p.demandAsset = bytes => { if(selection && selection.end !== null) selection.measured = null; p.asset(bytes, false); };
    p.ready = () => { flush(); if(selection && selection.end === null) { selection.end = performance.now(); selection.measured = {...totals}; selection.phase = "First frame rendered"; } update(); clearInterval(timer); };
    p.failed = () => { flush(); if(selection) { selection.end = performance.now(); selection.measured = {...totals}; selection.phase = "Load failed"; } update(); clearInterval(timer); };
    p.refresh = update;
    p.reload = () => location.reload();
    p.toggleReferenceSize = () => document.querySelector(".reference-frame")?.classList.toggle("actual");
    p.snapshot = () => {
        const c = document.getElementById("wfcanvas");
        if (!c || !selection || selection.end === null) return;
        c.toBlob(blob => {
            if(!blob) return;
            const url = URL.createObjectURL(blob), a = document.createElement("a");
            a.href = url; a.download = "browser-" + selection.slug + "-" + new Date().toISOString().replace(/[:.]/g,"-") + ".png";
            a.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
        });
    };
    tick();
})();

// The CLR asks for missing assemblies synchronously, including imports first
// touched during later user interactions. No folder scan or dependency list is
// downloaded. Main-thread sync XHR requires a text response, not arraybuffer.
window.wfwasmFetchAssembly = function (app, name) {
    const request = new XMLHttpRequest();
    request.open("GET", "/api/appresolve?app=" + encodeURIComponent(app) + "&name=" + encodeURIComponent(name), false);
    request.overrideMimeType("text/plain; charset=x-user-defined");
    request.send(null);
    if (request.status === 204) return new Uint8Array(0);
    if (request.status !== 200) throw new Error("Assembly request failed: " + name + " (HTTP " + request.status + ")");
    const hash = request.getResponseHeader("X-WfWasm-Sha256");
    if (!hash || !/^[a-f0-9]{64}$/i.test(hash)) throw new Error("Assembly response has no integrity hash: " + name);
    const text = request.responseText, packet = new Uint8Array(32 + text.length);
    for (let i = 0; i < 32; i++) packet[i] = parseInt(hash.slice(i * 2, i * 2 + 2), 16);
    for (let i = 0; i < text.length; i++) packet[32 + i] = text.charCodeAt(i) & 255;
    window.preview.demandAsset(text.length);
    return packet;
};
