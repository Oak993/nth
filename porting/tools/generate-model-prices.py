#!/usr/bin/env python3
"""Capture official pricing HTML, then build a cited table from reviewed cell mappings.

Windows: py porting/tools/generate-model-prices.py --inspect --captures <throwaway-dir>
Fill model-price-extraction.json with exact cell indices/quotes from those captures;
then py porting/tools/generate-model-prices.py --map <reviewed-map.json>.
Every build re-fetches the official pages. No numeric override/default token price.
Unfilled mappings, ambiguous cells, changed layouts and unavailable pages leave data untouched.
"""
import argparse
import datetime
import decimal
import hashlib
import html.parser
import json
import math
import pathlib
import re
import urllib.parse
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[2]
HOSTS = {'deepseek': 'api-docs.deepseek.com', 'meta': 'dev.meta.ai', 'zai': 'docs.z.ai'}

class Tables(html.parser.HTMLParser):
    def __init__(self):
        super().__init__(); self.tables = []; self.rows = None; self.row = None; self.cell = None; self.text = []
    def handle_starttag(self, tag, attrs):
        if tag == 'table':
            if self.rows is not None: raise ValueError('Nested pricing tables require a new reviewed extractor')
            self.rows = []
        elif tag == 'tr' and self.rows is not None: self.row = []
        elif tag in ('td', 'th') and self.row is not None:
            if any(k in ('colspan', 'rowspan') and v != '1' for k, v in attrs): raise ValueError('Spanned pricing cells require explicit reviewed mapping')
            self.cell = []
    def handle_data(self, data):
        self.text.append(data)
        if self.cell is not None: self.cell.append(data)
    def handle_endtag(self, tag):
        if tag in ('td', 'th') and self.cell is not None:
            self.row.append(' '.join(''.join(self.cell).split())); self.cell = None
        elif tag == 'tr' and self.row is not None:
            self.rows.append(self.row); self.row = None
        elif tag == 'table' and self.rows is not None:
            self.tables.append(self.rows); self.rows = None

def official(service, url):
    parsed = urllib.parse.urlparse(url)
    if parsed.scheme != 'https' or parsed.hostname != HOSTS.get(service) or parsed.port not in (None, 443) or parsed.username or parsed.password:
        raise ValueError('Expected the service\'s official HTTPS pricing host')

class NoRedirects(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *_): raise ValueError('Redirects require a reviewed canonical source URL')

def fetch(url):
    with urllib.request.build_opener(NoRedirects).open(urllib.request.Request(url, headers={'User-Agent': 'Marrow-Price-Table-Generator/1'}), timeout=30) as response:
        if response.status != 200: raise ValueError('Official pricing source did not return 200')
        data = response.read(2_000_001)
        if len(data) > 2_000_000: raise ValueError('Pricing source exceeded capture limit')
        return data

def parse(data):
    tables = Tables(); tables.feed(data.decode('utf-8')); tables.close()
    return tables

def extract(mapping, data, retrieved):
    official(mapping['service'], mapping['sourceUrl'])
    parsed = parse(data); text = ' '.join(' '.join(parsed.text).split())
    def quote(name):
        value = mapping.get(name)
        if not value or value not in text: raise ValueError('Missing/changed exact source quote: ' + name)
        return value
    unit = quote('tokenUnitQuote')
    if not re.search(r'(?:1,?000,?000|1\s*M|million)\s+tokens', unit, re.I) or 'USD' not in unit:
        raise ValueError('Source must explicitly establish USD per million tokens')
    def cell(name, optional=False):
        coords = mapping.get(name)
        if coords is None and optional: return None
        if not isinstance(coords, list) or len(coords) != 3 or any(type(x) is not int or x < 0 for x in coords): raise ValueError('Invalid cell mapping: ' + name)
        try: return parsed.tables[coords[0]][coords[1]][coords[2]]
        except IndexError as error: raise ValueError('Source layout changed: ' + name) from error
    if cell('modelCell') != mapping['model']: raise ValueError('Model identifier did not match its cited cell exactly')
    def money(name, optional=False):
        value = cell(name, optional)
        if value is None: return None
        match = re.fullmatch(r'\s*\$?\s*(\d+(?:\.\d+)?)\s*', value)
        if not match: raise ValueError('Ambiguous or non-USD numeric price cell: ' + name)
        amount = float(decimal.Decimal(match[1]))
        if not math.isfinite(amount): raise ValueError('Nonfinite source price: ' + name)
        return amount
    def tokens(name):
        value = cell(name, True)
        if value is None: return None
        if not re.fullmatch(r'[1-9][0-9,]*', value): raise ValueError('Token limits require exact integer counts, not guessed K/M multipliers')
        return int(value.replace(',', ''))
    request = money('requestCell', True)
    if request is None:
        evidence = quote('tokenOnlyQuote')
        if not re.search(r'no (?:per-request|request) fees', evidence, re.I): raise ValueError('No request-fee default without explicit source evidence')
        request = 0
    return dict(service=mapping['service'], model=mapping['model'], inputUsdPerMillion=money('inputCell'), outputUsdPerMillion=money('outputCell'),
                cachedInputUsdPerMillion=money('cachedInputCell', True), requestUsd=request, maximumInputTokens=tokens('maximumInputCell'),
                maximumOutputTokens=tokens('maximumOutputCell'), retrievedAt=retrieved, sourceUrl=mapping['sourceUrl'], sourceSha256=hashlib.sha256(data).hexdigest(),
                evidence=mapping)

def build(mappings, fetcher, json_path, cs_path):
    if not mappings: raise ValueError('Price table is UNFILLED: inspect official captures and supply reviewed mappings')
    cache = {}; prices = []; now = datetime.datetime.now(datetime.timezone.utc).isoformat().replace('+00:00', 'Z')
    for mapping in mappings:
        official(mapping['service'], mapping['sourceUrl'])
        url = mapping['sourceUrl']
        if url not in cache: cache[url] = fetcher(url)
        prices.append(extract(mapping, cache[url], now))
    if len({(p['service'], p['model']) for p in prices}) != len(prices): raise ValueError('Duplicate model/service price')
    document = json.dumps(dict(schemaVersion=1, state='filled', prices=prices), ensure_ascii=True, indent=2, allow_nan=False) + '\n'
    # C# escaped string is compiled into Services, so no csproj/content-copy or runtime path assumption is needed.
    cs = '// PORT-ONLY: generated from official pricing pages by generate-model-prices.py; source dates/hashes are in the JSON.\nnamespace Marrow.Services.Providers;\n\ninternal static class ModelPriceData\n{\n    internal const string Json = ' + json.dumps(document) + ';\n}\n'
    for path, content in ((json_path, document), (cs_path, cs)):
        path = pathlib.Path(path); pending = path.with_suffix(path.suffix + '.tmp'); pending.write_text(content, encoding='utf-8', newline='\n'); pending.replace(path)
    return prices

def main():
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument('--inspect', action='store_true'); cli.add_argument('--captures', type=pathlib.Path)
    cli.add_argument('--map', type=pathlib.Path, default=ROOT / 'src/Marrow.Services/Providers/Pricing/model-price-extraction.json')
    args = cli.parse_args(); mapping = json.loads(args.map.read_text(encoding='utf-8'))
    if args.inspect:
        if args.captures is None: cli.error('--inspect requires --captures (outside the repository is preferred)')
        args.captures.mkdir(parents=True, exist_ok=True)
        for source in mapping['sources']:
            official(source['service'], source['sourceUrl']); data = fetch(source['sourceUrl']); digest = hashlib.sha256(data).hexdigest()
            (args.captures / (digest + '.html')).write_bytes(data)
            (args.captures / (digest + '.tables.json')).write_text(json.dumps(dict(source=source, retrievedAt=datetime.datetime.now(datetime.timezone.utc).isoformat(), sha256=digest, tables=parse(data).tables), indent=2), encoding='utf-8')
        print('Official captures saved. Review exact model IDs, units, billing conditions and limits before mapping cells; no data was published.')
    else:
        prices = build(mapping['models'], fetch, ROOT / 'src/Marrow.Services/Providers/Pricing/model-prices.json', ROOT / 'src/Marrow.Services/Providers/ModelPriceData.cs')
        print('Generated', len(prices), 'dated model prices. Review/commit both JSON and C#; missing bounds remain unavailable to capped requests.')

if __name__ == '__main__': main()
