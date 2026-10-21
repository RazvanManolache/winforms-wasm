"""Gallery runner core: generation pin, browser launch, console capture,
pixel helpers. Real browser input only (playwright mouse/keyboard)."""
import hashlib
import json
import re
import uuid
from pathlib import Path
from datetime import datetime, timezone

CANVAS_OFFSET = (301, 53)  # canvas top-left in a 1500x950 viewport; verified by click logs
VIEWPORT = (1500, 950)

SRC = str(Path(__file__).resolve().parents[2] / 'src')
BIN = SRC + '/WfWasm.Host.Server/bin/Debug/net9.0'


def file_head_sha(path, n=1 << 26):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        h.update(f.read(n))
    return h.hexdigest() + f'+{n}'


def pin_generation(port=5213):
    import glob
    rev = None
    src = open(SRC + '/WfWasm.Host.Server/AppTransform.cs', encoding='utf-8').read()
    m = re.search(r'TransformRevision\s*=\s*(\d+)', src)
    if m:
        rev = int(m.group(1))
    fw = sorted(glob.glob(BIN + '/wwwroot/_framework/WfWasm.WinForms.*.wasm'))
    fw = [f for f in fw if not f.endswith('.gz')]
    gen = {
        'rev': rev,
        'server_dll': file_head_sha(BIN + '/WfWasm.Host.Server.dll'),
        'bundle_wasm': [f.split('/')[-1] + ':' + file_head_sha(f) for f in fw],
        'fork_src': file_head_sha(SRC + '/WfWasm.WinForms/bin/Debug/net9.0/WfWasm.WinForms.dll'),
        'drawing_src': file_head_sha(SRC + '/WfWasm.Drawing/bin/Debug/net9.0/WfWasm.Drawing.dll'),
        'port': port,
        'viewport': list(VIEWPORT),
        'run_id': uuid.uuid4().hex[:12],
        'started': datetime.now(timezone.utc).isoformat(),
    }
    return gen


CTRL_RE = re.compile(
    r"(\s*)- (\S+) '([^']*)' Bounds=\{X=(-?\d+),Y=(-?\d+),Width=(-?\d+),Height=(-?\d+)\} Visible=(\w+)")
ITEM_RE = re.compile(
    r"(\s*)\[Item\] (\S+) '([^']*)' .*?Bounds=\{X=(-?\d+),Y=(-?\d+),Width=(-?\d+),Height=(-?\d+)\} Visible=(\w+)")
SCENE_RE = re.compile(r"\[Scene\] Selected (\S+), Form=([^,]*), Size=\{Width=(\d+), Height=(\d+)\}")


def parse_scene(consoles):
    """Return (form_bounds, {name: (type, x, y, w, h, visible)}) in host coords.
    Child bounds accumulate through visible ancestors (client areas).
    ToolStrip/menu items ([Item], with Text) accumulate the same way so
    toolbar and menu targets are clickable."""
    controls = {}
    form = None
    stack = []  # (indent, absx, absy)
    for line in consoles:
        m = CTRL_RE.search(line) or ITEM_RE.search(line)
        if not m:
            continue
        indent = (len(m.group(1))) // 2
        typ, name = m.group(2), m.group(3)
        x, y, w, h = int(m.group(4)), int(m.group(5)), int(m.group(6)), int(m.group(7))
        vis = m.group(8) == 'True'
        while stack and stack[-1][0] >= indent:
            stack.pop()
        if stack:
            ox, oy = stack[-1][1], stack[-1][2]
        else:
            ox, oy = (0, 0)
        ax, ay = ox + x, oy + y
        if indent == 0:
            form = (ax, ay, w, h)
            stack.append((indent, ax, ay))
        else:
            key = name or f'{typ}@{ax},{ay}'
            if '[Item]' in line:
                tm = re.search(r"Text='([^']*)'", line)
                if tm and tm.group(1):
                    key = f'{typ}:{tm.group(1)[:40]}@{ax},{ay}'
            controls[key] = (typ, ax, ay, w, h, vis)
            stack.append((indent, ax, ay))
    return form, controls


def to_page(hx, hy):
    return (hx + CANVAS_OFFSET[0], hy + CANVAS_OFFSET[1])


def region_diff(img_a, img_b, rect, tol=24):
    """Count pixels in rect whose RGB differs by more than tol per channel."""
    from PIL import Image
    x, y, w, h = rect
    a = img_a.convert('RGB').crop((x, y, x + w, y + h))
    b = img_b.convert('RGB').crop((x, y, x + w, y + h))
    da, db = list(a.getdata()), list(b.getdata())
    n = 0
    for p, q in zip(da, db):
        if abs(p[0] - q[0]) > tol or abs(p[1] - q[1]) > tol or abs(p[2] - q[2]) > tol:
            n += 1
    return n


def diff_bbox(img_a, img_b, rect, tol=24):
    """Bounding box (page coords) of changed pixels within rect, or None."""
    x, y, w, h = rect
    a = img_a.convert('RGB').crop((x, y, x + w, y + h))
    b = img_b.convert('RGB').crop((x, y, x + w, y + h))
    da, db = list(a.getdata()), list(b.getdata())
    minx, miny, maxx, maxy = w, h, -1, -1
    for i, (p, q) in enumerate(zip(da, db)):
        if abs(p[0] - q[0]) > tol or abs(p[1] - q[1]) > tol or abs(p[2] - q[2]) > tol:
            cx, cy = i % w, i // w
            if cx < minx:
                minx = cx
            if cx > maxx:
                maxx = cx
            if cy < miny:
                miny = cy
            if cy > maxy:
                maxy = cy
    if maxx < 0:
        return None
    return (x + minx, y + miny, maxx - minx + 1, maxy - miny + 1)


def first_text_row(shot, rect, skip_top=18, dark_thresh=60, min_run=3):
    """Y-center of the first dark-text row below skip_top within rect."""
    img = shot.convert('RGB')
    x, y, w, h = rect
    for yy in range(y + skip_top, y + h - 2):
        n = 0
        for xx in range(x + 2, x + w - 2, 2):
            p = img.getpixel((xx, yy))
            if p[0] < dark_thresh and p[1] < dark_thresh and p[2] < dark_thresh:
                n += 1
                if n >= min_run:
                    break
        if n >= min_run:
            return yy
    return None
