"""Fictional prices exercise the lead's complete-list request-fee evidence rule."""
import copy
import hashlib
import importlib.util
import pathlib
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location('complete_prices', ROOT / 'porting/tools/generate-model-prices.py')
generator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(generator)


class CompleteListTests(unittest.TestCase):
    URL = 'https://docs.z.ai/guides/overview/pricing'
    BILLING = 'https://docs.z.ai/help/billing'
    PAGE = b'<p>USD per million tokens.</p><table><tr><td>fixture</td><td>2</td><td>4</td></tr></table><a href="/help/billing">Billing</a>'
    BILL = b'<h1>Billing</h1><p>You are billed for input and output tokens.</p>'

    def document(self):
        mapping = dict(service='zai', model='fixture', sourceUrl=self.URL, modelCell=[0, 0, 0], inputCell=[0, 0, 1], outputCell=[0, 0, 2], tokenUnitQuote='USD per million tokens', completeListReview='fixture-review')
        identity = dict(service='zai', model='fixture', displayText='fixture', sourceUrl=self.URL)
        review = dict(id='fixture-review', service='zai', sourceUrl=self.URL, completeTokenTable=True, pages=[dict(sourceUrl=url, contentSha256=generator.billing_content(data)['sha256']) for url, data in [(self.URL, self.PAGE), (self.BILLING, self.BILL)]], listedCharges=[])
        return dict(models=[mapping], identities=[identity], completeListReviews=[review])

    def run_build(self, document, fetcher):
        with tempfile.TemporaryDirectory() as directory:
            paths = [pathlib.Path(directory) / name for name in ('prices.json', 'data.cs', 'manifest.json')]
            return generator.build_reviewed(document, fetcher, *paths)

    def test_complete_official_list_records_sourced_zero_with_each_capture_receipt(self):
        manifest = self.run_build(self.document(), lambda url: self.PAGE if url == self.URL else self.BILL)
        self.assertEqual((1, 0), (manifest['filledCount'], manifest['unfilledCount']))
        evidence = manifest['models'][0]['requestFeeEvidence']
        self.assertEqual('none listed on the official pricing page', evidence['statement'])
        self.assertEqual(0, evidence['requestUsd'])
        self.assertEqual(2, len(evidence['captures']))
        self.assertEqual(hashlib.sha256(self.PAGE).hexdigest(), evidence['captures'][0]['sourceSha256'])
        self.assertIn('retrievedAt', evidence['captures'][1])

    def test_new_fee_minimum_or_surcharge_invalidates_review_before_zero_is_used(self):
        for extra in [b'<p>Request fee $1 per request.</p>', b'<p>Minimum charge $2.</p>', b'<p>A surcharge may apply.</p>']:
            with self.subTest(extra=extra):
                manifest = self.run_build(self.document(), lambda url: self.PAGE if url == self.URL else self.BILL + extra)
                self.assertEqual(0, manifest['filledCount'])
                self.assertIn('changed', ' '.join(manifest['models'][0]['reasons']).lower())

    def test_unreviewed_linked_billing_or_missing_capture_cannot_claim_no_fee(self):
        for change in ['omitted', 'unavailable', 'wrong-host', 'not-complete']:
            document = self.document()
            if change == 'omitted': document['completeListReviews'][0]['pages'].pop()
            if change == 'wrong-host': document['completeListReviews'][0]['pages'][1]['sourceUrl'] = 'https://untrusted.example/billing'
            if change == 'not-complete': document['completeListReviews'][0]['completeTokenTable'] = False
            def fetcher(url):
                if change == 'unavailable' and url == self.BILLING: raise OSError('billing unavailable')
                return self.PAGE if url == self.URL else self.BILL
            manifest = self.run_build(document, fetcher)
            self.assertEqual(0, manifest['filledCount'])

    def test_an_unclear_listed_charge_leaves_model_unfilled(self):
        document = self.document()
        document['completeListReviews'][0]['listedCharges'] = [dict(quote='A surcharge may apply.', sourceUrl=self.BILLING, disposition='unfilled', reason='Value is unspecified')]
        manifest = self.run_build(document, lambda url: self.PAGE if url == self.URL else self.BILL)
        self.assertEqual(0, manifest['filledCount'])

    def test_unit_currency_can_be_proved_by_separate_exact_official_quotes(self):
        data = self.PAGE.replace(b'USD per million tokens.', b'Prices per million tokens.')
        document = self.document(); document['models'][0]['tokenUnitQuote'] = 'Prices per million tokens.'
        document['models'][0]['currencyEvidence'] = dict(sourceUrl=self.BILLING, quote='All amounts are in U.S. Dollars.')
        billing = self.BILL + b'<p>All amounts are in U.S. Dollars.</p>'
        for page, body in zip(document['completeListReviews'][0]['pages'], [data, billing]): page['contentSha256'] = generator.billing_content(body)['sha256']
        manifest = self.run_build(document, lambda url: data if url == self.URL else billing)
        self.assertEqual(1, manifest['filledCount'])
        bad = copy.deepcopy(document); bad['models'][0]['currencyEvidence']['quote'] = 'Dollar sign means USD'
        manifest = self.run_build(bad, lambda url: data if url == self.URL else billing)
        self.assertEqual(0, manifest['filledCount'])

    def test_a_supplemental_official_token_unit_is_cited_without_inventing_a_quote(self):
        data = self.PAGE.replace(b'USD per million tokens.', b'USD token pricing.')
        document = self.document(); document['models'][0]['tokenUnitQuote'] = 'USD token pricing.'
        document['models'][0]['tokenUnitEvidence'] = dict(sourceUrl=self.BILLING, quote='Price per 1M tokens')
        billing = self.BILL + b'<p>Price per 1M tokens</p>'
        for page, body in zip(document['completeListReviews'][0]['pages'], [data, billing]): page['contentSha256'] = generator.billing_content(body)['sha256']
        manifest = self.run_build(document, lambda url: data if url == self.URL else billing)
        self.assertEqual(1, manifest['filledCount'])

    def test_generic_billing_markup_does_not_require_unique_meta_fragment_ids(self):
        content = generator.billing_content(b'<p id="duplicate">USD pricing.</p><p id="duplicate">No minimums.</p><script>request fee ignored script text</script>')
        self.assertEqual('USD pricing. No minimums.', content['text'])

    def test_a_login_shell_is_not_a_complete_linked_billing_review(self):
        document = self.document(); billing = b'<html><body>Login</body></html>'
        document['completeListReviews'][0]['pages'][1]['contentSha256'] = generator.billing_content(billing)['sha256']
        manifest = self.run_build(document, lambda url: self.PAGE if url == self.URL else billing)
        self.assertEqual(0, manifest['filledCount'])

    def test_a_listed_fee_cannot_be_ignored_to_claim_none_listed(self):
        document = self.document(); billing = self.BILL + b'<p>Search surcharge $1 per use.</p>'
        document['completeListReviews'][0]['pages'][1]['contentSha256'] = generator.billing_content(billing)['sha256']
        document['completeListReviews'][0]['listedCharges'] = [dict(sourceUrl=self.BILLING, quote='Search surcharge $1 per use.', disposition='not-applicable-to-text-function-requests', applicabilityEvidence='A reviewer marked it optional')]
        manifest = self.run_build(document, lambda url: self.PAGE if url == self.URL else billing)
        self.assertEqual(0, manifest['filledCount'])

    def test_official_billing_capture_provenance(self):
        import json
        captures = pathlib.Path(__file__).parent / 'billing-captures'
        for source in json.loads((captures / 'manifest.json').read_text()):
            data = (captures / source['file']).read_bytes()
            self.assertEqual(source['sourceSha256'], hashlib.sha256(data).hexdigest())
            generator.official_evidence('meta' if source['file'].startswith('meta') else 'deepseek' if source['file'].startswith('deepseek') else 'zai', source['sourceUrl'])


if __name__ == '__main__': unittest.main()
