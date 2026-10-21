"""Checker self-tests: the verifier must REJECT malformed, missing,
stale, and replayed evidence. No browser needed.
Usage: python selftest.py  (exit 0 iff every rejection fires)"""
import copy
import hashlib
import json
import os
import shutil
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from verify import verify_run

DIMENSIONS = ['launch', 'usefulPixels', 'interaction', 'dataOrFileResult',
              'isolation', 'performance', 'backendEvidence']


def good_row(run_id, rev):
    return {'id': 'self-ok', 'app': 'smoke',
            'generation': {'rev': rev, 'run_id': run_id},
            'status': 'passed',
            'dims': {d: ('passed' if d in ('launch', 'interaction') else 'untested')
                     for d in DIMENSIONS},
            'assertions': [{'name': 'click:routed', 'ok': True, 'detail': 'hit=Button'}],
            'artifacts': {'shots': [], 'sha256': {}},
            'errors': {'unexpected': [], 'firstChance': 0},
            'untested': []}


def make_run(tmp, run_id='run1', rev=82, mutate=None, drop_file=None):
    d = os.path.join(tmp, run_id)
    os.makedirs(d, exist_ok=True)
    gen = {'rev': rev, 'run_id': run_id, 'port': 5213}
    json.dump(gen, open(os.path.join(d, 'generation.json'), 'w'))
    row = good_row(run_id, rev)
    if mutate:
        mutate(row)
    if drop_file != 'rows':
        with open(os.path.join(d, 'rows.jsonl'), 'w') as f:
            f.write(json.dumps(row) + '\n')
    else:
        pass
    return d


def check(name, cond):
    print(('PASS' if cond else 'FAIL') + f' selftest:{name}')
    return cond


def main():
    tmp = tempfile.mkdtemp(prefix='galself-')
    ok = True
    # 1. good run accepted
    d = make_run(tmp)
    verdict, _ = verify_run(d)
    ok &= check('accept-good', verdict)
    # 2. stale generation rejected
    d = make_run(tmp, run_id='run2', mutate=lambda r: r['generation'].update(rev=81))
    verdict, _ = verify_run(d)
    ok &= check('reject-stale-rev', not verdict)
    # 3. passed with zero assertions rejected
    d = make_run(tmp, run_id='run3', mutate=lambda r: r.update(assertions=[]))
    verdict, _ = verify_run(d)
    ok &= check('reject-empty-assertions', not verdict)
    # 4. missing rows rejected
    d = make_run(tmp, run_id='run4', drop_file='rows')
    verdict, _ = verify_run(d)
    ok &= check('reject-missing-rows', not verdict)
    # 5. replayed evidence rejected: copy good run, claim a new run id
    src = make_run(tmp, run_id='run5')
    dst = os.path.join(tmp, 'run5x')
    shutil.copytree(src, dst)
    gen = json.load(open(os.path.join(dst, 'generation.json')))
    gen['run_id'] = 'run5x-forged'
    json.dump(gen, open(os.path.join(dst, 'generation.json'), 'w'))
    verdict, probs = verify_run(dst)
    ok &= check('reject-replayed-run-id', not verdict)
    # 6. passed-despite-unexpected-errors rejected
    def add_err(r):
        r['errors']['unexpected'] = ['[app-load] boom']
    d = make_run(tmp, run_id='run6', mutate=add_err)
    verdict, _ = verify_run(d)
    ok &= check('reject-passed-with-errors', not verdict)
    # 7. bad dimension value rejected
    d = make_run(tmp, run_id='run7', mutate=lambda r: r['dims'].update(launch='green'))
    verdict, _ = verify_run(d)
    ok &= check('reject-bad-dim', not verdict)
    # 8. wrong generation pin rejected via expect flags
    d = make_run(tmp, run_id='run8')
    verdict, _ = verify_run(d, expect_run='other', expect_rev=81)
    ok &= check('reject-pin-mismatch', not verdict)
    print('selftest: ' + ('ALL GREEN' if ok else 'FAILURES PRESENT'))
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main())
