import { PlatformClient } from './platform/platform-client.mjs';
import { showFileDialog, preparePrint } from './platform/platform-ui.mjs';
const $ = id => document.getElementById(id), status = text => $('status').textContent = text;
let client;
try {
    client = await new PlatformClient('demo', { onStorageError: error => status('Settings not saved: ' + error.message) }).start();
    $('identity').textContent = 'User ' + client.user.slice(0, 10) + ' · Application: demo';
    $('theme').value = client.registry.getValue('HKCU\\Software\\Demo', 'Theme', 'Light');
    $('stored').textContent = 'Loaded: ' + $('theme').value; status('Ready');
} catch (error) { status(error.message); }
function action(id, run) { $(id).onclick = async () => { try { if (!client) throw new Error('Services unavailable'); await run(); } catch (e) { status(e.message); } }; }
action('open', async () => {
    const result = await showFileDialog(client, { filter: 'Text documents|*.txt;*.csv;*.json|All files|*.*' });
    if (result.cancelled) return status('Open cancelled');
    const item = result.items[0]; $('text').value = new TextDecoder().decode(await client.read(item.path)); $('selection').textContent = item.path; status('Opened ' + item.name);
});
action('folder', async () => {
    const result = await showFileDialog(client, { mode: 'folder', title: 'Choose a folder' });
    status(result.cancelled ? 'Folder selection cancelled' : 'Selected folder: ' + result.items[0].path);
});
action('save', async () => {
    const result = await showFileDialog(client, { mode: 'save', title: 'Save document', fileName: 'document.txt' });
    if (result.cancelled) return status('Save cancelled');
    await client.write(result.items[0].path, new TextEncoder().encode($('text').value)); $('selection').textContent = result.items[0].path; status('Saved ' + result.items[0].name);
});
action('download', async () => {
    const url = URL.createObjectURL(new Blob([$('text').value], { type: 'text/plain' })); const a = document.createElement('a'); a.href = url; a.download = 'document.txt'; a.click(); setTimeout(() => URL.revokeObjectURL(url), 1000); status('Download requested');
});
action('store', async () => {
    client.registry.setValue('HKCU\\Software\\Demo', 'Theme', 'String', $('theme').value); await client.registry.flush(); $('stored').textContent = 'Saved: ' + $('theme').value; status('Setting saved to the registry database');
});
action('print', async () => {
    const canvas = document.createElement('canvas'); canvas.width = 794; canvas.height = 1123; const ctx = canvas.getContext('2d');
    ctx.fillStyle = 'white'; ctx.fillRect(0, 0, canvas.width, canvas.height); ctx.fillStyle = '#20304a'; ctx.font = 'bold 32px system-ui'; ctx.fillText('Your document', 65, 100); ctx.font = '18px system-ui';
    $('text').value.split('\n').slice(0, 40).forEach((line, i) => ctx.fillText(line, 65, 160 + i * 23, 660));
    await preparePrint({ title: 'Document preview', pages: [{ width: 794, height: 1123, widthMm: 210, heightMm: 297, png: canvas.toDataURL('image/png') }] });
    status('Preview ready. Use its Print button to open browser printing.');
});
