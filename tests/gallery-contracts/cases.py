"""Gallery case workflows: real browser input, independent oracles.
Dimensions per row: launch|usefulPixels|interaction|dataOrFileResult|
isolation|performance|backendEvidence in {passed,failed,blocked,untested}."""
import json
import time
import urllib.request
from datetime import datetime, timezone

from core import CANVAS_OFFSET, VIEWPORT, parse_scene, to_page, region_diff, diff_bbox, first_text_row

DIMENSIONS = ['launch', 'usefulPixels', 'interaction', 'dataOrFileResult',
              'isolation', 'performance', 'backendEvidence']


class Ctx:
    def __init__(self, pw, base, outdir, gen, negative=False):
        self.consoles, self.page_errors = None, None
        self.pw = pw
        self.base = base
        self.outdir = outdir
        self.gen = gen
        self.negative = negative
        self.browser = pw.chromium.launch(channel='msedge', headless=True,
            args=['--window-size=1500,950', '--no-sandbox', '--disable-gpu-sandbox'])

    def close(self):
        self.browser.close()

    def inv(self):
        with urllib.request.urlopen(self.base + '/api/client-errors?take=1000', timeout=30) as r:
            return json.load(r)

    def new_row(self, case_id, app):
        dims = {d: 'untested' for d in DIMENSIONS}
        return {'id': case_id, 'app': app,
                'generation': {'rev': self.gen['rev'], 'run_id': self.gen['run_id']},
                'status': 'blocked', 'dims': dims, 'assertions': [],
                'artifacts': {'shots': []}, 'errors': {'unexpected': [], 'firstChance': 0},
                'untested': []}

    def check(self, row, name, ok, detail=''):
        row['assertions'].append({'name': name, 'ok': bool(ok), 'detail': str(detail)[:220]})
        return ok

    def launch(self, row, app, wait_s=22, deadline_s=120):
        """Open /app/{slug}; return (page, consoles, errors, form, controls)."""
        self.consoles, self.page_errors = [], []
        pg = self.browser.new_page(viewport={'width': VIEWPORT[0], 'height': VIEWPORT[1]})
        pg.set_default_timeout(deadline_s * 1000)
        consoles, errors = self.consoles, self.page_errors
        pg.on('console', lambda m: consoles.append(f'[{m.type}] {m.text[:280]}'))
        pg.on('pageerror', lambda e: errors.append(str(e)[:300]))
        t0 = datetime.now(timezone.utc).isoformat()
        row['artifacts']['t0'] = t0
        try:
            pg.goto(f'{self.base}/app/{app}', wait_until='domcontentloaded', timeout=deadline_s * 1000)
            pg.wait_for_timeout(wait_s * 1000)
        except Exception as e:
            consoles.append(f'[goto-err] {str(e)[:150]}')
        form, controls = parse_scene(consoles)
        return pg, consoles, errors, form, controls

    def shot(self, row, pg, step):
        p = f"{self.outdir}/shot-{row['id']}-{step}.png"
        pg.screenshot(path=p)
        row['artifacts']['shots'].append(p)
        from PIL import Image
        return Image.open(p).convert('RGB')

    def click(self, pg, row, x, y, wait_s=3):
        pg.mouse.click(x, y)
        pg.wait_for_timeout(wait_s * 1000)

    def click_control(self, pg, consoles_before, row, form, ctl, expect_hit=None):
        """Click a scene control (parser yields absolute host coords);
        auto-detect caption chrome via hit log."""
        typ, hx, hy, w, h = ctl[0], ctl[1], ctl[2], ctl[3], ctl[4]
        for chrome in (0, 30):
            px, py = to_page(hx + w // 2, chrome + hy + h // 2)
            n0 = len([c for c in consoles_before if 'OnClick hit=' in c])
            self.click(pg, row, px, py)
            hits = [c for c in consoles_before if 'OnClick hit=' in c][n0:]
            # consoles list is live-appended by listener; re-read via closure below
            if expect_hit and any(expect_hit in hh for hh in hits):
                return (px, py, True)
        return (px, py, False)

    def err_delta(self, row, before, apps, t0):
        # Count-based delta: entries re-fire across runs (shared log dir),
        # so presence alone proves nothing; a bumped count or lastSeen
        # inside this case window attributes the report to this attempt.
        after = self.inv()
        old = {(e.get('signature'), e.get('source')):
               (e.get('count', 0), e.get('lastSeen', '')) for e in before}
        new = []
        for e in after:
            key = (e.get('signature'), e.get('source'))
            if not any(a in (e.get('apps') or []) for a in apps):
                continue
            prev = old.get(key)
            if prev is None:
                new.append(e)
            elif (e.get('count', 0), e.get('lastSeen', '')) > (prev[0], prev[1]):
                new.append(e)
        unexp, fc = [], 0
        for e in new:
            src = e.get('source') or ''
            if src == 'first-chance':
                fc += 1
            else:
                unexp.append(f"[{src}] {(e.get('signature') or '')[:160]}")
        row['errors']['unexpected'] = unexp[:10]
        row['errors']['firstChance'] = fc
        return unexp


def finalize(row):
    req = [a for a in row['assertions']]
    if any(a['name'].startswith('fixture:') and not a['ok'] for a in req):
        row['status'] = 'blocked'
    elif all(a['ok'] for a in req) and req:
        row['status'] = 'passed'
    else:
        row['status'] = 'failed'
    for d in row['dims']:
        if row['dims'][d] not in ('passed', 'failed', 'blocked', 'untested'):
            row['dims'][d] = 'untested'
    return row


def case_smoke(ctx):
    row = ctx.new_row('smoke-click-keys', 'smoke')
    inv0 = ctx.inv()
    pg, consoles, errors, form, controls = ctx.launch(row, 'smoke')
    try:
        ctx.check(row, 'fixture:form', form is not None, f'form={form}')
        btn = next((v for k, v in controls.items() if v[0] == 'Button' and v[5]), None)
        ctx.check(row, 'fixture:button', btn is not None, f'btn={btn}')
        lbls = [v for k, v in controls.items() if v[0] == 'Label' and v[5]]
        ctx.check(row, 'fixture:status-label', len(lbls) >= 2, f'labels={len(lbls)}')
        if form is None or btn is None or len(lbls) < 2:
            row['dims']['launch'] = 'failed' if form is None else 'blocked'
            return finalize(row)
        row['dims']['launch'] = 'passed' if not errors else 'failed'
        ctx.check(row, 'launch:no-pageerrors', not errors, f'pageerrors={len(errors)}')
        status = sorted(lbls, key=lambda v: v[1])[1]
        base = ctx.shot(row, pg, 'base')
        # usefulPixels: form region is non-uniform (real content, not blank)
        fx, fy, fw, fh = form
        region = base.crop((fx + CANVAS_OFFSET[0], fy + CANVAS_OFFSET[1],
                            fx + CANVAS_OFFSET[0] + fw, fy + CANVAS_OFFSET[1] + fh))
        colors = region.convert('RGB').getcolors(maxcolors=1 << 20)
        ctx.check(row, 'pixels:form-nonblank', colors is not None and len(colors) > 4,
                  f'distinct={None if colors is None else len(colors)}')
        row['dims']['usefulPixels'] = 'passed' if row['assertions'][-1]['ok'] else 'failed'
        sx, sy, sw, sh = status[1], status[2], status[3], status[4]
        srect = (sx + CANVAS_OFFSET[0], 30 + sy + CANVAS_OFFSET[1], sw, sh)
        px, py, hit = ctx.click_control(pg, consoles, row, form, btn, expect_hit='Button')
        ctx.check(row, 'click:routed-to-button', hit, f'page=({px},{py})')
        after_click = ctx.shot(row, pg, 'click')
        d1 = region_diff(base, after_click, srect)
        want_change = d1 > 100 and not ctx.negative
        ctx.check(row, 'click:status-pixels-change', want_change, f'changed={d1}')
        pg.keyboard.press('Space')
        pg.wait_for_timeout(3000)
        after_key = ctx.shot(row, pg, 'key')
        d2 = region_diff(after_click, after_key, srect)
        # single-digit increment ("Clicks: 1 (Success!)" -> "Clicks: 2"):
        # assert on the counter cell, not the whole label
        digit = (srect[0] + 35, srect[1], 45, srect[3])
        d2d = region_diff(after_click, after_key, digit)
        want_change2 = (d2d > 10) and not ctx.negative
        ctx.check(row, 'key:status-pixels-change', want_change2, f'digit={d2d} full={d2}')
        ok_inter = all(a['ok'] for a in row['assertions'] if a['name'].startswith(('click:', 'key:')))
        row['dims']['interaction'] = 'passed' if ok_inter else 'failed'
        row['dims']['dataOrFileResult'] = 'blocked'
        row['untested'].append('exact counter value: no text readback bridge')
        unexp = ctx.err_delta(row, inv0, ['smoke'], row['artifacts']['t0'])
        ctx.check(row, 'errors:no-unexpected', not unexp, f'unexpected={len(unexp)}')
    finally:
        pg.close()
    return finalize(row)


def case_todo(ctx):
    row = ctx.new_row('todo-add-done', 'todo')
    inv0 = ctx.inv()
    pg, consoles, errors, form, controls = ctx.launch(row, 'todo')
    try:
        need = ['Task_TB', 'Add_BTN', 'NewTasks_LTV', 'DoneTask_BTN', 'DoneTasks_LTV']
        missing = [n for n in need if n not in controls]
        ctx.check(row, 'fixture:controls', not missing, f'missing={missing}')
        if form is None or missing:
            row['dims']['launch'] = 'blocked'
            return finalize(row)
        row['dims']['launch'] = 'passed' if not errors else 'failed'
        ctx.check(row, 'launch:no-pageerrors', not errors, f'pageerrors={len(errors)}')
        task = f"gallery-{ctx.gen['run_id']}"
        tb = controls['Task_TB']
        tpx, tpy = to_page(tb[1] + tb[3] // 2, 30 + tb[2] + tb[4] // 2)
        ctx.click(pg, row, tpx, tpy)
        pg.keyboard.press('Control+a')
        pg.keyboard.type(task, delay=20)
        pg.wait_for_timeout(1000)
        base = ctx.shot(row, pg, 'typed')
        add = controls['Add_BTN']
        ctx.click_control(pg, consoles, row, form, add, expect_hit='Button')
        after_add = ctx.shot(row, pg, 'added')
        lv = controls['NewTasks_LTV']
        newrect = (lv[1] + CANVAS_OFFSET[0], 30 + lv[2] + CANVAS_OFFSET[1], lv[3], lv[4])
        d1 = region_diff(base, after_add, newrect)
        ctx.check(row, 'add:row-appears-in-new', d1 > 60, f'changed={d1}')
        # select the first item row by its observed text position, then Done
        item_y = first_text_row(after_add, newrect)
        ctx.check(row, 'fixture:item-row-visible', item_y is not None, f'y={item_y}')
        if item_y is None:
            return finalize(row)
        ctx.click(pg, row, newrect[0] + 30, item_y)
        done = controls['DoneTask_BTN']
        ctx.click_control(pg, consoles, row, form, done, expect_hit='Button')
        after_done = ctx.shot(row, pg, 'done')
        dv = controls['DoneTasks_LTV']
        donerect = (dv[1] + CANVAS_OFFSET[0], 30 + dv[2] + CANVAS_OFFSET[1], dv[3], dv[4])
        d2 = region_diff(after_add, after_done, donerect)
        d3 = region_diff(after_add, after_done, newrect)
        ctx.check(row, 'done:done-list-changes', d2 > 60, f'changed={d2}')
        ctx.check(row, 'done:new-list-changes', d3 > 60, f'changed={d3}')
        ok_inter = all(a['ok'] for a in row['assertions'] if a['name'].startswith(('add:', 'done:')))
        row['dims']['interaction'] = 'passed' if ok_inter else 'failed'
        row['dims']['usefulPixels'] = 'passed' if ok_inter else 'failed'
        row['dims']['dataOrFileResult'] = 'blocked'
        row['untested'].append('exact row text: no text readback bridge (geometry diff only)')
        unexp = ctx.err_delta(row, inv0, ['todo'], row['artifacts']['t0'])
        ctx.check(row, 'errors:no-unexpected', not unexp, f'unexpected={len(unexp)}')
    finally:
        pg.close()
    return finalize(row)


def case_barcode(ctx):
    row = ctx.new_row('barcode-qr-update', 'syncfusion-barcode')
    inv0 = ctx.inv()
    pg, consoles, errors, form, controls = ctx.launch(row, 'syncfusion-barcode')
    try:
        need = ['textToEncode', 'button1', 'barcodeControl']
        missing = [n for n in need if n not in controls]
        ctx.check(row, 'fixture:controls', not missing, f'missing={missing}')
        if form is None or missing:
            row['dims']['launch'] = 'blocked'
            return finalize(row)
        row['dims']['launch'] = 'passed' if not errors else 'failed'
        ctx.check(row, 'launch:no-pageerrors', not errors, f'pageerrors={len(errors)}')
        import cv2
        bc = controls['barcodeControl']
        qrect = (bc[1] + CANVAS_OFFSET[0] + 8, 30 + bc[2] + CANVAS_OFFSET[1] + 8,
                 bc[3] - 16, bc[4] - 16)
        base = ctx.shot(row, pg, 'base')
        det = cv2.QRCodeDetector()
        crop0 = base.crop((qrect[0], qrect[1], qrect[0] + qrect[2], qrect[1] + qrect[3]))
        import numpy as np
        val0, _, _ = det.detectAndDecode(np.array(crop0))
        ctx.check(row, 'decode:default', val0 == 'HTTP://WWW.SYNCFUSION.COM', f'decoded={val0!r}')
        want = 'GALLERY-123'
        tb = controls['textToEncode']
        tpx, tpy = to_page(tb[1] + tb[3] // 2, 30 + tb[2] + tb[4] // 2)
        # Ctrl+A select-all is the user-visible contract (host emulates it;
        # the fork has no native edit control). Real chord, then replace.
        ctx.click(pg, row, tpx, tpy)
        pg.keyboard.press('Control+a')
        pg.wait_for_timeout(500)
        pg.keyboard.type(want, delay=20)
        pg.wait_for_timeout(1000)
        ctx.click_control(pg, consoles, row, form, controls['button1'], expect_hit='Button')
        after = ctx.shot(row, pg, 'updated')
        d1 = region_diff(base, after, qrect)
        ctx.check(row, 'update:qr-region-changes', d1 > 200, f'changed={d1}')
        crop1 = after.crop((qrect[0], qrect[1], qrect[0] + qrect[2], qrect[1] + qrect[3]))
        val1, _, _ = det.detectAndDecode(np.array(crop1))
        ctx.check(row, 'decode:exact-text', val1 == want, f'decoded={val1!r}')
        ok_data = row['assertions'][-1]['ok'] and row['assertions'][-3]['ok']
        row['dims']['dataOrFileResult'] = 'passed' if ok_data else 'failed'
        row['dims']['interaction'] = 'passed' if ok_data else 'failed'
        row['dims']['usefulPixels'] = 'passed' if d1 > 200 else 'failed'
        unexp = ctx.err_delta(row, inv0, ['syncfusion-barcode'], row['artifacts']['t0'])
        ctx.check(row, 'errors:no-unexpected', not unexp, f'unexpected={len(unexp)}')
    finally:
        pg.close()
    return finalize(row)


def case_mdi(ctx):
    row = ctx.new_row('mdi-file-menu', 'syncfusion-tabbed-mdi')
    inv0 = ctx.inv()
    pg, consoles, errors, form, controls = ctx.launch(row, 'syncfusion-tabbed-mdi')
    try:
        ctx.check(row, 'fixture:form', form is not None, f'form={form}')
        mdi = next((v for k, v in controls.items() if v[0] == 'MdiClient' and v[5]), None)
        ctx.check(row, 'fixture:mdiclient', mdi is not None, f'mdi={mdi}')
        if form is None or mdi is None:
            row['dims']['launch'] = 'blocked'
            return finalize(row)
        row['dims']['launch'] = 'passed' if not errors else 'failed'
        ctx.check(row, 'launch:no-pageerrors', not errors, f'pageerrors={len(errors)}')
        # File menu via real Alt+F chord (coordinate-free: menu item
        # bounds are not dumped). Then activate the first dropdown item.
        mx, my, mw, mh = mdi[1], mdi[2], mdi[3], mdi[4]
        clientrect = (mx + CANVAS_OFFSET[0], 30 + my + CANVAS_OFFSET[1], mw, mh)
        fx, fy, fw, fh = form
        formrect = (fx + CANVAS_OFFSET[0], fy + CANVAS_OFFSET[1], fw, fh)
        base = ctx.shot(row, pg, 'base')
        # Focus the app surface first (desktop parity: an unfocused
        # window ignores menu chords). Re-baseline after the click so
        # menu:opens measures the popup, not the focus click itself.
        ctx.click(pg, row, formrect[0] + fw // 2, formrect[1] + fh // 2)
        base = ctx.shot(row, pg, 'focused')
        pg.keyboard.press('Alt+f')
        pg.wait_for_timeout(3000)
        menu = ctx.shot(row, pg, 'menu')
        dmenu = region_diff(base, menu, formrect)
        ctx.check(row, 'menu:opens', dmenu > 300, f'changed={dmenu}')
        box = diff_bbox(base, menu, formrect)
        ctx.check(row, 'fixture:dropdown-visible', box is not None, f'box={box}')
        if box is None:
            return finalize(row)
        # first dropdown item (center of its top row)
        ctx.click(pg, row, box[0] + 40, box[1] + 12)
        after_item = ctx.shot(row, pg, 'item')
        d1 = region_diff(menu, after_item, clientrect)
        ctx.check(row, 'menu:item-acts-on-client', d1 > 500, f'changed={d1}')
        pg.keyboard.press('Escape')
        pg.wait_for_timeout(2000)
        ctx.shot(row, pg, 'esc')
        ok_inter = all(a['ok'] for a in row['assertions'] if a['name'].startswith(('menu:',)))
        row['dims']['interaction'] = 'passed' if ok_inter else 'failed'
        row['dims']['usefulPixels'] = 'passed' if ok_inter else 'failed'
        row['dims']['dataOrFileResult'] = 'blocked'
        row['untested'].append('tab-strip switching: strip Bounds diverge from paint position')
        row['untested'].append('document identity: no model readback')
        unexp = ctx.err_delta(row, inv0, ['syncfusion-tabbed-mdi'], row['artifacts']['t0'])
        ctx.check(row, 'errors:no-unexpected', not unexp, f'unexpected={len(unexp)}')
    finally:
        pg.close()
    return finalize(row)


def case_tile_layout(ctx):
    """Load regression for the tile-layout resource-reader chain (red until
    the DeserializingResourceReader-format .resources streams in
    TileLayoutDemo_2008.dll are readable by the browser stack).
    Chain under test: 16x NotSupportedException 'should not be read with
    this reader' during Form1 ctor -> NullReferenceException -> ctor-guard
    stash -> 'No form created for scene' -> window-create fallback.
    Boot-only: interaction stays untested by design."""
    row = ctx.new_row('tile-layout-load', 'syncfusion-tile-layout')
    inv0 = ctx.inv()
    pg, consoles, errors, form, controls = ctx.launch(row, 'syncfusion-tile-layout')
    try:
        base = ctx.shot(row, pg, 'base')
        text = '\n'.join(consoles or [])
        ctx.check(row, 'load:form-created', form is not None, f'form={form}')
        if form is None:
            row['dims']['launch'] = 'failed'
        else:
            row['dims']['launch'] = 'passed' if not errors else 'failed'
        ctx.check(row, 'launch:no-pageerrors', not errors, f'pageerrors={len(errors)}')
        failed_shell = ('could not create its window' in text) or ('could not continue' in text)
        ctx.check(row, 'load:no-fallback-shell', not failed_shell,
                  'fallback marker present' if failed_shell else 'no fallback markers')
        bad_reader = 'should not be read with this reader' in text
        ctx.check(row, 'load:resources-readable', not bad_reader,
                  'DeserializingResourceReader rejected' if bad_reader else 'no reader rejection')
        painted = any('first-painted' in c for c in (consoles or []))
        ctx.check(row, 'pixels:any-first-painted', painted,
                  'first paint observed' if painted else 'canvas stayed blank')
        row['dims']['usefulPixels'] = 'passed' if painted else 'failed'
        row['dims']['interaction'] = 'untested'
        row['untested'].append('click/drag workflows: boot-only regression, no controls painted pre-fix')
        row['untested'].append('menu/nav discovery: unreachable while the window cannot be created')
        unexp = ctx.err_delta(row, inv0, ['syncfusion-tile-layout'], row['artifacts']['t0'])
        ctx.check(row, 'errors:no-unexpected', not unexp, f'unexpected={len(unexp)}')
    finally:
        pg.close()
    return finalize(row)


def case_failed_ctor(ctx):
    row = ctx.new_row('failed-ctor-contained', 'syncfusion-auto-complete')
    # The failure under test IS the error report: expect exactly the
    # app-load window-create report, nothing else. Boolean True is banned
    # (verifier rejects it); each entry names app+phase+signature.
    row['expectedErrors'] = [
        {'app': 'syncfusion-auto-complete', 'phase': 'app-load',
         'signature': 'could not create its window'},
    ]
    # launch fails BY DESIGN here (window never created); isolation is the
    # dimension under test. Declared so the verifier's passed-with-failed-dim
    # rule accepts this row while rejecting undeclared ones.
    row['allowedFailedDims'] = {
        'launch': 'failure under test: window never created by design',
    }
    inv0 = ctx.inv()
    pg, consoles, errors, form, controls = ctx.launch(row, 'syncfusion-auto-complete', wait_s=15)
    try:
        marker = any('could not create its window' in c for c in consoles)
        ctx.check(row, 'failure:window-marker', marker, 'app-load marker in console')
        unexp = ctx.err_delta(row, inv0, ['syncfusion-auto-complete'], row['artifacts']['t0'])
        ctx.check(row, 'failure:inventoried', len(unexp) > 0, f'reports={unexp[:1]}')
        row['dims']['launch'] = 'failed'
        # isolation: shell must stay usable -> smoke click works after
        pg.goto(f'{ctx.base}/app/smoke', wait_until='domcontentloaded', timeout=90000)
        pg.wait_for_timeout(20000)
        consoles2 = []
        pg.on('console', lambda m: consoles2.append(f'[{m.type}] {m.text[:280]}'))
        pg.wait_for_timeout(2000)
        form2, controls2 = parse_scene(consoles)
        # fresh listener missed early lines; reuse full console list
        form2, controls2 = parse_scene(consoles + consoles2)
        btn = next((v for k, v in controls2.items() if v[0] == 'Button' and v[5]), None)
        if form2 is None or btn is None:
            ctx.check(row, 'isolation:smoke-ready', False, f'form={form2} btn={btn}')
            row['dims']['isolation'] = 'failed'
            return finalize(row)
        base = ctx.shot(row, pg, 'smoke-base')
        lbls = [v for k, v in controls2.items() if v[0] == 'Label' and v[5]]
        status = sorted(lbls, key=lambda v: v[1])[1] if len(lbls) >= 2 else None
        px, py, hit = ctx.click_control(pg, consoles, row, form2, btn, expect_hit='Button')
        after = ctx.shot(row, pg, 'smoke-click')
        if status is not None:
            srect = (status[1] + CANVAS_OFFSET[0], 30 + status[2] + CANVAS_OFFSET[1],
                     status[3], status[4])
            d = region_diff(base, after, srect)
        else:
            d = 0
        ctx.check(row, 'isolation:smoke-click-works', hit and d > 100, f'hit={hit} changed={d}')
        row['dims']['isolation'] = 'passed' if row['assertions'][-1]['ok'] else 'failed'
        row['dims']['interaction'] = row['dims']['isolation']
    finally:
        pg.close()
    # passes iff the failure is observed AND contained (assertions encode both)
    return finalize(row)


def case_paint(ctx):
    row = ctx.new_row('paint-stroke-undo', 'paint')
    inv0 = ctx.inv()
    pg, consoles, errors, form, controls = ctx.launch(row, 'paint', wait_s=60, deadline_s=300)
    try:
        ctx.check(row, 'fixture:form', form is not None, f'form={form}')
        if form is None:
            row['dims']['launch'] = 'blocked'
            return finalize(row)
        row['dims']['launch'] = 'passed' if not errors else 'failed'
        ctx.check(row, 'launch:no-pageerrors', not errors, f'pageerrors={len(errors)}')
        fx, fy, fw, fh = form
        canvas = (fx + CANVAS_OFFSET[0] + fw // 4, fy + CANVAS_OFFSET[1] + fh // 3,
                  fw // 2, fh // 3)
        base = ctx.shot(row, pg, 'base')
        x0, y0 = canvas[0] + 40, canvas[1] + 40
        pg.mouse.move(x0, y0)
        pg.mouse.down()
        for i in range(1, 11):
            pg.mouse.move(x0 + i * 12, y0 + i * 6)
            pg.wait_for_timeout(120)
        pg.mouse.up()
        pg.wait_for_timeout(4000)
        after_stroke = ctx.shot(row, pg, 'stroke')
        d1 = region_diff(base, after_stroke, canvas)
        ctx.check(row, 'stroke:pixels-change', d1 > 200, f'changed={d1}')
        pg.keyboard.press('Control+z')
        pg.wait_for_timeout(4000)
        after_undo = ctx.shot(row, pg, 'undo')
        d2 = region_diff(base, after_undo, canvas)
        restored = d2 < max(200, d1 // 4)
        ctx.check(row, 'undo:restores-baseline', restored, f'residual={d2} stroke={d1}')
        ok = all(a['ok'] for a in row['assertions'] if a['name'].startswith(('stroke:', 'undo:')))
        row['dims']['interaction'] = 'passed' if ok else 'failed'
        row['dims']['usefulPixels'] = 'passed' if ok else 'failed'
        row['dims']['dataOrFileResult'] = 'blocked'
        row['untested'].append('redo + document bytes: no model readback')
        unexp = ctx.err_delta(row, inv0, ['paint'], row['artifacts']['t0'])
        ctx.check(row, 'errors:no-unexpected', not unexp, f'unexpected={len(unexp)}')
    finally:
        pg.close()
    return finalize(row)


def case_todo_scroll(ctx):
    row = ctx.new_row('todo-list-scroll', 'todo')
    inv0 = ctx.inv()
    pg, consoles, errors, form, controls = ctx.launch(row, 'todo')
    try:
        need = ['Task_TB', 'Add_BTN', 'NewTasks_LTV', 'DoneTask_BTN']
        missing = [n for n in need if n not in controls]
        ctx.check(row, 'fixture:controls', not missing, f'missing={missing}')
        if form is None or missing:
            row['dims']['launch'] = 'blocked'
            return finalize(row)
        row['dims']['launch'] = 'passed' if not errors else 'failed'
        ctx.check(row, 'launch:no-pageerrors', not errors, f'pageerrors={len(errors)}')
        tb = controls['Task_TB']
        tpx, tpy = to_page(tb[1] + tb[3] // 2, 30 + tb[2] + tb[4] // 2)
        add = controls['Add_BTN']
        lv = controls['NewTasks_LTV']
        newrect = (lv[1] + CANVAS_OFFSET[0], 30 + lv[2] + CANVAS_OFFSET[1], lv[3], lv[4])
        strip = (newrect[0] + newrect[2] - 14, newrect[1], 14, newrect[3])
        for i in range(4):
            ctx.click(pg, row, tpx, tpy, wait_s=0)
            pg.keyboard.press('Control+a')
            pg.keyboard.type(f'scroll-{i}', delay=20)
            pg.wait_for_timeout(600)
            ctx.click_control(pg, consoles, row, form, add, expect_hit='Button')
        four = ctx.shot(row, pg, 'four')
        for i in range(4, 6):
            ctx.click(pg, row, tpx, tpy, wait_s=0)
            pg.keyboard.press('Control+a')
            pg.keyboard.type(f'scroll-{i}', delay=20)
            pg.wait_for_timeout(600)
            ctx.click_control(pg, consoles, row, form, add, expect_hit='Button')
        six = ctx.shot(row, pg, 'six')
        # Overflow must paint a scrollbar (right strip changes vs 4-row state).
        dbar = region_diff(four, six, strip)
        ctx.check(row, 'scroll:scrollbar-paints', dbar > 40, f'changed={dbar}')
        # Wheeling over the list must reveal the tail rows (region changes).
        pg.mouse.move(newrect[0] + newrect[2] // 2, newrect[1] + newrect[3] // 2)
        pg.mouse.wheel(0, 400)
        pg.wait_for_timeout(2000)
        wheeled = ctx.shot(row, pg, 'wheeled')
        dwheel = region_diff(six, wheeled, newrect)
        ctx.check(row, 'scroll:wheel-reveals-tail', dwheel > 60, f'changed={dwheel}')
        # All six stored rows must drain through Done (offset click mapping).
        done = controls['DoneTask_BTN']
        for _ in range(6):
            y = first_text_row(wheeled if _ == 0 else ctx.shot(row, pg, f'drain{_}'), newrect)
            if y is None:
                break
            ctx.click(pg, row, newrect[0] + 30, y, wait_s=0)
            pg.wait_for_timeout(400)
            ctx.click_control(pg, consoles, row, form, done, expect_hit='Button')
        empty = ctx.shot(row, pg, 'drained')
        ctx.check(row, 'scroll:done-drains-all', first_text_row(empty, newrect) is None, 'new-list-empty')
        ok = all(a['ok'] for a in row['assertions'] if a['name'].startswith('scroll:'))
        row['dims']['interaction'] = 'passed' if ok else 'failed'
        row['dims']['usefulPixels'] = 'passed' if ok else 'failed'
        row['dims']['dataOrFileResult'] = 'blocked'
        row['untested'].append('thumb drag: track click pages, drag untested')
        unexp = ctx.err_delta(row, inv0, ['todo'], row['artifacts']['t0'])
        ctx.check(row, 'errors:no-unexpected', not unexp, f'unexpected={len(unexp)}')
    finally:
        pg.close()
    return finalize(row)


CASES = [case_smoke, case_todo, case_barcode, case_mdi, case_failed_ctor, case_paint, case_todo_scroll,
       case_tile_layout]
