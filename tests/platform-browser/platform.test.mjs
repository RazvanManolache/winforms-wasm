import test from 'node:test';
import assert from 'node:assert/strict';
import { PlatformClient, RegistryMirror, registryKey, virtualPath, validateRegistryValue } from '../../src/WfWasm.Platform.Browser/platform-client.mjs';
import { filePatterns, matchesFilter, validatePrintJob } from '../../src/WfWasm.Platform.Browser/platform-ui.mjs';

test('virtual paths reject traversal, host paths, encoded escapes, ADS and devices', () => {
    for (const p of ['C:\\Windows\\win.ini', 'workspace:/../x', 'workspace:/%2e%2e/x', 'workspace:/x:stream', 'workspace:/con.txt', 'workspace:/a//b', 'workspace:/a./b']) assert.throws(() => virtualPath(p));
    assert.deepEqual(virtualPath('workspace:/docs\\file.txt'), { mount: 'workspace', path: 'docs/file.txt' });
});
test('registry keys normalize aliases and reject remote hives', () => {
    assert.equal(registryKey('hkey_current_user/Software/Demo'), 'HKCU\\SOFTWARE\\DEMO');
    assert.throws(() => registryKey('\\\\host\\HKCU')); assert.throws(() => registryKey('HKCU\\..\\x'));
});
test('typed values retain 64-bit precision and reject malformed payloads', () => {
    validateRegistryValue('QWord', '9223372036854775807'); validateRegistryValue('DWord', 4294967295); validateRegistryValue('Binary', 'AQID');
    assert.throws(() => validateRegistryValue('QWord', 9223372036854775807)); assert.throws(() => validateRegistryValue('QWord', '9223372036854775808'));
    assert.throws(() => validateRegistryValue('MultiString', [1])); assert.throws(() => validateRegistryValue('Binary', '<bad>'));
});
test('registry mirror owns snapshots, supports default values and case-insensitive names', async () => {
    const sent = [], r = new RegistryMirror({ keys: [], values: [] }, async (...a) => sent.push(a));
    const data = ['a']; r.setValue('HKCU\\Demo', '', 'MultiString', data); data.push('outside');
    assert.deepEqual(r.getValue('hkcu\\demo'), ['a']); const got = r.getValue('HKCU\\Demo'); got.push('outside'); assert.deepEqual(r.getValue('HKCU\\Demo'), ['a']);
    assert.equal(r.getValueKind('HKCU\\Demo'), 'MultiString'); assert.deepEqual(r.getValueNames('HKCU\\Demo'), ['']);
    assert.deepEqual(r.getSubKeyNames('HKCU'), ['DEMO']); await r.flush(); assert.equal(sent.length, 2); assert.equal(r.pendingWrites, 0);
});
test('failed registry persistence remains dirty and retryable', async () => {
    let fail = true; const r = new RegistryMirror({ keys: [], values: [] }, async () => { if (fail) throw new Error('offline'); });
    r.setValue('HKCU\\Demo', 'x', 'String', 'value'); await assert.rejects(r.flush(), /offline/); assert.equal(r.pendingWrites, 2);
    fail = false; await r.flush(); assert.equal(r.pendingWrites, 0); assert.equal(r.getValue('HKCU\\Demo', 'x'), 'value');
});
test('registry rejects nonrecursive deletion and protects hives', async () => {
    const r = new RegistryMirror({ keys: [], values: [] }, async () => {}); r.createKey('HKCU\\A\\Child');
    assert.throws(() => r.deleteKey('HKCU\\A')); assert.throws(() => r.deleteKey('HKCU', true));
    r.deleteKey('HKCU\\A', true); assert.equal(r.hasKey('HKCU\\A\\Child'), false); await r.flush();
});
test('session token and credentials accompany only scoped platform requests', async () => {
    const calls = []; const client = await new PlatformClient('demo', { fetchImpl: async (url, init) => {
        calls.push({ url, init }); return { ok: true, status: 200, json: async () => url.endsWith('/session') ? { token: 'scoped', user: 'opaque' } : { keys: ['HKCU'], values: [] } };
    }}).start();
    assert.equal(calls[0].init.headers['X-Wf-Session'], undefined); assert.equal(calls[1].init.headers['X-Wf-Session'], 'scoped');
    assert.equal(calls[1].init.credentials, 'same-origin'); assert.equal(client.registry.hasKey('HKCU'), true);
});
test('revoked device permission fails without requesting a new permission implicitly', async () => {
    const c = new PlatformClient('demo', { fetchImpl: async () => { throw new Error('unexpected fetch'); } }); let requested = false;
    c.devices.set('device-test', { kind: 'file', name: 'a.txt', queryPermission: async () => 'denied', requestPermission: async () => { requested = true; } });
    await assert.rejects(c.read('device-test:/a.txt'), /permission required/); assert.equal(requested, false);
});
test('single device file grant cannot read a sibling', async () => {
    const c = new PlatformClient('demo', { fetchImpl: async () => {} }); c.devices.set('device-test', { kind: 'file', name: 'a.txt', queryPermission: async () => 'granted' });
    await assert.rejects(c.deviceFile('device-test:/secret.txt'), /not granted/); c.revokeDevice('device-test:/a.txt'); assert.equal(c.devices.size, 0);
});
test('filter syntax is case insensitive and does not execute regular expressions', () => {
    const patterns = filePatterns('Images|*.png;*.jpg|All files|*.*'); assert.equal(matchesFilter('PHOTO.JPG', patterns), true); assert.equal(matchesFilter('notes.txt', patterns), false);
    assert.equal(matchesFilter('anything', filePatterns('Images|*.png|All files|*.*', 2)), true); assert.equal(matchesFilter('x', ['[x]']), false);
});
test('failed flush prevents a false successful shutdown', async () => {
    const c = new PlatformClient('demo', { fetchImpl: async () => {} }); c.registry = { flush: async () => { throw new Error('not durable'); } };
    await assert.rejects(c.dispose(), /not durable/); assert.equal(c.disposed, false);
});
test('printing validates actual PNG dimensions before browser decoding and rejects HTML', () => {
    const page = { width: 1, height: 1, widthMm: 210, heightMm: 297, png: 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aV3sAAAAASUVORK5CYII=' };
    validatePrintJob([page]); assert.throws(() => validatePrintJob([{ ...page, width: 1000 }]), /header/);
    assert.throws(() => validatePrintJob([{ ...page, png: '<script>alert(1)</script>' }]));
    assert.throws(() => validatePrintJob([{ ...page, png: 'data:image/png;base64,Ym9ndXM=' }]));
});
test('printing enforces page and pixel budgets and exact RGBA length', () => {
    const p = { width: 2, height: 2, widthMm: 210, heightMm: 297, rgba: new Uint8Array(16) }; validatePrintJob([p]);
    assert.throws(() => validatePrintJob([{ ...p, rgba: new Uint8Array(15) }]));
    assert.throws(() => validatePrintJob(Array(101).fill(p))); assert.throws(() => validatePrintJob([{ ...p, widthMm: Infinity }]));
});
