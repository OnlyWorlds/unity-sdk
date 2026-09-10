#!/usr/bin/env python3
"""The SDK's measured pre-push gate: run the EditMode suite headlessly and assert it actually RAN.

Why a script and not `unity test` alone: the CLI exits 0 with result="Passed" when ZERO tests ran
(a --filter that matches nothing, a mode with no tests) — measured 2026-09-10. So the gate parses
the NUnit report and refuses a count of zero, and, when --expect is given, a count below it.

Usage:
    python tools/gate.py                 # EditMode, expects > 0 tests
    python tools/gate.py --expect 190    # refuse if fewer than 190 ran
    python tools/gate.py --mode PlayMode # (the SDK has no PlayMode tests today: this FAILS on count 0, honestly)

Needs: the `unity` CLI (Unity Hub 1.0.0-beta.8+; %LOCALAPPDATA%\\Unity\\bin\\unity.exe on Windows) and
the project NOT held by a GUI editor (Temp/UnityLockfile absent). Exit codes: 0 green · 8 tests failed
· 9 the suite did not run (zero tests, or fewer than expected) · other: the CLI's own failure.
"""
import argparse, os, subprocess, sys, tempfile, time
import xml.etree.ElementTree as ET

PROJECT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))


def find_unity():
    for candidate in ('unity', os.path.join(os.environ.get('LOCALAPPDATA', ''), 'Unity', 'bin', 'unity.exe')):
        try:
            r = subprocess.run([candidate, '--version', '--no-banner'], capture_output=True, text=True, timeout=30)
            if r.returncode == 0:
                return candidate
        except (FileNotFoundError, subprocess.TimeoutExpired):
            continue
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--mode', default='EditMode', choices=['EditMode', 'PlayMode'])
    ap.add_argument('--expect', type=int, default=0, help='minimum number of tests that must run')
    ap.add_argument('--timeout', type=int, default=900)
    ap.add_argument('--out', default=None, help='directory for the reports (default: a temp dir)')
    args = ap.parse_args()

    lock = os.path.join(PROJECT, 'Temp', 'UnityLockfile')
    if os.path.exists(lock):
        print(f'gate: {lock} exists — a GUI editor holds the project; close it or run from a copy', file=sys.stderr)
        return 9

    unity = find_unity()
    if unity is None:
        print('gate: the `unity` CLI was not found (Unity Hub 1.0.0-beta.8+)', file=sys.stderr)
        return 9

    out = args.out or tempfile.mkdtemp(prefix='ow-sdk-gate-')
    report = os.path.join(out, f'{args.mode.lower()}.xml')
    cmd = [unity, 'test', PROJECT, '--mode', args.mode, '--no-banner', '--non-interactive',
           '--timeout', str(args.timeout), '--output', report, '--report-format', 'nunit']
    t0 = time.time()
    r = subprocess.run(cmd, capture_output=True, text=True)
    wall = time.time() - t0
    if r.returncode not in (0, 8):
        print(f'gate: unity test exited {r.returncode} in {wall:.0f}s (no verdict)\n{r.stdout}\n{r.stderr}', file=sys.stderr)
        return r.returncode

    if not os.path.exists(report):
        print(f'gate: no report at {report} after exit {r.returncode}', file=sys.stderr)
        return 9
    run = ET.parse(report).getroot()
    total = int(run.get('testcasecount', run.get('total', '0')))
    passed = int(run.get('passed', '0')); failed = int(run.get('failed', '0')); skipped = int(run.get('skipped', '0'))
    print(f'gate: {args.mode} total={total} passed={passed} failed={failed} skipped={skipped} '
          f'wall={wall:.0f}s exit={r.returncode} report={report}')

    if total == 0:
        print('gate: ZERO tests ran — a green exit here is the trap (2026-09-10); refusing', file=sys.stderr)
        return 9
    if args.expect and total < args.expect:
        print(f'gate: only {total} tests ran, expected at least {args.expect}; refusing', file=sys.stderr)
        return 9
    if failed or r.returncode == 8:
        for case in run.iter('test-case'):
            if case.get('result') == 'Failed':
                msg = case.find('.//message')
                print(f'  FAILED {case.get("fullname")}: {(msg.text or "").strip()[:200] if msg is not None else ""}')
        return 8
    return 0


if __name__ == '__main__':
    sys.exit(main())
