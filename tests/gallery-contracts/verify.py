"""Independent verifier: derives verdicts from rows+artifacts, rejects
stale/wrong-generation/missing/empty evidence. Candidate-reported success
is never enough on its own.
Usage: python verify.py --run E:/tmp/gal-run1 [--expect-run ID] [--expect-rev N]
"""
import argparse
import hashlib
import json
import os
import sys

DIMENSIONS = ['launch', 'usefulPixels', 'interaction', 'dataOrFileResult',
              'isolation', 'performance', 'backendEvidence']
VALID = {'passed', 'failed', 'blocked', 'untested'}


def sha(p):
    h = hashlib.sha256()
    with open(p, 'rb') as f:
        h.update(f.read())
    return h.hexdigest()


def verify_run(rundir, expect_run=None, expect_rev=None):
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
    rows = [json.loads(line) for line in open(rows_path) if line.strip()]
    if not rows:
        problems.append('no rows')
    for r in rows:
        rid = r.get('id', '?')
        g = r.get('generation', {})
        if g.get('run_id') != gen.get('run_id') or g.get('rev') != gen.get('rev'):
            problems.append(f'{rid}: row generation {g} != pinned {gen.get("rev")}/{gen.get("run_id")}')
            continue
        if r.get('status') not in ('passed', 'failed', 'blocked'):
            problems.append(f'{rid}: bad status {r.get("status")!r}')
        asserts = r.get('assertions', [])
        if r.get('status') == 'passed' and not asserts:
            problems.append(f'{rid}: passed with zero assertions')
        for d, v in (r.get('dims') or {}).items():
            if d not in DIMENSIONS or v not in VALID:
                problems.append(f'{rid}: bad dim {d}={v!r}')
        for p, want in (r.get('artifacts', {}).get('sha256', {}) or {}).items():
            if want == 'MISSING' or not os.path.exists(p):
                problems.append(f'{rid}: missing artifact {p}')
            elif sha(p) != want:
                problems.append(f'{rid}: artifact changed after run {p}')
        cpath = (r.get('artifacts', {}) or {}).get('consoles')
        if cpath is not None and not os.path.exists(cpath):
            problems.append(f'{rid}: missing consoles artifact')
        # passed rows must not carry unexpected session errors, unless the
        # case declares the error report itself as the expected outcome
        if (r.get('status') == 'passed' and not r.get('expectedErrors')
                and (r.get('errors') or {}).get('unexpected')):
            problems.append(f'{rid}: passed despite unexpected errors')
    ok = not problems
    return ok, problems


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--run', required=True)
    ap.add_argument('--expect-run', default=None)
    ap.add_argument('--expect-rev', type=int, default=None)
    args = ap.parse_args()
    ok, problems = verify_run(args.run, args.expect_run, args.expect_rev)
    print(json.dumps({'run': args.run, 'verdict': 'accept' if ok else 'reject',
                      'problems': problems}, indent=1))
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main())
