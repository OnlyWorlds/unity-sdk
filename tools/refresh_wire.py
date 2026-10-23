#!/usr/bin/env python3
"""Refresh the wire fixture: the parts of keel's OpenAPI document the client's wire tests check.

The EditMode suite's OWWireContractTests drive every OWClient call through a fake transport and
check each request against this fixture: the path is one keel serves, the method is one it takes
there, every query parameter and header the client sends is one keel declares on that operation,
and every key of a JSON body it sends is one keel's request schema names. They also check the
other way, for the models the SDK reads: every key a model maps is one keel's answer declares, and
every error code the OWApiError helpers branch on is one keel's document lists. Two client routes
were wrong for months while their own tests passed, because those tests asserted what the client
sent rather than what keel serves (EditLinksAsync's /editLinks/, ChangesAsync's cursor=; both found
2026-10-16). keel publishes the document; this fixture is its snapshot, so the suite needs no network.

Per operation the fixture keeps `query` and `header` (parameter names, always present), `body` (the
JSON request body's property names, when the operation takes one), `body_patterns` (the request
schema's patternProperties keys, when it has any: keel's extension keys, `^(atlas_|shadow_|x_)`, on
the element writes and PATCH /world) and `response` (the 200/201 JSON
answer's property names, when it declares one). Both name lists go one level down: `world.name`
for an object's property, `characters[].taken` for an array of objects' items. A `$ref` is
resolved into components; `allOf`, `oneOf` and `anyOf` are merged (an answer told apart by `kind`
lists every branch's keys). Top level: `error_codes`, every code in the Error schema's
`x-ow-error-codes`, with its HTTP status.

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
JSON = 'application/json'


def resolve(doc, node, seen=()):
    """Follow a local `$ref` (`#/components/...`) to the node it names; a cycle stops at an empty schema."""
    while isinstance(node, dict) and '$ref' in node:
        ref = node['$ref']
        if ref in seen or not ref.startswith('#/'):
            return {}
        seen = seen + (ref,)
        target = doc
        for part in ref[2:].split('/'):
            target = target.get(part, {}) if isinstance(target, dict) else {}
        node = target
    return node if isinstance(node, dict) else {}


def properties(doc, schema):
    """An object schema's properties, name -> schema, with allOf/oneOf/anyOf merged into one map."""
    schema = resolve(doc, schema)
    props = dict(schema.get('properties', {}))
    for key in ('allOf', 'oneOf', 'anyOf'):
        for branch in schema.get(key, []):
            for name, sub in properties(doc, branch).items():
                props.setdefault(name, sub)
    return props


def names(doc, schema):
    """Property names, sorted, one level down: `a`, `a.b` for an object, `a[].b` for an array of objects."""
    out = set()
    for name, sub in properties(doc, schema).items():
        out.add(name)
        sub = resolve(doc, sub)
        for child in properties(doc, sub):
            out.add(f'{name}.{child}')
        for child in properties(doc, sub.get('items', {})):
            out.add(f'{name}[].{child}')
    return sorted(out)


def patterns(doc, schema):
    """An object schema's patternProperties keys (regexes a key may match), allOf/oneOf/anyOf merged, sorted."""
    schema = resolve(doc, schema)
    out = set(schema.get('patternProperties', {}))
    for key in ('allOf', 'oneOf', 'anyOf'):
        for branch in schema.get(key, []):
            out.update(patterns(doc, branch))
    return sorted(out)


def json_schema(doc, holder):
    """The application/json schema of a requestBody or a response, or None when it has none."""
    holder = resolve(doc, holder)
    media = holder.get('content', {}).get(JSON)
    return None if media is None or 'schema' not in media else media['schema']


def extract(doc):
    """paths -> method -> {query, header[, body][, response]}, the auth headers, and the error codes."""
    paths = {}
    for path, item in doc.get('paths', {}).items():
        shared = item.get('parameters', [])
        ops = {}
        for method in METHODS:
            op = item.get(method)
            if op is None:
                continue
            params = [resolve(doc, p) for p in shared + op.get('parameters', [])]
            entry = {
                'query': sorted({p['name'] for p in params if p.get('in') == 'query'}),
                'header': sorted({p['name'] for p in params if p.get('in') == 'header'}),
            }
            body = json_schema(doc, op.get('requestBody', {}))
            if body is not None:
                entry['body'] = names(doc, body)
                if patterns(doc, body):
                    entry['body_patterns'] = patterns(doc, body)
            answers = [json_schema(doc, op.get('responses', {}).get(code, {})) for code in ('200', '201')]
            answers = [a for a in answers if a is not None]
            if answers:
                entry['response'] = sorted({n for a in answers for n in names(doc, a)})
            ops[method.upper()] = entry
        if ops:
            paths[path] = ops
    schemes = doc.get('components', {}).get('securitySchemes', {}).values()
    auth = sorted(s['name'] for s in schemes if s.get('type') == 'apiKey' and s.get('in') == 'header')
    bearer = any(s.get('type') == 'http' and s.get('scheme') == 'bearer' for s in schemes)
    codes = doc.get('components', {}).get('schemas', {}).get('Error', {}).get('x-ow-error-codes', {})
    errors = {code: spec.get('status') for code, spec in codes.items()}
    return paths, auth + (['Authorization'] if bearer else []), errors


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('--url', default=DEFAULT_URL)
    ap.add_argument('--check', action='store_true', help='compare only; exit 1 if the document moved')
    args = ap.parse_args()

    with urllib.request.urlopen(args.url, timeout=60) as r:
        raw = r.read()
    sha = hashlib.sha256(raw).hexdigest()
    doc = json.loads(raw)
    paths, auth, errors = extract(doc)

    if args.check:
        with open(FIXTURE, encoding='utf-8', newline='') as f:
            have = json.load(f)
        moved = have.get('paths') != paths or have.get('auth_headers') != auth or have.get('error_codes') != errors
        print(f"wire fixture {'MOVED' if moved else 'current'}: {len(paths)} paths, {len(errors)} error codes "
              f"(fixture fetched {have.get('fetched')}, document sha {sha[:12]})")
        return 1 if moved else 0

    out = {
        'source': args.url,
        'fetched': datetime.datetime.now().astimezone().isoformat(timespec='minutes'),
        'openapi_version': doc.get('info', {}).get('version'),
        'document_sha256': sha,
        'auth_headers': auth,
        'error_codes': errors,
        'paths': paths,
    }
    os.makedirs(os.path.dirname(FIXTURE), exist_ok=True)
    with open(FIXTURE, 'w', encoding='utf-8', newline='\n') as f:
        f.write(json.dumps(out, indent=2, sort_keys=True) + '\n')
    print(f"wrote {os.path.relpath(FIXTURE, PROJECT)}: {len(paths)} paths, {len(errors)} error codes, "
          f"auth {auth}, sha {sha[:12]}")
    return 0


if __name__ == '__main__':
    sys.exit(main())
