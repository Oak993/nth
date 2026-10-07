# Fake HTML only. None of these numbers is a provider price; no network/CLI/model is used.
import importlib.util
import pathlib
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location("generate_prices", ROOT / "porting/tools/generate-model-prices.py")
generator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(generator)

class GeneratorTests(unittest.TestCase):
    def test_cells_are_source_bound_and_cache_rate_keeps_its_own_column(self):
        page = b'<p>USD per 1,000,000 tokens. No per-request fees.</p><table><tr><th>Model</th><th>Input</th><th>Cached</th><th>Output</th><th>Context</th><th>Output limit</th></tr><tr><td>fixture</td><td>$2.00</td><td>$0.50</td><td>$4.00</td><td>1000</td><td>200</td></tr></table>'
        mapping = dict(service="deepseek", model="fixture", sourceUrl="https://api-docs.deepseek.com/quick_start/pricing", modelCell=[0,1,0], inputCell=[0,1,1], cachedInputCell=[0,1,2], outputCell=[0,1,3], maximumInputCell=[0,1,4], maximumOutputCell=[0,1,5], tokenUnitQuote="USD per 1,000,000 tokens", tokenOnlyQuote="No per-request fees.")
        row = generator.extract(mapping, page, "2026-10-07T00:00:00Z")
        self.assertEqual((2, 0.5, 4, 0, 1000, 200), (row['inputUsdPerMillion'], row['cachedInputUsdPerMillion'], row['outputUsdPerMillion'], row['requestUsd'], row['maximumInputTokens'], row['maximumOutputTokens']))
        self.assertEqual(64, len(row['sourceSha256']))
        self.assertEqual(mapping['sourceUrl'], row['sourceUrl'])
        with self.assertRaises(ValueError): generator.extract(dict(mapping, model="other"), page, "2026-10-07T00:00:00Z")
        with self.assertRaises(ValueError): generator.extract(dict(mapping, sourceUrl="https://untrusted.example/pricing"), page, "2026-10-07T00:00:00Z")
        with self.assertRaises(ValueError): generator.extract(mapping, page.replace(b'$2.00', b'$2.00 to $3.00'), "2026-10-07T00:00:00Z")

    def test_an_unfilled_map_or_failed_fetch_does_not_publish_or_replace_existing_data(self):
        with tempfile.TemporaryDirectory() as tmp:
            json_path = pathlib.Path(tmp) / 'prices.json'; cs_path = pathlib.Path(tmp) / 'data.cs'
            json_path.write_bytes(b'original-json'); cs_path.write_bytes(b'original-cs')
            with self.assertRaises(ValueError): generator.build([], lambda _: self.fail('must not fetch'), json_path, cs_path)
            self.assertEqual(b'original-json', json_path.read_bytes()); self.assertEqual(b'original-cs', cs_path.read_bytes())

    def test_a_nonfinite_source_rate_cannot_publish_an_invalid_price_table(self):
        page = ('<p>USD per million tokens. No request fees.</p><table><tr><td>fixture</td><td>' + '9' * 400 + '</td><td>4</td></tr></table>').encode()
        mapping = dict(service='zai', model='fixture', sourceUrl='https://docs.z.ai/guides/overview/pricing', modelCell=[0,0,0], inputCell=[0,0,1], outputCell=[0,0,2], tokenUnitQuote='USD per million tokens', tokenOnlyQuote='No request fees.')
        with tempfile.TemporaryDirectory() as tmp:
            json_path = pathlib.Path(tmp) / 'prices.json'; cs_path = pathlib.Path(tmp) / 'data.cs'
            json_path.write_bytes(b'original-json'); cs_path.write_bytes(b'original-cs')
            with self.assertRaises(ValueError): generator.build([mapping], lambda _: page, json_path, cs_path)
            self.assertEqual(b'original-json', json_path.read_bytes()); self.assertEqual(b'original-cs', cs_path.read_bytes())

if __name__ == '__main__': unittest.main()
