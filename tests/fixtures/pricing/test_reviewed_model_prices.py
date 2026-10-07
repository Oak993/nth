"""Capture regressions use verbatim official HTML; synthetic guard cases use fictional prices."""
import copy
import hashlib
import importlib.util
import json
import pathlib
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[3]
CAPTURES = pathlib.Path(__file__).parent / 'captures'
spec = importlib.util.spec_from_file_location('reviewed_prices', ROOT / 'porting/tools/generate-model-prices.py')
generator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(generator)


class ReviewedGeneratorTests(unittest.TestCase):
    def test_capture_provenance(self):
        for source in json.loads((CAPTURES / 'manifest.json').read_text()):
            data = (CAPTURES / source['file']).read_bytes()
            self.assertEqual(source['sourceSha256'], hashlib.sha256(data).hexdigest())
            generator.official(source['service'], source['sourceUrl'])

    def test_deepseek_spans_expand_into_an_exact_rectangular_grid(self):
        table = generator.parse((CAPTURES / 'deepseek.html').read_bytes()).tables[0]
        self.assertTrue(all(len(row) == 5 for row in table))
        self.assertEqual(['MODEL', 'MODEL', 'MODEL', 'deepseek-flash(1)', 'deepseek-v4-pro'], table[0])
        self.assertEqual(['PRICING(2)', '1M INPUT TOKENS (CACHE HIT)', 'OFF-PEAK', '$0.003', '$0.022'], table[14])
        self.assertEqual(['PRICING(2)', '1M INPUT TOKENS (CACHE HIT)', 'PEAK', '$0.006', '$0.044'], table[15])

    def test_meta_streamed_non_table_prices_and_empty_shell(self):
        rows = generator.parse_meta((CAPTURES / 'meta-product.html').read_bytes()).tables[0]
        self.assertEqual(['Model', 'Context window', 'Input (Mtok)', 'Cached input (Mtok)', 'Output (Mtok)'], rows[0])
        self.assertEqual(['muse-spark-1.3-contributor', '1M', '$0.10', '$0.002', '$0.20'], rows[1])
        self.assertEqual(['muse-spark-1.3', '1M', '$1.25', '$0.15', '$4.25'], rows[2])
        self.assertEqual(6, len(rows))
        self.assertEqual([], generator.parse_meta((CAPTURES / 'meta-shell.html').read_bytes()).tables)
        page = (CAPTURES / 'meta-product.html').read_bytes()
        with self.assertRaises(ValueError): generator.parse_meta(page.replace(b'Input (Mtok)', b'Changed input header'))
        with self.assertRaises(ValueError): generator.parse_meta(page.replace(b'>$0.10<', b'><'))

    def test_invalid_spans_and_nested_tables_remain_rejected(self):
        for html in [b'<table><tr><td colspan="0">x</td></tr></table>',
                     b'<table><tr><td rowspan="2">x</td></tr></table>',
                     b'<table><tr><td rowspan="1000000">x</td></tr></table>',
                     b'<table><tr><td><table></table></td></tr></table>']:
            with self.subTest(html=html), self.assertRaises(ValueError): generator.parse(html)

    def fixture(self):
        page = b'<p>USD per million tokens. No request fees. Peak and off-peak rates.</p><table><tr><td>Fixture-ID</td><td>2</td><td>5</td><td>4</td><td>8</td><td>1</td><td>3</td></tr></table>'
        mapping = dict(service='zai', model='fixture-id', sourceUrl='https://docs.z.ai/guides/overview/pricing',
                       modelCell=[0, 0, 0], inputCell=[0, 0, 1], outputCell=[0, 0, 3], cachedInputCell=[0, 0, 5],
                       inputAlternatives=[[0, 0, 2]], outputAlternatives=[[0, 0, 4]], cachedInputAlternatives=[[0, 0, 6]],
                       billingConditionQuotes=['Peak and off-peak rates.'], tokenUnitQuote='USD per million tokens',
                       tokenOnlyQuote='No request fees.')
        identity = dict(service='zai', model='fixture-id', displayText='Fixture-ID', sourceUrl=mapping['sourceUrl'])
        return page, mapping, identity

    def test_exact_reviewed_identity_and_highest_rates(self):
        page, mapping, identity = self.fixture()
        row = generator.extract(mapping, page, '2026-10-07T00:00:00Z', identity)
        self.assertEqual((5, 8, 3), (row['inputUsdPerMillion'], row['outputUsdPerMillion'], row['cachedInputUsdPerMillion']))
        self.assertEqual(identity, row['evidence']['identity'])
        for change in [dict(displayText='fixture-id'), dict(model='different'), dict(sourceUrl='https://docs.z.ai/other')]:
            with self.assertRaises(ValueError): generator.extract(mapping, page, '2026-10-07T00:00:00Z', dict(identity, **change))
        with self.assertRaises(ValueError): generator.extract(dict(mapping, billingConditionQuotes=[]), page, '2026-10-07T00:00:00Z', identity)
        with self.assertRaises(ValueError): generator.extract(mapping, page.replace(b'<td>5</td>', b'<td>5 to 6</td>'), '2026-10-07T00:00:00Z', identity)

    def test_only_affected_model_unfilled_and_quote_guard_unchanged(self):
        page, mapping, identity = self.fixture()
        second = dict(mapping, model='another-id', tokenOnlyQuote='')
        document = dict(models=[mapping, second], identities=[identity, dict(identity, model='another-id')])
        with tempfile.TemporaryDirectory() as directory:
            paths = [pathlib.Path(directory) / name for name in ('prices.json', 'prices.cs', 'manifest.json')]
            manifest = generator.build_reviewed(document, lambda _: page, *paths)
            self.assertEqual((1, 1), (manifest['filledCount'], manifest['unfilledCount']))
            self.assertIn('tokenOnlyQuote', ' '.join(manifest['models'][1]['reasons']))
            self.assertEqual(['fixture-id'], [row['model'] for row in json.loads(paths[0].read_text())['prices']])
        for phrase in [b'Billed by tokens.', b'No minimums.', b'No upfront commitment.']:
            altered = page.replace(b'No request fees.', phrase)
            with self.assertRaises(ValueError): generator.extract(dict(mapping, tokenOnlyQuote=phrase.decode()), altered, '2026-10-07T00:00:00Z', identity)

    def test_duplicate_identity_cannot_replace_data(self):
        page, mapping, identity = self.fixture()
        document = dict(models=[mapping], identities=[identity, copy.deepcopy(identity)])
        with tempfile.TemporaryDirectory() as directory:
            paths = [pathlib.Path(directory) / name for name in ('prices.json', 'prices.cs', 'manifest.json')]
            for path in paths: path.write_bytes(b'original')
            with self.assertRaises(ValueError): generator.build_reviewed(document, lambda _: page, *paths)
            self.assertTrue(all(path.read_bytes() == b'original' for path in paths))

    def test_one_failed_source_does_not_discard_an_independent_filled_model(self):
        page, mapping, identity = self.fixture()
        unavailable = dict(mapping, model='unavailable-id', sourceUrl='https://docs.z.ai/unavailable')
        document = dict(models=[mapping, unavailable], identities=[identity, dict(identity, model='unavailable-id', sourceUrl=unavailable['sourceUrl'])])
        def fetcher(url):
            if url == unavailable['sourceUrl']: raise OSError('fixture HTTP failure')
            return page
        with tempfile.TemporaryDirectory() as directory:
            paths = [pathlib.Path(directory) / name for name in ('prices.json', 'prices.cs', 'manifest.json')]
            manifest = generator.build_reviewed(document, fetcher, *paths)
            self.assertEqual((1, 1), (manifest['filledCount'], manifest['unfilledCount']))
            self.assertEqual(['fixture HTTP failure'], manifest['models'][1]['reasons'])

    def test_paid_storage_or_changed_header_cannot_publish_a_flat_price(self):
        page, mapping, identity = self.fixture()
        for unsafe in [dict(mapping, storageCell=[0, 0, 1]), dict(mapping, checks=[dict(cell=[0, 0, 0], text='Changed')])]:
            document = dict(models=[unsafe], identities=[identity])
            with tempfile.TemporaryDirectory() as directory:
                paths = [pathlib.Path(directory) / name for name in ('prices.json', 'prices.cs', 'manifest.json')]
                manifest = generator.build_reviewed(document, lambda _: page, *paths)
                self.assertEqual((0, 1), (manifest['filledCount'], manifest['unfilledCount']))
                self.assertEqual([], json.loads(paths[0].read_text())['prices'])

    def test_capture_mappings_select_peak_columns_and_all_known_exact_ids(self):
        pricing = ROOT / 'src/Marrow.Services/Providers/Pricing'
        document = json.loads((pricing / 'model-price-extraction.json').read_text())
        identities = {(row['service'], row['model']): row for row in json.loads((pricing / document['identityMap']).read_text())['models']}
        self.assertEqual(9, len(identities))
        for mapping in document['models']:
            parser = generator.parse_meta if mapping.get('parser') == 'meta-marketing' else generator.parse
            capture = 'meta-product' if mapping['service'] == 'meta' else mapping['service']
            parsed = parser((CAPTURES / (capture + '.html')).read_bytes())
            observed = generator.observed_cells(mapping, identities[mapping['service'], mapping['model']], parsed)
            if mapping['service'] == 'deepseek':
                self.assertEqual(17, observed['inputCell']['highestCell'][1])
                self.assertEqual(19, observed['outputCell']['highestCell'][1])
                self.assertEqual(15, observed['cachedInputCell']['highestCell'][1])


if __name__ == '__main__': unittest.main()
