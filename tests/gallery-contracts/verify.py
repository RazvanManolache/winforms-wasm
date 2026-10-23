"""Independent verifier: derives verdicts from rows+artifacts, rejects
stale/wrong-generation/missing/empty evidence. Candidate-reported success
is never enough on its own.

Hardening contract (adversarial brief):
- a 'passed' row with any failing assertion is rejected;
- a 'passed' row with any failed dimension is rejected, unless that
  dimension is explicitly allowed via the row's allowedFailedDims map
  (expected-failure tests declare which dims fail by design, with reason);
- a 'passed' launch on a fallback/error shell is rejected: the row's
  consoles artifact is scanned for 'could not create its window' /
  'could not continue' markers;
- expectedErrors is NOT a boolean escape hatch: it must be a list of
  {app?, phase?, signature} entries; every unexpected error must match at
  least one entry and every entry must be consumed;
- required cases (--expect-cases) missing from the run are rejected;
- a blocked required case is rejected unless named in --allow-blocked;
- the same screenshot hash appearing under two different rows is rejected
  (copy-pasted evidence);
- interaction/usefulPixels passed requires >=1 shot artifact on disk;
  launch passed requires the consoles artifact on disk;
- negative runs (generation['negative']) are rejected as acceptance
  unless --expect-negative is given.
Usage: python verify.py --run E:/tmp/gal-run1 [--expect-run ID] [--expect-rev N]
       [--expect-cases smoke-click-keys,...] [--allow-blocked case,...]
       [--expect-negative]
"""
import argparse
import hashlib
import json
import os
import sys

DIMENSIONS = ['launch', 'usefulPixels', 'interaction', 'dataOrFileResult',
              'isolation', 'performance', 'backendEvidence']
VALID = {'passed', 'failed', 'blocked', 'untested'}
FALLBACK_MARKERS = ('could not create its window', 'could not continue')


def sha(p):
    h = hashlib.sha256()
    with open(p, 'rb') as f:
        h.update(f.read())
    return h.hexdigest()


def read_consoles(row):
    cpath = (row.get('artifacts', {}) or {}).get('consoles')
    if not cpath or not os.path.exists(cpath):
        return None
    try:
        with open(cpath, encoding='utf-8', errors='replace') as f:
            return f.read()
    except OSError:
        return None


def error_matches(entry, app, err):
    """entry {app?, phase?, signature} vs '[phase] rest' error string."""
    if not isinstance(entry, dict):
        return False
    sig = entry.get('signature')
    if not isinstance(sig, str) or not sig:
        return False
    if entry.get('app') is not None and entry.get('app') != app:
        return False
    phase = entry.get('phase')
    rest = err
    if phase is not None:
        prefix = f'[{phase}]'
        if not err.startswith(prefix):
            return False
        rest = err[len(prefix):]
    return sig in rest


def verify_run(rundir, expect_run=None, expect_rev=None, expect_cases=None,
               allow_blocked=None, expect_negative=False):
    problems = []
    gen_path = os.path.join(rundir, 'generation.json')
    rows_path = os.path.join(rundir, 'rows.jsonl')
    if not os.path.exists(gen_path):
        return False, ['missing generation.json']
    if not os.path.exists(rows_path):
        return False, ['missing rows.jsonl']
    gen = json.load(open(gen_path))
    if expect_run and gen.get('run_id') != expect_run:
        problems.append(f"generation run mismatch: {gen.get('run_id')} != {expect_run}")
    if expect_rev is not None and gen.get('rev') != expect_rev:
        problems.append(f"generation rev mismatch: {gen.get('rev')} != {expect_rev}")
    if gen.get('negative') and not expect_negative:
        problems.append('negative run presented as acceptance (missing --expect-negative)')
    if expect_negative and not gen.get('negative'):
        problems.append('--expect-negative given but run is not a negative run')
    rows = [json.loads(line) for line in open(rows_path) if line.strip()]
    if not rows:
        problems.append('no rows')
    if expect_cases:
        have = {r.get('id') for r in rows}
        for cid in expect_cases:
            if cid not in have:
                problems.append(f'missing required case {cid}')
    seen_shot_contents = {}
    for r in rows:
        rid = r.get('id', '?')
        app = r.get('app', '?')
        g = r.get('generation', {})
        if g.get('run_id') != gen.get('run_id') or g.get('rev') != gen.get('rev'):
            problems.append(f'{rid}: row generation {g} != pinned {gen.get("rev")}/{gen.get("run_id")}')
            continue
        status = r.get('status')
        if status not in ('passed', 'failed', 'blocked'):
            problems.append(f'{rid}: bad status {status!r}')
            continue
        if (status == 'blocked' and expect_cases and rid in expect_cases
                and not (allow_blocked and rid in allow_blocked)):
            problems.append(f'{rid}: required case blocked (not in --allow-blocked)')
        asserts = r.get('assertions', [])
        if status == 'passed' and not asserts:
            problems.append(f'{rid}: passed with zero assertions')
        for a in asserts:
            if status == 'passed' and not a.get('ok', False):
                problems.append(f"{rid}: passed with failing assertion {a.get('name')!r}")
                break
        dims = r.get('dims') or {}
        for d, v in dims.items():
            if d not in DIMENSIONS or v not in VALID:
                problems.append(f'{rid}: bad dim {d}={v!r}')
        allowed = r.get('allowedFailedDims') or {}
        if not isinstance(allowed, dict):
            problems.append(f'{rid}: allowedFailedDims must be a map dim->reason')
            allowed = {}
        if status == 'passed':
            for d, v in dims.items():
                if v == 'failed' and (d not in allowed or not allowed[d]):
                    problems.append(f'{rid}: passed with failed dimension {d}')
        for p, want in (r.get('artifacts', {}).get('sha256', {}) or {}).items():
            if want == 'MISSING' or not os.path.exists(p):
                problems.append(f'{rid}: missing artifact {p}')
            elif sha(p) != want:
                problems.append(f'{rid}: artifact changed after run {p}')
        for sp in (r.get('artifacts', {}).get('shots', []) or []):
            if os.path.exists(sp):
                digest = sha(sp)
                if digest in seen_shot_contents and seen_shot_contents[digest] != rid:
                    problems.append(
                        f'{rid}: shot replays evidence from {seen_shot_contents[digest]}')
                else:
                    seen_shot_contents.setdefault(digest, rid)
        shots = [p for p in (r.get('artifacts', {}).get('shots', []) or []) if os.path.exists(p)]
        cpath = (r.get('artifacts', {}) or {}).get('consoles')
        if cpath is not None and not os.path.exists(cpath):
            problems.append(f'{rid}: missing consoles artifact')
        if status == 'passed':
            if dims.get('launch') == 'passed' and cpath is not None and not os.path.exists(cpath):
                problems.append(f'{rid}: launch passed without consoles evidence')
            if dims.get('interaction') == 'passed' and not shots:
                problems.append(f'{rid}: interaction passed without shot evidence')
            if dims.get('usefulPixels') == 'passed' and not shots:
                problems.append(f'{rid}: usefulPixels passed without shot evidence')
            text = read_consoles(r)
            if dims.get('launch') == 'passed' and text is not None:
                for m in FALLBACK_MARKERS:
                    if m in text:
                        exp = r.get('expectedErrors')
                        covered = (isinstance(exp, list)
                                   and any(isinstance(e, dict) and e.get('signature') in text
                                           for e in exp))
                        if not covered:
                            problems.append(f'{rid}: launch passed on fallback shell ({m!r})')
                        break
        exp = r.get('expectedErrors', None)
        if isinstance(exp, bool) or (exp is not None and not isinstance(exp, list)):
            problems.append(f'{rid}: expectedErrors must be a list of entries (boolean is not specific)')
        unexp = (r.get('errors') or {}).get('unexpected') or []
        if status == 'passed' and unexp:
            if not isinstance(exp, list):
                problems.append(f'{rid}: passed despite unexpected errors')
            else:
                for e in unexp:
                    if not any(error_matches(en, app, e) for en in exp):
                        problems.append(f'{rid}: unexpected error matches no expectedErrors entry: {e[:120]!r}')
                        break
                used = [False] * len(exp)
                for e in unexp:
                    for i, en in enumerate(exp):
                        if error_matches(en, app, e):
                            used[i] = True
                for i, en in enumerate(exp):
                    if isinstance(en, dict) and not used[i]:
                        problems.append(f'{rid}: stale expectedErrors entry never observed: {en}')
    ok = not problems
    return ok, problems


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--run', required=True)
    ap.add_argument('--expect-run', default=None)
    ap.add_argument('--expect-rev', type=int, default=None)
    ap.add_argument('--expect-cases', default=None)
    ap.add_argument('--allow-blocked', default=None)
    ap.add_argument('--expect-negative', action='store_true')
    args = ap.parse_args()
    expect_cases = args.expect_cases.split(',') if args.expect_cases else None
    allow_blocked = args.allow_blocked.split(',') if args.allow_blocked else None
    ok, problems = verify_run(args.run, args.expect_run, args.expect_rev,
                              expect_cases, allow_blocked, args.expect_negative)
    print(json.dumps({'run': args.run, 'verdict': 'accept' if ok else 'reject',
                      'problems': problems}, indent=1))
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main())
