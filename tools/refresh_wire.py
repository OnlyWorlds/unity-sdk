#!/usr/bin/env python3
"""Refresh the wire fixture: the parts of keel's OpenAPI document the client's wire tests check.

The EditMode suite's OWWireContractTests drive every OWClient call through a fake transport and
check each request against this fixture: the path is one keel serves, the method is one it takes
there, and every query parameter and header the client sends is one keel declares on that
operation. Two client routes were wrong for months while their own tests passed, because those
tests asserted what the client sent rather than what keel serves (EditLinksAsync's /editLinks/,
ChangesAsync's cursor=; both found 2026-10-16). keel publishes the document; this fixture is its
snapshot, so the suite needs no network.

Usage:
    python tools/refresh_wire.py                     # production's document
    python tools/refresh_wire.py --url URL           # another keel (a staging host)
    python tools/refresh_wire.py --check             # exit 1 if production moved past the fixture

Writes Packages/com.onlyworlds.sdk/Tests/Editor/Fixtures/wire/keel-wire.json (LF, sorted keys) with
the source URL, the fetch time and the sha256 of the whole document, so a moved document is visible.
Then run the gate: a new red there is either a client bug or a keel change to answer.
"""
import argparse
import datetime
import hashlib
import json
import os
import sys
import urllib.request

PROJECT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
FIXTURE = os.path.join(PROJECT, 'Packages', 'com.onlyworlds.sdk', 'Tests', 'Editor', 'Fixtures', 'wire', 'keel-wire.json')
DEFAULT_URL = 'https://www.onlyworlds.com/api/v2/openapi.json'
METHODS = ('get', 'put', 'post', 'patch', 'delete')


def extract(doc):
    """paths -> method -> {query: [...], header: [...]}, plus the auth headers the schemes name."""
    paths = {}
    for path, item in doc.get('paths', {}).items():
        shared = item.get('parameters', [])
        ops = {}
        for method in METHODS:
            op = item.get(method)
            if op is None:
                continue
            params = shared + op.get('parameters', [])
            ops[method.upper()] = {
                'query': sorted({p['name'] for p in params if p.get('in') == 'query'}),
                'header': sorted({p['name'] for p in params if p.get('in') == 'header'}),
            }
        if ops:
            paths[path] = ops
    auth = sorted(
        s['name'] for s in doc.get('components', {}).get('securitySchemes', {}).values()
        if s.get('type') == 'apiKey' and s.get('in') == 'header'
    )
    bearer = any(
        s.get('type') == 'http' and s.get('scheme') == 'bearer'
        for s in doc.get('components', {}).get('securitySchemes', {}).values()
    )
    return paths, auth + (['Authorization'] if bearer else [])


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('--url', default=DEFAULT_URL)
    ap.add_argument('--check', action='store_true', help='compare only; exit 1 if the document moved')
    args = ap.parse_args()

    with urllib.request.urlopen(args.url, timeout=60) as r:
        raw = r.read()
    sha = hashlib.sha256(raw).hexdigest()
    doc = json.loads(raw)
    paths, auth = extract(doc)

    if args.check:
        with open(FIXTURE, encoding='utf-8') as f:
            have = json.load(f)
        moved = have.get('paths') != paths or have.get('auth_headers') != auth
        print(f"wire fixture {'MOVED' if moved else 'current'}: {len(paths)} paths "
              f"(fixture fetched {have.get('fetched')}, document sha {sha[:12]})")
        return 1 if moved else 0

    out = {
        'source': args.url,
        'fetched': datetime.datetime.now().astimezone().isoformat(timespec='minutes'),
        'openapi_version': doc.get('info', {}).get('version'),
        'document_sha256': sha,
        'auth_headers': auth,
        'paths': paths,
    }
    os.makedirs(os.path.dirname(FIXTURE), exist_ok=True)
    with open(FIXTURE, 'w', encoding='utf-8', newline='\n') as f:
        f.write(json.dumps(out, indent=2, sort_keys=True) + '\n')
    print(f"wrote {os.path.relpath(FIXTURE, PROJECT)}: {len(paths)} paths, auth {auth}, sha {sha[:12]}")
    return 0


if __name__ == '__main__':
    sys.exit(main())
