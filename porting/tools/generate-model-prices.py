#!/usr/bin/env python3
"""Capture official pricing HTML, then build a cited table from reviewed cell mappings.

Windows: py porting/tools/generate-model-prices.py --inspect --captures <throwaway-dir>
Fill model-price-extraction.json with exact cell indices/quotes from those captures;
then py porting/tools/generate-model-prices.py --map <reviewed-map.json>.
Every build re-fetches the official pages. No numeric override/default token price.
Legacy unreviewed builds preserve data on failure. Reviewed exact-ID builds isolate
unfilled models and emit their reasons in the fetch manifest.
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
EVIDENCE_HOSTS = {'deepseek': {'api-docs.deepseek.com', 'static.deepseek.com', 'platform.deepseek.com'},
                  'meta': {'dev.meta.ai'}, 'zai': {'docs.z.ai', 'z.ai'}}

def official_evidence(service, url):
    parsed = urllib.parse.urlparse(url)
    if parsed.scheme != 'https' or parsed.hostname not in EVIDENCE_HOSTS.get(service, set()) or parsed.port not in (None, 443) or parsed.username or parsed.password:
        raise ValueError('Expected an explicitly allowed official billing/evidence host')

def billing_content(data):
    """Stable complete human-readable content; scripts/styles cannot establish absent fees."""
    class BillingMarkup(MetaMarkup):
        def handle_starttag(self, tag, attrs):
            super().handle_starttag(tag, [(key, value) for key, value in attrs if key != 'id'])
    markup = BillingMarkup(); markup.feed(data.decode('utf-8')); markup.close()
    def text(node):
        if isinstance(node, str): return ' '.join(node.split())
        if node['tag'] in ('script', 'style'): return ''
        return ' '.join(filter(None, (text(child) for child in node['children'])))
    content = text(markup.root)
    links = []
    def visit(node):
        if not isinstance(node, dict): return
        if node['tag'] == 'a':
            href = node['attrs'].get('href', '')
            if re.search(r'bill|payment|top.up|recharge|faq', href + ' ' + text(node), re.I): links.append(href)
        if node['tag'] not in ('script', 'style'):
            for child in node['children']: visit(child)
    visit(markup.root)
    return dict(text=content, sha256=hashlib.sha256(content.encode('utf-8')).hexdigest(), billingLinks=sorted(set(links)))

def unit_check(mapping, text, currency_evidence=None, token_unit_evidence=None):
    unit = mapping.get('tokenUnitQuote', '')
    if not unit or unit not in text: raise ValueError('Missing/changed exact source quote: tokenUnitQuote')
    token_unit = unit
    if token_unit_evidence is not None:
        official_evidence(mapping['service'], token_unit_evidence['sourceUrl'])
        token_unit = token_unit_evidence['quote']
    if not re.search(r'(?:1,?000,?000|1\s*M|million)\s+tokens', token_unit, re.I):
        raise ValueError('Source must explicitly establish USD per million tokens')
    if 'USD' not in unit:
        if not currency_evidence or not re.search(r'\bUSD\b|U\.S\. Dollars', currency_evidence.get('quote', '')):
            raise ValueError('Source must explicitly establish USD per million tokens')
        official_evidence(mapping['service'], currency_evidence['sourceUrl'])

def complete_list_evidence(mapping, review, capture):
    if not review or review.get('service') != mapping['service'] or review.get('sourceUrl') != mapping['sourceUrl'] or review.get('completeTokenTable') is not True:
        raise ValueError('Missing complete official token-price-list review' + (': ' + review['reason'] if review and review.get('reason') else ''))
    pages = review.get('pages', []); urls = {page['sourceUrl'] for page in pages}; receipts = []; contents = {}
    if mapping['sourceUrl'] not in urls or len(urls) != len(pages): raise ValueError('Incomplete/duplicate official pricing review pages')
    for page in pages:
        url = page['sourceUrl']; official_evidence(mapping['service'], url)
        data, receipt, failure = capture(mapping['service'], url)
        if failure: raise ValueError('Linked official billing capture unavailable: ' + failure)
        content = billing_content(data)
        if len(content['text']) < 30: raise ValueError('Official page has no public pricing/billing content: ' + url)
        if page.get('billingContentAvailable') is False:
            raise ValueError('Linked official page does not expose its billing content without login: ' + url)
        if content['sha256'] != page.get('contentSha256'): raise ValueError('Official pricing/billing content changed; review every fee again: ' + url)
        contents[url] = content
        receipts.append(dict(sourceUrl=url, retrievedAt=receipt['retrievedAt'], sourceSha256=receipt['sourceSha256'], reviewedContentSha256=content['sha256']))
    for url, content in contents.items():
        for href in content['billingLinks']:
            linked = urllib.parse.urljoin(url, href); parts = urllib.parse.urlsplit(linked)
            linked = urllib.parse.urlunsplit(parts._replace(fragment=''))
            if parts.hostname in EVIDENCE_HOSTS[mapping['service']] and linked not in urls:
                raise ValueError('Unreviewed linked official billing page: ' + linked)
    for charge in review.get('listedCharges', []):
        content = contents.get(charge['sourceUrl'])
        if content is None or not charge.get('quote') or charge['quote'] not in content['text']:
            raise ValueError('Missing/changed listed fee/minimum/surcharge quote')
        raise ValueError('Listed charge cannot use absent-fee evidence: ' + charge.get('reason', charge['quote']))
    return dict(kind='complete-official-price-list', statement='none listed on the official pricing page', requestUsd=0,
                sourceUrl=mapping['sourceUrl'], captures=receipts, listedConditionalCharges=review.get('listedCharges', []),
                scope='complete official token price list and every linked official billing page; no listed separate charge')

class Tables(html.parser.HTMLParser):
    def __init__(self):
        super().__init__(); self.tables = []; self.rows = None; self.row = None; self.cell = None; self.text = []; self.grid = {}
    def handle_starttag(self, tag, attrs):
        if tag == 'br' and self.cell is not None: self.cell.append(' ')
        if tag == 'table':
            if self.rows is not None: raise ValueError('Nested pricing tables require a new reviewed extractor')
            self.rows = []
            self.grid = {}
        elif tag == 'tr' and self.rows is not None: self.row = []
        elif tag in ('td', 'th') and self.row is not None:
            spans = dict(attrs)
            def span(name):
                value = spans.get(name, '1')
                if not value or not re.fullmatch(r'[1-9][0-9]*', value) or int(value) > 1000:
                    raise ValueError('Invalid or excessive pricing cell span')
                return int(value)
            self.colspan, self.rowspan = span('colspan'), span('rowspan')
            self.cell = []
    def handle_data(self, data):
        self.text.append(data)
        if self.cell is not None: self.cell.append(data)
    def handle_endtag(self, tag):
        if tag in ('td', 'th') and self.cell is not None:
            r = len(self.rows); c = 0
            while (r, c) in self.grid: c += 1
            value = ' '.join(''.join(self.cell).split())
            for y in range(r, r + self.rowspan):
                for x in range(c, c + self.colspan):
                    if (y, x) in self.grid: raise ValueError('Overlapping pricing cell spans')
                    self.grid[y, x] = value
                    if len(self.grid) > 10000: raise ValueError('Expanded pricing table exceeds capture limit')
            self.cell = None
        elif tag == 'tr' and self.row is not None:
            self.rows.append(self.row); self.row = None
        elif tag == 'table' and self.rows is not None:
            if any(r >= len(self.rows) for r, _ in self.grid): raise ValueError('Rowspan extends beyond pricing table')
            width = max((c + 1 for _, c in self.grid), default=0)
            if any((r, c) not in self.grid for r in range(len(self.rows)) for c in range(width)):
                raise ValueError('Incomplete pricing table grid')
            self.tables.append([[self.grid[r, c] for c in range(width)] for r in range(len(self.rows))]); self.rows = None

class MetaMarkup(html.parser.HTMLParser):
    """Read Meta's div layout and React HTML fragments. Never execute scripts/Flight data."""
    VOID = {'area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input', 'link', 'meta', 'param', 'source', 'track', 'wbr'}
    def __init__(self):
        super().__init__(); self.root = dict(tag='root', attrs={}, children=[]); self.stack = [self.root]; self.ids = {}
    def handle_starttag(self, tag, attrs):
        node = dict(tag=tag, attrs=dict(attrs), children=[]); self.stack[-1]['children'].append(node)
        identifier = node['attrs'].get('id')
        if identifier:
            if identifier in self.ids: raise ValueError('Duplicate Meta fragment identifier')
            self.ids[identifier] = node
        if tag not in self.VOID: self.stack.append(node)
    def handle_startendtag(self, tag, attrs):
        self.handle_starttag(tag, attrs)
        if tag not in self.VOID: self.handle_endtag(tag)
    def handle_endtag(self, tag):
        for index in range(len(self.stack) - 1, 0, -1):
            if self.stack[index]['tag'] == tag:
                del self.stack[index:]; break
    def handle_data(self, data): self.stack[-1]['children'].append(data)

def parse_meta(data):
    markup = MetaMarkup(); markup.feed(data.decode('utf-8')); markup.close()
    replacements = {}
    def nodes(node):
        if isinstance(node, dict):
            yield node
            for child in node['children']: yield from nodes(child)
    for node in nodes(markup.root):
        if node['tag'] == 'script':
            script = ''.join(x for x in node['children'] if isinstance(x, str))
            for source, target in re.findall(r'\$RS\("(S:[0-9a-f]+)","(P:[0-9a-f]+)"\)', script):
                if target in replacements or source not in markup.ids or target not in markup.ids:
                    raise ValueError('Missing or duplicate Meta streamed fragment')
                replacements[target] = markup.ids[source]
    def texts(node, visited=()):
        if isinstance(node, str):
            value = ' '.join(node.split())
            return [value] if value else []
        if node['tag'] in ('script', 'style'): return []
        identifier = node['attrs'].get('id')
        if identifier in replacements:
            if identifier in visited: raise ValueError('Cyclic Meta streamed fragment')
            return texts(replacements[identifier], (*visited, identifier))
        return [value for child in node['children'] for value in texts(child, visited)]
    result = Tables(); result.text = texts(markup.root)
    header = ['Model', 'Context window', 'Input (Mtok)', 'Cached input (Mtok)', 'Output (Mtok)']
    for node in nodes(markup.root):
        if 'data-marketing-scroller' not in node['attrs']: continue
        values = texts(node)
        if not any(re.fullmatch(r'muse-spark-[a-z0-9.-]+', value) for value in values): continue
        if values[:5] != header: raise ValueError('Meta pricing header changed')
        rows = [header]; remaining = values[5:]
        if len(remaining) % 6: raise ValueError('Incomplete Meta pricing row')
        for start in range(0, len(remaining), 6):
            model, description, context, input_rate, cached_rate, output_rate = remaining[start:start + 6]
            if not re.fullmatch(r'muse-spark-[a-z0-9.-]+', model) or description not in ('Used to improve our products.', 'Not used to improve our products.'):
                raise ValueError('Meta pricing row layout changed')
            rows.append([model, context, input_rate, cached_rate, output_rate])
        result.tables.append(rows)
    if len(result.tables) > 1: raise ValueError('Ambiguous Meta text pricing blocks')
    return result

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

def extract(mapping, data, retrieved, identity=None, request_evidence=None, currency_evidence=None, token_unit_evidence=None):
    official(mapping['service'], mapping['sourceUrl'])
    parsed = parse_meta(data) if mapping.get('parser') == 'meta-marketing' and mapping['service'] == 'meta' else parse(data)
    text = ' '.join(' '.join(parsed.text).split())
    def quote(name):
        value = mapping.get(name)
        if not value or value not in text: raise ValueError('Missing/changed exact source quote: ' + name)
        return value
    unit_check(mapping, text, currency_evidence, token_unit_evidence)
    def cell(name, optional=False):
        coords = mapping.get(name)
        if coords is None and optional: return None
        if not isinstance(coords, list) or len(coords) != 3 or any(type(x) is not int or x < 0 for x in coords): raise ValueError('Invalid cell mapping: ' + name)
        try: return parsed.tables[coords[0]][coords[1]][coords[2]]
        except IndexError as error: raise ValueError('Source layout changed: ' + name) from error
    expected = mapping['model']
    if identity is not None:
        if any(identity.get(key) != mapping[key] for key in ('service', 'model', 'sourceUrl')):
            raise ValueError('Reviewed identity does not match model/service/source exactly')
        expected = identity['displayText']
    if cell('modelCell') != expected: raise ValueError('Model identifier did not match its cited cell exactly')
    for check in mapping.get('checks', []):
        if cell_coords(parsed, check['cell']) != check['text']: raise ValueError('Reviewed pricing header/condition cell changed')
    conditions = mapping.get('billingConditionQuotes', [])
    for condition in conditions:
        if not condition or condition not in text: raise ValueError('Missing/changed exact billing condition quote')
    def money(name, optional=False, coords=None):
        value = cell_coords(parsed, coords) if coords is not None else cell(name, optional)
        if value is None: return None
        match = re.fullmatch(r'\s*\$?\s*(\d+(?:\.\d+)?)\s*', value)
        if not match: raise ValueError('Ambiguous or non-USD numeric price cell: ' + name)
        amount = float(decimal.Decimal(match[1]))
        if not math.isfinite(amount): raise ValueError('Nonfinite source price: ' + name)
        return amount
    def highest(name, optional=False):
        value = money(name, optional)
        alternatives = mapping.get(name.replace('Cell', 'Alternatives'), [])
        if alternatives and not conditions: raise ValueError('Conditional prices require exact billing condition quotes')
        values = [] if value is None else [value]
        for coords in alternatives:
            values.append(money(name, coords=coords))
        return max(values) if values else None
    def tokens(name):
        value = cell(name, True)
        if value is None: return None
        if not re.fullmatch(r'[1-9][0-9,]*', value): raise ValueError('Token limits require exact integer counts, not guessed K/M multipliers')
        return int(value.replace(',', ''))
    request = highest('requestCell', True)
    if request is None:
        if request_evidence is not None:
            if request_evidence.get('kind') != 'complete-official-price-list' or request_evidence.get('statement') != 'none listed on the official pricing page' or request_evidence.get('requestUsd') != 0:
                raise ValueError('Invalid complete-list request-fee evidence')
            if not any(receipt.get('sourceUrl') == mapping['sourceUrl'] and receipt.get('sourceSha256') == hashlib.sha256(data).hexdigest() and receipt.get('retrievedAt') == retrieved for receipt in request_evidence.get('captures', [])):
                raise ValueError('Request-fee evidence does not bind the exact pricing capture/date/hash')
            request = 0
        else:
            evidence = quote('tokenOnlyQuote')
            if not re.search(r'no (?:per-request|request) fees', evidence, re.I): raise ValueError('No request-fee default without explicit source evidence')
            request = 0
    evidence = dict(mapping)
    if identity is not None: evidence['identity'] = identity
    if request_evidence is not None: evidence['requestFeeEvidence'] = request_evidence
    if currency_evidence is not None: evidence['currencyEvidence'] = currency_evidence
    if token_unit_evidence is not None: evidence['tokenUnitEvidence'] = token_unit_evidence
    return dict(service=mapping['service'], model=mapping['model'], inputUsdPerMillion=highest('inputCell'), outputUsdPerMillion=highest('outputCell'),
                cachedInputUsdPerMillion=highest('cachedInputCell', True), requestUsd=request, maximumInputTokens=tokens('maximumInputCell'),
                maximumOutputTokens=tokens('maximumOutputCell'), retrievedAt=retrieved, sourceUrl=mapping['sourceUrl'], sourceSha256=hashlib.sha256(data).hexdigest(),
                evidence=evidence)

def cell_coords(parsed, coords):
    if not isinstance(coords, list) or len(coords) != 3 or any(type(x) is not int or x < 0 for x in coords): raise ValueError('Invalid reviewed cell coordinates')
    try: return parsed.tables[coords[0]][coords[1]][coords[2]]
    except IndexError as error: raise ValueError('Source layout changed at reviewed cell') from error

def observed_cells(mapping, identity, parsed):
    """Audit raw source cells, including the maximum numeric cell. These are not published prices."""
    if any(identity.get(key) != mapping[key] for key in ('service', 'model', 'sourceUrl')) or cell_coords(parsed, mapping['modelCell']) != identity['displayText']:
        raise ValueError('Reviewed identity does not match its exact cited source cell')
    for check in mapping.get('checks', []):
        if cell_coords(parsed, check['cell']) != check['text']: raise ValueError('Reviewed pricing header/condition cell changed')
    text = ' '.join(' '.join(parsed.text).split())
    conditions = mapping.get('billingConditionQuotes', [])
    if any(not quote or quote not in text for quote in conditions): raise ValueError('Missing/changed exact billing condition quote')
    observations = {}
    for name in ('inputCell', 'outputCell', 'cachedInputCell', 'requestCell', 'storageCell'):
        coords = mapping.get(name)
        if coords is None: continue
        alternatives = mapping.get(name.replace('Cell', 'Alternatives'), [])
        if alternatives and not conditions: raise ValueError('Conditional prices require exact billing condition quotes')
        cells = [dict(cell=position, text=cell_coords(parsed, position)) for position in [coords, *alternatives]]
        numeric = []
        for cell in cells:
            match = re.fullmatch(r'\s*\$?\s*(\d+(?:\.\d+)?)\s*', cell['text'])
            if match:
                amount = decimal.Decimal(match[1])
                if not math.isfinite(float(amount)): raise ValueError('Nonfinite source price')
                numeric.append((amount, cell['cell']))
        observations[name] = dict(cells=cells, highestCell=max(numeric, key=lambda pair: pair[0])[1] if len(numeric) == len(cells) else None)
    return observations

def write_outputs(prices, json_path, cs_path, manifest=None, manifest_path=None):
    document = json.dumps(dict(schemaVersion=1, state='filled' if prices else 'unfilled', prices=prices), ensure_ascii=True, indent=2, allow_nan=False) + '\n'
    cs = '// PORT-ONLY: generated from official pricing pages by generate-model-prices.py; source dates/hashes are in the JSON.\nnamespace Marrow.Services.Providers;\n\ninternal static class ModelPriceData\n{\n    internal const string Json = ' + json.dumps(document) + ';\n}\n'
    outputs = [(json_path, document), (cs_path, cs)]
    if manifest_path is not None: outputs.append((manifest_path, json.dumps(manifest, ensure_ascii=True, indent=2, allow_nan=False) + '\n'))
    pending_files = []
    for path, content in outputs:
        path = pathlib.Path(path); pending = path.with_suffix(path.suffix + '.tmp')
        pending.write_text(content, encoding='utf-8', newline='\n'); pending_files.append((pending, path))
    for pending, path in pending_files: pending.replace(path)

def build_reviewed(document, fetcher, json_path, cs_path, manifest_path):
    mappings = document['models']; identities = {}; cache = {}; sources = []; models = []; prices = []
    if not mappings: raise ValueError('Price table is UNFILLED: supply reviewed exact model mappings')
    for identity in document['identities']:
        official(identity['service'], identity['sourceUrl']); key = (identity['service'], identity['model'])
        if key in identities: raise ValueError('Duplicate reviewed model identity')
        if not identity.get('displayText'): raise ValueError('Empty reviewed model display text')
        identities[key] = identity
    keys = [(mapping['service'], mapping['model']) for mapping in mappings]
    if len(set(keys)) != len(keys) or set(keys) != set(identities): raise ValueError('Model mappings and reviewed identities must bind exactly once')
    for source in document.get('sources', []): official_evidence(source['service'], source['sourceUrl'])
    def capture(service, url):
        official_evidence(service, url)
        if url not in cache:
            receipt = dict(service=service, sourceUrl=url, retrievedAt=datetime.datetime.now(datetime.timezone.utc).isoformat().replace('+00:00', 'Z'))
            try:
                data = fetcher(url); receipt.update(status='fetched', byteLength=len(data), sourceSha256=hashlib.sha256(data).hexdigest())
                text = billing_content(data)['text']
                receipt['explicitNoRequestFeeQuotes'] = re.findall(r'[^.]*\bno (?:per-request|request) fees[^.]*\.?', text, re.I)
                receipt['billingQuotes'] = []
                for source in document.get('sources', []):
                    if source['sourceUrl'] != url: continue
                    for quote in source.get('billingQuotes', []):
                        if not quote or quote not in text: raise ValueError('Missing/changed supplemental exact billing quote')
                        receipt['billingQuotes'].append(quote)
                cache[url] = (data, receipt, None)
            except (ValueError, OSError) as error:
                receipt.update(status='unavailable', reason=str(error)); cache[url] = (None, receipt, str(error))
            sources.append(receipt)
        return cache[url]
    for mapping in mappings:
        identity = identities[mapping['service'], mapping['model']]
        data, receipt, failure = capture(mapping['service'], mapping['sourceUrl'])
        row = dict(service=mapping['service'], model=mapping['model'], identity=identity, status='unfilled', reasons=[],
                   sourceUrl=mapping['sourceUrl'], retrievedAt=receipt['retrievedAt'], sourceSha256=receipt.get('sourceSha256'),
                   billingConditionQuotes=mapping.get('billingConditionQuotes', []), selectionPolicy='highest applicable cited numeric cell; never average')
        if failure: row['reasons'].append(failure)
        else:
            try:
                parsed = parse_meta(data) if mapping.get('parser') == 'meta-marketing' else parse(data)
                row['observedTokenCells'] = observed_cells(mapping, identity, parsed)
                text = ' '.join(' '.join(parsed.text).split())
                currency = None; token_unit = None; request_evidence = None
                for name in ('currencyEvidence', 'tokenUnitEvidence'):
                    if not mapping.get(name): continue
                    spec = mapping[name]; source, evidence_receipt, error = capture(mapping['service'], spec['sourceUrl'])
                    if error or not spec.get('quote') or spec['quote'] not in billing_content(source)['text']: raise ValueError('Missing/changed exact official quote: ' + name)
                    evidence = dict(spec, retrievedAt=evidence_receipt['retrievedAt'], sourceSha256=evidence_receipt['sourceSha256'])
                    row[name] = evidence
                    if name == 'currencyEvidence': currency = evidence
                    else: token_unit = evidence
                try: unit_check(mapping, text, currency, token_unit)
                except ValueError as error: row['reasons'].append(str(error))
                fee = mapping.get('tokenOnlyQuote', '')
                if mapping.get('requestCell') is None:
                    if mapping.get('completeListReview'):
                        review = next((review for review in document.get('completeListReviews', []) if review['id'] == mapping['completeListReview']), None)
                        row['listedCharges'] = review.get('listedCharges', []) if review else []
                        try:
                            request_evidence = complete_list_evidence(mapping, review, capture)
                            row['requestFeeEvidence'] = request_evidence
                        except ValueError as error: row['reasons'].append(str(error))
                    elif not fee or fee not in text or not re.search(r'no (?:per-request|request) fees', fee, re.I):
                        row['reasons'].append('Missing/changed exact source quote: tokenOnlyQuote (no request fees); no per-request rate quoted')
                if mapping.get('storageCell') is not None:
                    storage = cell_coords(parsed, mapping['storageCell'])
                    row['storageCondition'] = storage
                    if storage not in ('Free', 'Limited-time Free', '-'):
                        row['reasons'].append('Cache storage charge cannot be represented by the flat token/request catalog')
                if not row['reasons']:
                    price = extract(mapping, data, receipt['retrievedAt'], identity, request_evidence, currency, token_unit)
                    prices.append(price); row['status'] = 'filled'
            except ValueError as error:
                row['reasons'].append(str(error))
        models.append(row)
    for source in document.get('sources', []): capture(source['service'], source['sourceUrl'])
    manifest = dict(schemaVersion=2, filledCount=len(prices), unfilledCount=len(models) - len(prices), sources=sources, models=models,
                    note='Observed raw cells are audit evidence only. Only prices passing every original quote/numeric/bounds guard enter model-prices.json.')
    write_outputs(prices, json_path, cs_path, manifest, manifest_path)
    return manifest

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
    def capturing_fetch(url):
        data = fetch(url)
        if args.captures is not None:
            args.captures.mkdir(parents=True, exist_ok=True)
            (args.captures / (hashlib.sha256(data).hexdigest() + '.html')).write_bytes(data)
        return data
    if args.inspect:
        if args.captures is None: cli.error('--inspect requires --captures (outside the repository is preferred)')
        args.captures.mkdir(parents=True, exist_ok=True)
        for source in mapping['sources']:
            official_evidence(source['service'], source['sourceUrl']); data = capturing_fetch(source['sourceUrl']); digest = hashlib.sha256(data).hexdigest()
            (args.captures / (digest + '.html')).write_bytes(data)
            parsed = parse_meta(data) if source.get('parser') == 'meta-marketing' else parse(data)
            (args.captures / (digest + '.tables.json')).write_text(json.dumps(dict(source=source, retrievedAt=datetime.datetime.now(datetime.timezone.utc).isoformat(), sha256=digest, tables=parsed.tables), indent=2), encoding='utf-8')
        print('Official captures saved. Review exact model IDs, units, billing conditions and limits before mapping cells; no data was published.')
    else:
        pricing = ROOT / 'src/Marrow.Services/Providers/Pricing'
        if mapping.get('identityMap'):
            identity_path = args.map.parent / mapping['identityMap']
            mapping['identities'] = json.loads(identity_path.read_text(encoding='utf-8'))['models']
            manifest = build_reviewed(mapping, capturing_fetch, pricing / 'model-prices.json', ROOT / 'src/Marrow.Services/Providers/ModelPriceData.cs', pricing / 'model-price-fetch-manifest.json')
            print('Generated', manifest['filledCount'], 'filled and', manifest['unfilledCount'], 'unfilled model records. Reasons and highest-rate selections are in the manifest.')
        else:
            prices = build(mapping['models'], fetch, pricing / 'model-prices.json', ROOT / 'src/Marrow.Services/Providers/ModelPriceData.cs')
            print('Generated', len(prices), 'dated model prices. Review/commit both JSON and C#; missing bounds remain unavailable to capped requests.')

if __name__ == '__main__': main()
