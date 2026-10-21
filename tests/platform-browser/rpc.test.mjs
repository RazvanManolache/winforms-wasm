import test from 'node:test';
import assert from 'node:assert/strict';
import { bindPlatformHost, PlatformRpcClient } from '../../src/WfWasm.Platform.Browser/platform-rpc.mjs';

const nonce = 'test-session-0123456789';
function bus() { const listeners = new Set(); return { addEventListener: (_, fn) => listeners.add(fn), removeEventListener: (_, fn) => listeners.delete(fn), emit: event => Promise.all([...listeners].map(fn => fn(event))), count: () => listeners.size }; }
test('host rejects wrong source, origin, session, replay and unsupported operations', async () => {
    const host = bus(), replies = [], child = { postMessage: (data, origin) => replies.push({ data, origin }) }; let listed = 0;
    const client = { mounts: async () => { listed++; return ['allowed']; }, dispose: async () => {} };
    const binding = bindPlatformHost({ client, childWindow: child, childOrigin: 'http://app.localhost:5210', session: nonce, hostWindow: host, onError: () => {} });
    const request = { type: 'wf-platform-request', version: 1, session: nonce, id: 1, op: 'files.mounts' };
    await host.emit({ source: {}, origin: 'http://app.localhost:5210', data: request });
    await host.emit({ source: child, origin: 'http://attacker.invalid', data: request });
    await host.emit({ source: child, origin: 'http://app.localhost:5210', data: { ...request, session: 'stale' } });
    assert.equal(listed, 0); assert.equal(replies.length, 0);
    await host.emit({ source: child, origin: 'http://app.localhost:5210', data: request });
    await host.emit({ source: child, origin: 'http://app.localhost:5210', data: request });
    assert.equal(listed, 1); assert.equal(replies[0].origin, 'http://app.localhost:5210'); assert.deepEqual(replies[0].data.value, ['allowed']);
    await host.emit({ source: child, origin: 'http://app.localhost:5210', data: { ...request, id: 2, op: 'execute-shell' } });
    assert.equal(replies[1].data.ok, false); await binding.dispose(); assert.equal(host.count(), 0);
});
test('runtime ignores forged responses and rejects pending requests on teardown', async () => {
    const runtime = bus(), requests = [], parent = { postMessage: (data, origin) => requests.push({ data, origin }) };
    const client = new PlatformRpcClient({ parentWindow: parent, parentOrigin: 'http://127.0.0.1:5210', session: nonce, runtimeWindow: runtime });
    const pending = client.call('files.mounts'); const response = { type: 'wf-platform-response', version: 1, session: nonce, id: 1, ok: true, value: ['allowed'] };
    await runtime.emit({ source: {}, origin: 'http://127.0.0.1:5210', data: response }); assert.equal(client.pending.size, 1);
    await runtime.emit({ source: parent, origin: 'http://127.0.0.1:5210', data: response }); assert.deepEqual(await pending, ['allowed']);
    assert.equal(requests[0].origin, 'http://127.0.0.1:5210');
    const cancelled = client.call('dialog.open'); client.dispose(); await assert.rejects(cancelled, /disposed/); assert.equal(runtime.count(), 0);
});
test('runtime bounds concurrent requests and timeouts are explicit failures', async () => {
    const runtime = bus(), parent = { postMessage: () => {} };
    const client = new PlatformRpcClient({ parentWindow: parent, parentOrigin: 'http://127.0.0.1:5210', session: nonce, runtimeWindow: runtime, timeoutMs: 10 });
    const pending = Array.from({ length: 32 }, () => client.call('files.mounts').catch(e => e.message));
    await assert.rejects(client.call('files.mounts'), /Too many/);
    assert.equal((await Promise.all(pending)).every(s => s.includes('timed out')), true); client.dispose();
});
test('wildcard and ambiguous origins are rejected', () => {
    assert.throws(() => new PlatformRpcClient({ parentWindow: {}, parentOrigin: '*', session: nonce, runtimeWindow: bus() }));
    assert.throws(() => bindPlatformHost({ client: {}, childWindow: {}, childOrigin: 'http://app.localhost/path', session: nonce, hostWindow: bus() }));
});
