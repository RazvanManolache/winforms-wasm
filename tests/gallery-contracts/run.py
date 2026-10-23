"""Gallery runner: pin one generation, run cases, emit machine-readable rows.
Usage: python run.py --out E:/tmp/gal-run1 --cases smoke,todo [--negative]
"""
import argparse
import hashlib
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from playwright.sync_api import sync_playwright

import cases
from core import pin_generation


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    ap.add_argument('--cases', default='all')
    ap.add_argument('--port', type=int, default=5213)
    ap.add_argument('--negative', action='store_true',
                    help='invert pixel oracles: every change-expectation must FAIL live')
    args = ap.parse_args()

    os.makedirs(args.out, exist_ok=True)
    gen = pin_generation(args.port)
    base = f'http://127.0.0.1:{args.port}/'
    # health gate: generation server must answer before any case runs
    import urllib.request
    try:
        with urllib.request.urlopen(base + 'api/preview-config', timeout=30) as r:
            cfg = json.load(r)
        gen['apps'] = len(cfg.get('apps', []))
    except Exception as e:
        print(f'FATAL: generation server unreachable: {e}')
        return 2
    if gen['rev'] is None:
        print('FATAL: could not pin TransformRevision')
        return 2
    # Negative runs invert oracles on purpose: mark the generation so the
    # report can never be mistaken for a real acceptance pass (the verifier
    # rejects negative runs unless --expect-negative is given).
    gen['negative'] = bool(args.negative)
    json.dump(gen, open(f'{args.out}/generation.json', 'w'), indent=1)

    wanted = args.cases.split(',') if args.cases != 'all' else None
    rows = []
    with sync_playwright() as pw:
        ctx = cases.Ctx(pw, base.rstrip('/'), args.out, gen, negative=args.negative)
        try:
            for fn in cases.CASES:
                if wanted and fn.__name__ not in wanted and fn.__name__.replace('case_', '') not in wanted:
                    continue
                print(f'case={fn.__name__} ...', flush=True)
                try:
                    row = fn(ctx)
                except Exception as e:
                    row = ctx.new_row(fn.__name__, '?')
                    row['assertions'].append({'name': 'harness:exception', 'ok': False,
                                              'detail': f'{type(e).__name__}: {str(e)[:200]}'})
                    row['status'] = 'failed'
                rows.append(row)
                try:
                    cpath = f"{args.out}/consoles-{row['id']}.txt"
                    with open(cpath, 'w', encoding='utf-8') as f:
                        for c in (ctx.consoles or []):
                            f.write(c + '\n')
                    row['artifacts']['consoles'] = cpath
                    row['artifacts']['consoleCount'] = len(ctx.consoles or [])
                except Exception:
                    pass
                print(f"case={fn.__name__} status={row['status']}", flush=True)
        finally:
            ctx.close()

    with open(f'{args.out}/rows.jsonl', 'w') as f:
        for r in rows:
            # artifact hashes let the verifier reject swapped/stale evidence
            arts = dict(r['artifacts'])
            hashes = {}
            for p in arts.get('shots', []):
                try:
                    h = hashlib.sha256()
                    with open(p, 'rb') as fh:
                        h.update(fh.read())
                    hashes[p] = h.hexdigest()
                except OSError:
                    hashes[p] = 'MISSING'
            arts['sha256'] = hashes
            r['artifacts'] = arts
            f.write(json.dumps(r) + '\n')
    n_pass = sum(1 for r in rows if r['status'] == 'passed')
    n_fail = sum(1 for r in rows if r['status'] == 'failed')
    n_block = sum(1 for r in rows if r['status'] == 'blocked')
    print(f'run={gen["run_id"]} rev={gen["rev"]} passed={n_pass}/{len(rows)} '
          f'failed={n_fail} blocked={n_block} out={args.out}')
    # A report describing failed/blocked workflows must NOT exit success:
    # the acceptance command gates on this code.
    return 0 if (n_fail == 0 and n_block == 0 and rows) else 1


if __name__ == '__main__':
    sys.exit(main())
