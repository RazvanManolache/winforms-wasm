// Localhost-only test fixture. No browser automation, workspace static serving,
// shell commands or arbitrary output paths from HTTP requests.
import http from 'node:http';
import {readFileSync,writeFileSync,mkdirSync} from 'node:fs';
import {resolve,dirname} from 'node:path';
import {createHash,randomUUID} from 'node:crypto';
import {verifyBindingReport} from './verify-binding-report.mjs';
const args=process.argv.slice(2),value=(key,fallback)=>{const i=args.indexOf(key);return i<0?fallback:args[i+1];};
const output=resolve(value('--output','E:/tmp/wf-binding-contract-report.json'));
const hash=b=>createHash('sha256').update(b).digest('hex');
const binding=readFileSync(new URL('../../src/WfWasm.CanvasHost/wwwroot/gpu-bind.js',import.meta.url));
const cases=readFileSync(new URL('./binding-pixel-cases.mjs',import.meta.url));
const identity={runId:randomUUID(),bindingSha256:hash(binding),testSha256:hash(cases)};
const routes=new Map([
 ['/',[readFileSync(new URL('./binding-fixture.html',import.meta.url)),'text/html']],
 ['/binding.js',[binding,'text/javascript']],['/cases.mjs',[cases,'text/javascript']],
 ['/meta.json',[Buffer.from(JSON.stringify(identity)),'application/json']]
]);
let delivered=false;
const server=http.createServer(async(req,res)=>{
 res.setHeader('Cache-Control','no-store');
 if(req.method==='GET'&&routes.has(req.url)){const[b,type]=routes.get(req.url);res.writeHead(200,{'Content-Type':type});res.end(b);return;}
 if(req.method==='GET'&&req.url==='/favicon.ico'){res.writeHead(204);res.end();return;}
 if(req.method!=='POST'||req.url!=='/report'||delivered){res.writeHead(404);res.end();return;}
 try{
  const parts=[];let size=0;for await(const part of req){size+=part.length;if(size>1048576)throw Error('Report too large');parts.push(part);}
  const report=JSON.parse(Buffer.concat(parts).toString('utf8'));
  const verdict=verifyBindingReport(report,identity,{allowPixelFailures:true});
  mkdirSync(dirname(output),{recursive:true});writeFileSync(output,JSON.stringify(report,null,2));
  const accepted=verdict.failed.length===0;res.writeHead(200,{'Content-Type':'application/json'});res.end(JSON.stringify({accepted,...verdict}));
  delivered=true;console.log(JSON.stringify({output,...identity,...verdict,accepted}));
  clearTimeout(deadline);server.close(()=>{process.exitCode=accepted?0:1;});
 }catch(e){res.writeHead(400,{'Content-Type':'application/json'});res.end(JSON.stringify({accepted:false,error:e.message}));}
});
const deadline=setTimeout(()=>{console.error('GPU BINDING ACCEPTANCE FAILED: no valid browser report before deadline');server.close();process.exitCode=1;},120000);
server.listen(Number(value('--port','0')),'127.0.0.1',()=>console.log(JSON.stringify({url:`http://127.0.0.1:${server.address().port}/`,...identity,output})));
