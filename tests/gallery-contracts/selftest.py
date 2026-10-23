"""Checker self-tests: the verifier must REJECT malformed, missing,
stale, and replayed evidence, plus the adversarial battery: false
assertions, failed dimensions, fallback shells, vague expectedErrors,
missing/blocked required cases, duplicated evidence, missing evidence,
and negative runs presented as acceptance. No browser needed.
Usage: python selftest.py  (exit 0 iff every rejection/acceptance fires)"""
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

# 1x1 transparent PNG (67 bytes) so shot artifacts are real image files.
PNG_1X1 = bytes.fromhex(
    '89504e470d0a1a0a0000000d49484452000000010000000108060000001f15c489'
    '0000000a49444154789c63000100000500010d0a2db40000000049454e44ae426082')


def good_row(run_id, rev, shot=None, consoles=None, sha=None):
    arts = {'shots': [], 'sha256': {}}
    if shot and sha:
        arts = {'shots': [shot], 'sha256': {shot: sha}}
    if consoles:
        arts['consoles'] = consoles
        arts['consoleCount'] = 3
    return {'id': 'self-ok', 'app': 'smoke',
            'generation': {'rev': rev, 'run_id': run_id},
            'status': 'passed',
            'dims': {d: ('passed' if d in ('launch', 'interaction') else 'untested')
                     for d in DIMENSIONS},
            'assertions': [{'name': 'click:routed', 'ok': True, 'detail': 'hit=Button'}],
            'artifacts': arts,
            'errors': {'unexpected': [], 'firstChance': 0},
            'untested': []}


def sha_of(p):
    h = hashlib.sha256()
    with open(p, 'rb') as f:
        h.update(f.read())
    return h.hexdigest()


def make_run(tmp, run_id='run1', rev=82, mutate=None, drop_file=None,
             with_evidence=True, negative=False):
    d = os.path.join(tmp, run_id)
    os.makedirs(d, exist_ok=True)
    gen = {'rev': rev, 'run_id': run_id, 'port': 5213}
    if negative:
        gen['negative'] = True
    json.dump(gen, open(os.path.join(d, 'generation.json'), 'w'))
    shot = os.path.join(d, 'shot.png')
    with open(shot, 'wb') as f:
        f.write(PNG_1X1)
    consoles = os.path.join(d, 'consoles-self-ok.txt')
    with open(consoles, 'w') as f:
        f.write('[log] constructed SmokeForm1\n')
    if with_evidence:
        row = good_row(run_id, rev, shot, consoles, sha_of(shot))
    else:
        row = good_row(run_id, rev)
    if mutate:
        mutate(row)
    if drop_file != 'rows':
        if 'sha256' in row.get('artifacts', {}):
            for p, want in list(row['artifacts']['sha256'].items()):
                if os.path.exists(p):
                    row['artifacts']['sha256'][p] = sha_of(p)
        with open(os.path.join(d, 'rows.jsonl'), 'w') as f:
            f.write(json.dumps(row) + '\n')
    return d


def add_second_row(d, run_id, rev, shot_name='shot2.png'):
    shot = os.path.join(d, shot_name)
    with open(shot, 'wb') as f:
        f.write(PNG_1X1 + b'\x00')
    row = good_row(run_id, rev, shot, None, sha_of(shot))
    row['id'] = 'self-ok-2'
    with open(os.path.join(d, 'rows.jsonl'), 'a') as f:
        f.write(json.dumps(row) + '\n')
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
    # 9. passed with a FALSE assertion rejected
    def false_assert(r):
        r['assertions'].append({'name': 'click:routed-2', 'ok': False, 'detail': 'miss'})
    d = make_run(tmp, run_id='run9', mutate=false_assert)
    verdict, _ = verify_run(d)
    ok &= check('reject-false-assertion', not verdict)
    # 10. passed with a failed required dimension rejected ...
    d = make_run(tmp, run_id='run10', mutate=lambda r: r['dims'].update(launch='failed'))
    verdict, _ = verify_run(d)
    ok &= check('reject-failed-dim', not verdict)
    # 10b. ... unless explicitly allowed with a reason
    def allow_launch(r):
        r['dims'].update(launch='failed')
        r['allowedFailedDims'] = {'launch': 'failure under test by design'}
    d = make_run(tmp, run_id='run10b', mutate=allow_launch)
    verdict, _ = verify_run(d)
    ok &= check('accept-allowed-failed-dim', verdict)
    # 11. boolean expectedErrors rejected (vague allowlist)
    def bool_exp(r):
        r['errors']['unexpected'] = ['[app-load] boom']
        r['expectedErrors'] = True
    d = make_run(tmp, run_id='run11', mutate=bool_exp)
    verdict, _ = verify_run(d)
    ok &= check('reject-bool-expectedErrors', not verdict)
    # 12. specific expectedErrors accepted when it matches ...
    def exact_exp(r):
        r['errors']['unexpected'] = ['[app-load] InvalidOperation||could not create its window']
        r['expectedErrors'] = [{'app': 'smoke', 'phase': 'app-load',
                                'signature': 'could not create its window'}]
    d = make_run(tmp, run_id='run12', mutate=exact_exp)
    verdict, probs = verify_run(d)
    ok &= check('accept-exact-expectedErrors', verdict)
    # 12b. ... and stale (unconsumed) entries rejected
    def stale_exp(r):
        r['errors']['unexpected'] = ['[app-load] boom']
        r['expectedErrors'] = [{'app': 'smoke', 'phase': 'app-load',
                                'signature': 'something else entirely'}]
    d = make_run(tmp, run_id='run12b', mutate=stale_exp)
    verdict, _ = verify_run(d)
    ok &= check('reject-stale-expectedErrors', not verdict)
    # 13. missing required case rejected
    d = make_run(tmp, run_id='run13')
    verdict, _ = verify_run(d, expect_cases=['self-ok', 'self-missing'])
    ok &= check('reject-missing-required-case', not verdict)
    # 14. blocked required case rejected unless allowed
    def block_it(r):
        r['status'] = 'blocked'
    d = make_run(tmp, run_id='run14', mutate=block_it)
    verdict, _ = verify_run(d, expect_cases=['self-ok'])
    ok &= check('reject-blocked-required', not verdict)
    verdict, _ = verify_run(d, expect_cases=['self-ok'], allow_blocked=['self-ok'])
    ok &= check('accept-allowed-blocked', verdict)
    # 15. duplicated shot hash across rows rejected
    d = make_run(tmp, run_id='run15')
    add_second_row(d, 'run15', 82)
    # force the second row to reuse the first row's shot bytes
    shutil.copy(os.path.join(d, 'shot.png'), os.path.join(d, 'shot2.png'))
    rows = [json.loads(l) for l in open(os.path.join(d, 'rows.jsonl')) if l.strip()]
    for r in rows:
        for p in list(r.get('artifacts', {}).get('sha256', {})):
            if os.path.exists(p):
                r['artifacts']['sha256'][p] = sha_of(p)
    with open(os.path.join(d, 'rows.jsonl'), 'w') as f:
        for r in rows:
            f.write(json.dumps(r) + '\n')
    verdict, _ = verify_run(d)
    ok &= check('reject-duplicate-shot', not verdict)
    # 16. launch passed on a fallback shell rejected ...
    def fallback_consoles(r):
        with open(r['artifacts']['consoles'], 'w') as f:
            f.write('[log] constructed SmokeForm1\n'
                    '[report:app-load] InvalidOperation||X could not create its window\n')
    d = make_run(tmp, run_id='run16', mutate=fallback_consoles)
    verdict, _ = verify_run(d)
    ok &= check('reject-fallback-launch', not verdict)
    # 16b. ... unless the fallback signature is an expected error
    def fallback_covered(r):
        fallback_consoles(r)
        r['expectedErrors'] = [{'app': 'smoke', 'phase': 'report:app-load',
                                'signature': 'could not create its window'}]
        r['errors']['unexpected'] = ['[report:app-load] InvalidOperation||X could not create its window']
    d = make_run(tmp, run_id='run16b', mutate=fallback_covered)
    verdict, probs = verify_run(d)
    ok &= check('accept-covered-fallback', verdict)
    # 17. interaction passed without shot evidence rejected
    def drop_shots(r):
        r['artifacts']['shots'] = []
        r['artifacts']['sha256'] = {}
    d = make_run(tmp, run_id='run17', mutate=drop_shots)
    verdict, _ = verify_run(d)
    ok &= check('reject-interaction-without-shot', not verdict)
    # 18. negative run presented as acceptance rejected ...
    d = make_run(tmp, run_id='run18', negative=True)
    verdict, _ = verify_run(d)
    ok &= check('reject-negative-as-acceptance', not verdict)
    # 18b. ... accepted only with --expect-negative
    verdict, _ = verify_run(d, expect_negative=True)
    ok &= check('accept-expected-negative', verdict)
    # 19. a failed application row still verifies as a REPORT (isolation
    # stays separate from the app outcome)
    def fail_row(r):
        r['status'] = 'failed'
        r['dims'].update(launch='failed')
    d = make_run(tmp, run_id='run19', mutate=fail_row)
    verdict, _ = verify_run(d)
    ok &= check('accept-failed-row-report', verdict)
    print('selftest: ' + ('ALL GREEN' if ok else 'FAILURES PRESENT'))
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main())
