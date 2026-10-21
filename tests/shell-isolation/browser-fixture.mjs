// Manual browser acceptance fixture, never included in the deployed app.
// Run: node tests/shell-isolation/browser-fixture.mjs
// Open http://127.0.0.1:5372; select Blocking app, press Block, then switch
// to Healthy app while the child spins for 60 seconds. Repeat with Failing app.
import http from 'node:http';
import {readFileSync} from 'node:fs';
const root = new URL('../../src/WfWasm.CanvasHost/wwwroot/',import.meta.url);
const apps = [{slug:'healthy',label:'Healthy app'},{slug:'blocking',label:'Blocking app'},{slug:'failing',label:'Failing app'},{slug:'slow',label:'Slow loading app'}];
const reports=[];
const html = `<!doctype html><base href="/"><script src="/runtime-context.js"></script><script src="/canvas-bridge.js"></script>
<style>body{font:18px system-ui;padding:30px}button{padding:14px}</style><h1 id="title"></h1><button id="block">Block this app for 60 seconds</button><p id="state">Ready</p>
<script>
wfwasm.installErrorReporting();
title.textContent = wfwasmRuntime.label;
if(wfwasmRuntime.app==='failing') setTimeout(()=>{throw new Error('ISOLATION FIXTURE: intentional load failure')},100);
else if(wfwasmRuntime.app==='slow') setTimeout(()=>wfwasmRuntime.send('ready'),60000);
else wfwasmRuntime.send('ready');
block.onclick=()=>{state.textContent='Blocking starts in 500 ms';setTimeout(()=>{const end=performance.now()+60000;while(performance.now()<end){};state.textContent='Block finished'},500)};
</script>`;
function serve(req,res) {
    const path = new URL(req.url,'http://local').pathname;
    if(path==='/api/preview-config') {res.setHeader('Content-Type','application/json');res.end(JSON.stringify({runtimeOrigin:'http://localhost:5373',apps}));return;}
    if(path==='/api/client-errors' && req.method==='POST') {let body='';req.on('data',b=>body+=b);req.on('end',()=>{reports.push(...JSON.parse(body));res.end('{}')});return;}
    if(path==='/api/client-errors') {res.setHeader('Content-Type','application/json');res.end(JSON.stringify(reports));return;}
    if(path==='/runtime.html') {res.setHeader('Content-Type','text/html');res.end(html);return;}
    const allowed = {'/shell-core.mjs':'text/javascript','/shell-ui.mjs':'text/javascript','/css/app.css':'text/css','/css/shell.css':'text/css','/runtime-context.js':'text/javascript','/canvas-bridge.js':'text/javascript'};
    if(allowed[path]) {res.setHeader('Content-Type',allowed[path]);res.end(readFileSync(new URL(path.slice(1),root)));return;}
    if(path==='/'||path.startsWith('/app/')) {res.setHeader('Content-Type','text/html');res.end(readFileSync(new URL('index.html',root)));return;}
    res.statusCode=404;res.end('Not found');
}
http.createServer(serve).listen(5372,'127.0.0.1');
http.createServer(serve).listen(5373,'127.0.0.1');
console.log('Isolation fixture: http://127.0.0.1:5372/app/healthy');
