import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import vm from 'node:vm';
import {AppSessions, channel, runtimeOriginFor} from '../../src/WfWasm.CanvasHost/wwwroot/shell-core.mjs';

const app = slug => ({slug, label:'App ' + slug});
function fixture() {
    let clock = 0, serial = 0;
    const frames = [], removed = [], states = [];
    const controller = new AppSessions({runtimeOrigin:'http://localhost:5210', parentOrigin:'http://127.0.0.1:5210',
        now:() => clock, id:() => 'session-' + ++serial,
        mount:(url, app) => { const f = {url, app, contentWindow:{}}; frames.push(f); return f; },
        unmount:frame => { removed.push(frame); }, changed:s => states.push(s)});
    const event = (type, payload = {}, s = controller.current) => ({origin:controller.runtimeOrigin, source:s.frame.contentWindow,
        data:{channel, app:s.app.slug, session:s.id, type, ...payload}});
    return {controller, frames, removed, states, event, advance:ms => clock += ms};
}
test('loopback origins use different sites in both directions', () => {
    assert.equal(runtimeOriginFor('http://127.0.0.1:5210'), 'http://localhost:5210');
    assert.equal(runtimeOriginFor('http://localhost:5210'), 'http://127.0.0.1:5210');
});
test('different apps use different loopback sites so a hung renderer cannot block another app', () => {
    assert.equal(runtimeOriginFor('http://127.0.0.1:5210',null,'paint'),'http://paint.localhost:5210');
    assert.equal(runtimeOriginFor('http://localhost:5210',null,'smoke'),'http://smoke.localhost:5210');
    assert.throws(() => runtimeOriginFor('http://localhost:5210',null,'../bad'));
    const f=fixture(); f.controller.runtimeOrigin=slug=>runtimeOriginFor('http://127.0.0.1:5210',null,slug);
    const first=f.controller.select(app('paint')), second=f.controller.select(app('smoke'));
    assert.notEqual(first.origin,second.origin);
    assert.equal(f.controller.accept({...f.event('ready'),origin:second.origin}),true);
});
for (const configured of ['http://127.0.0.1:9999','javascript:alert(1)','http://user:pass@runtime.test','http://runtime.test/path','http://runtime.test/?x=1','http://runtime.test/#x'])
    test('reject unsafe/same-site runtime origin ' + configured, () => assert.throws(() => runtimeOriginFor('http://127.0.0.1:5210',configured)));
test('deployed sites require explicit runtime site configuration', () => {
    assert.throws(() => runtimeOriginFor('https://studio.test'));
    assert.equal(runtimeOriginFor('https://studio.test','https://apps.example'), 'https://apps.example');
    assert.throws(() => runtimeOriginFor('https://studio.test','http://apps.example'));
});
test('switching during an unfinished load replaces immediately without waiting', () => {
    const f=fixture(), first=f.controller.select(app('paint'));
    const second=f.controller.select(app('smoke'));
    assert.deepEqual(f.removed,[first.frame]);
    assert.equal(second.status,'loading'); assert.notEqual(second.id,first.id);
    const url=new URL(second.frame.url);
    assert.equal(url.pathname,'/runtime.html'); assert.equal(url.searchParams.get('app'),'smoke');
    assert.equal(url.searchParams.get('session'),second.id); assert.equal(url.searchParams.get('parentOrigin'),'http://127.0.0.1:5210');
});
test('failed runtime cannot poison next selection or retry', () => {
    const f=fixture(), first=f.controller.select(app('paint'));
    f.controller.accept(f.event('failed',{message:'CLR aborted'})); assert.equal(first.status,'failed');
    const second=f.controller.select(app('paint')); assert.notEqual(first.id,second.id);
    assert.equal(f.controller.accept(f.event('ready')),true); assert.equal(second.status,'ready');
    assert.equal(f.controller.accept(f.event('failed',{message:'late'},first)),false); assert.equal(second.status,'ready');
});
for (const type of ['ready','failed','metrics','phase','heartbeat']) test('late old-session ' + type + ' is ignored', () => {
    const f=fixture(), old=f.controller.select(app('paint')), message=f.event(type,{},old);
    f.controller.select(app('smoke')); assert.equal(f.controller.accept(message),false); assert.equal(f.controller.current.status,'loading');
});
for (const field of ['origin','source','channel','session','app','type']) test('reject spoofed runtime message: '+field, () => {
    const f=fixture(); f.controller.select(app('smoke')); const message=f.event('ready');
    if (['origin','source'].includes(field)) message[field]='spoof'; else message.data[field]='spoof';
    assert.equal(f.controller.accept(message),false); assert.equal(f.controller.current.status,'loading');
});
test('stop detaches runtime, resets metrics and rejects its pending messages', () => {
    const f=fixture(),s=f.controller.select(app('smoke')), msg=f.event('ready'); f.controller.stop();
    assert.equal(f.controller.current,null); assert.deepEqual(f.removed,[s.frame]); assert.equal(f.controller.accept(msg),false);
});
test('heartbeat watchdog detects a blocked app and remains recoverable', () => {
    const f=fixture(); f.controller.select(app('paint')); f.advance(8001); assert.equal(f.controller.isBusy(),true);
    f.controller.accept(f.event('heartbeat')); assert.equal(f.controller.isBusy(),false);
    f.advance(9000); f.controller.select(app('smoke')); assert.equal(f.controller.isBusy(),false);
});
test('restarting a frozen app uses a new site, including after Stop', () => {
    const f=fixture(); f.controller.runtimeOrigin=slug=>runtimeOriginFor('http://127.0.0.1:5210',null,slug);
    const first=f.controller.select(app('paint')); f.advance(9000); f.controller.stop();
    const retry=f.controller.select(app('paint')); assert.notEqual(first.origin,retry.origin);
    assert.match(retry.origin,/retry-session2--paint\.localhost/);
    assert.equal(f.controller.accept({...f.event('ready'),origin:first.origin}),false);
    assert.equal(f.controller.accept({...f.event('ready'),origin:retry.origin}),true);
});
test('ready cannot clear a fatal error from the same runtime', () => {
    const f=fixture(),s=f.controller.select(app('smoke')); f.controller.accept(f.event('failed',{message:'fatal'}));
    f.controller.accept(f.event('ready')); assert.equal(s.status,'failed'); assert.equal(s.phase,'fatal');
});
test('runtime metric payloads are normalized, no executable HTML accepted', () => {
    const f=fixture(),s=f.controller.select(app('smoke')); f.controller.accept(f.event('metrics',{metrics:{bytes:NaN,wireBytes:-1,bootMs:5,httpCacheHits:Infinity,evil:'<script>'}}));
    assert.equal(s.metrics.bytes,0); assert.equal(s.metrics.wireBytes,0); assert.equal(s.metrics.bootMs,5); assert.equal(s.metrics.httpCacheHits,0); assert.equal(s.metrics.evil,undefined);
});

function runtime() {
    const messages=[],handlers={},posts=[],history=[];
    const parent={postMessage:(data,origin) => messages.push({data,origin})};
    const w={parent, addEventListener:(type,fn) => handlers[type]=fn};
    const context=vm.createContext({window:w, URL, URLSearchParams, crypto:{randomUUID:()=> 'generated'},
        location:{origin:'http://localhost:5210',pathname:'/runtime.html',search:'?app=paint&label=Paint.NET&session=paint-session&parentOrigin=http%3A%2F%2F127.0.0.1%3A5210'},
        document:{referrer:'http://127.0.0.1:5210/',documentElement:{classList:{add(){}}}},
        history:{replaceState:(...args) => history.push(args)}, setInterval(){},setTimeout(){},clearTimeout(){},
        console:{log(){},info(){},debug(){},warn(){},error(){}},fetch:(url,options)=> {posts.push({url,options});return Promise.resolve({ok:true});}});
    vm.runInContext(readFileSync(new URL('../../src/WfWasm.CanvasHost/wwwroot/runtime-context.js',import.meta.url),'utf8'),context);
    vm.runInContext(readFileSync(new URL('../../src/WfWasm.CanvasHost/wwwroot/canvas-bridge.js',import.meta.url),'utf8'),context);
    w.wfwasm.installErrorReporting();
    return {w,context,messages,handlers,posts,history};
}
test('boot context captures app and session before Blazor starts', () => {
    const f=runtime(); assert.equal(f.w.wfwasmRuntime.app,'paint'); assert.equal(f.w.wfwasmRuntime.session,'paint-session');
    assert.match(f.history[0][2],/^\/app\/paint\?/); assert.equal(Object.isFrozen(f.w.wfwasmRuntime),true);
});
test('JS fatal error posts immutable app identity and survives frame removal', () => {
    const f=runtime(); f.context.location.pathname='/app/smoke';
    f.handlers.error({error:new Error('isolation regression probe')});
    const report=JSON.parse(f.posts[0].options.body)[0];
    assert.equal(report.app,'paint'); assert.equal(report.appLabel,'Paint.NET'); assert.equal(report.sessionId,'paint-session');
    assert.equal(f.posts[0].options.keepalive,true); assert.equal(f.messages.at(-1).data.type,'failed');
});
test('unhandled rejection and bootstrap failures retain the app context', () => {
    const f=runtime(); f.handlers.unhandledrejection({reason:new Error('reject')}); f.w.wfwasm.reportRuntimeFailure(new Error('bootstrap'));
    assert.equal(f.posts.length,2); for(const p of f.posts) assert.equal(JSON.parse(p.options.body)[0].app,'paint');
    assert.equal(f.messages.filter(m=>m.data.type==='failed').length,1);
});
test('runtime commands only accept the owning parent and session', () => {
    const f=runtime(); let snapshots=0; f.w.preview={snapshot:()=>snapshots++};
    const event={origin:'http://127.0.0.1:5210',source:f.w.parent,data:{channel,app:'paint',session:'paint-session',type:'snapshot'}};
    f.handlers.message({...event,source:{}}); f.handlers.message({...event,data:{...event.data,session:'stale'}}); assert.equal(snapshots,0);
    f.handlers.message(event); assert.equal(snapshots,1);
});
test('anonymous JS frames deduplicate across runtime hosts and session IDs', () => {
    const f=runtime();
    for (const id of ['first','second']) f.handlers.error({error:{name:'Error',message:'same crash',stack:`Error: same crash\n at http://${id}.localhost:5210/app/paint?session=${id}:6:57`}});
    assert.equal(JSON.parse(f.posts[0].options.body)[0].signature,JSON.parse(f.posts[1].options.body)[0].signature);
    assert.match(JSON.parse(f.posts[0].options.body)[0].signature,/\/app\/paint:6:57/);
});
test('Blazor fatal renderer console errors also notify the owning shell', () => {
    const f=runtime(); f.context.console.error('crit: Microsoft.AspNetCore.Components.WebAssembly.Rendering.WebAssemblyRenderer Unhandled exception');
    assert.equal(JSON.parse(f.posts[0].options.body)[0].app,'paint');
    assert.equal(f.messages.at(-1).data.type,'failed');
});
