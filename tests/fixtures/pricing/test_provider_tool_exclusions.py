"""Round 4: excluded fees require fresh production-builder and transport-guard proof."""
import copy
import hashlib
import importlib.util
import json
import pathlib
import tempfile
import unittest
from unittest.mock import patch

ROOT = pathlib.Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location('tool_prices', ROOT / 'porting/tools/generate-model-prices.py')
generator = importlib.util.module_from_spec(spec); spec.loader.exec_module(generator)


class ProviderToolExclusionTests(unittest.TestCase):
    URL = 'https://docs.z.ai/guides/overview/pricing'
    QUOTE = 'Web Search $0.01 / use'
    STATEMENT = "applies only to the provider's web-search tool, which Marrow never enables"
    PAGE = b'<p>USD per million tokens.</p><table><tr><td>fixture</td><td>2</td><td>4</td></tr></table><p>Web Search $0.01 / use</p>'

    def proof(self):
        paths = ['src/Marrow.Services/Providers/OpenAICompatibleSession.cs', 'src/Marrow.Services/Providers/ZaiSession.cs',
                 'src/Marrow.Services/Providers/ProviderToolBillingGuard.cs', 'tests/Marrow.Services.Tests/Providers/ProviderToolBillingGuardTests.cs']
        return dict(service='zai', statement=self.STATEMENT, sources=[dict(path=path, sourceSha256=hashlib.sha256((ROOT / path).read_bytes()).hexdigest(), line=1) for path in paths])

    def document(self):
        mapping = dict(service='zai', model='fixture', sourceUrl=self.URL, modelCell=[0, 0, 0], inputCell=[0, 0, 1], outputCell=[0, 0, 2], tokenUnitQuote='USD per million tokens', completeListReview='fixture')
        review = dict(id='fixture', service='zai', sourceUrl=self.URL, completeTokenTable=True,
                      pages=[dict(sourceUrl=self.URL, contentSha256=generator.billing_content(self.PAGE)['sha256'])],
                      listedCharges=[dict(sourceUrl=self.URL, quote=self.QUOTE, disposition='excluded-provider-web-search-tool', statement=self.STATEMENT)])
        return dict(models=[mapping], identities=[dict(service='zai', model='fixture', displayText='fixture', sourceUrl=self.URL)], completeListReviews=[review], providerToolProofs=[self.proof()])

    def build(self, document, page=None):
        with tempfile.TemporaryDirectory() as directory:
            return generator.build_reviewed(document, lambda _: page or self.PAGE, *[pathlib.Path(directory) / name for name in ('p.json', 'd.cs', 'm.json')])

    def test_exact_optional_quote_and_fresh_guard_proof_publish_sourced_zero(self):
        manifest = self.build(self.document())
        self.assertEqual((1, 0), (manifest['filledCount'], manifest['unfilledCount']))
        charge = manifest['models'][0]['requestFeeEvidence']['listedConditionalCharges'][0]
        self.assertEqual(self.QUOTE, charge['quote']); self.assertEqual(self.STATEMENT, charge['statement'])
        self.assertEqual(self.URL, charge['sourceUrl']); self.assertIn('retrievedAt', charge)
        self.assertEqual(hashlib.sha256(self.PAGE).hexdigest(), charge['sourceSha256'])
        self.assertEqual(4, len(charge['requestBuilderProof']['sources']))

    def test_future_builder_or_guard_change_invalidates_the_exclusion(self):
        document = self.document()
        for index in range(4):
            changed = copy.deepcopy(document); changed['providerToolProofs'][0]['sources'][index]['sourceSha256'] = '0' * 64
            self.assertEqual(0, self.build(changed)['filledCount'])

    def test_missing_wrong_provider_or_partial_proof_cannot_exclude_a_charge(self):
        for change in ('missing', 'provider', 'partial', 'statement'):
            document = self.document()
            if change == 'missing': document['providerToolProofs'] = []
            if change == 'provider': document['providerToolProofs'][0]['service'] = 'meta'
            if change == 'partial': document['providerToolProofs'][0]['sources'].pop()
            if change == 'statement': document['completeListReviews'][0]['listedCharges'][0]['statement'] = 'optional'
            self.assertEqual(0, self.build(document)['filledCount'])

    def test_new_minimum_and_changed_tool_fee_still_fail_closed(self):
        for extra in (b'<p>Minimum request fee $1.</p>', b'<p>New tool price $2 per query.</p>'):
            self.assertEqual(0, self.build(self.document(), self.PAGE + extra)['filledCount'])
        document = self.document(); document['completeListReviews'][0]['listedCharges'][0]['quote'] = 'Request fee $1'
        self.assertEqual(0, self.build(document)['filledCount'])

    def test_login_page_cannot_be_excluded_as_a_billing_terms_page(self):
        document = self.document(); url = 'https://z.ai/manage-apikey/billing'
        document['completeListReviews'][0]['pages'].append(dict(sourceUrl=url, contentSha256=generator.billing_content(b'Login')['sha256'], billingContentAvailable=False))
        self.assertEqual(0, self.build(document)['filledCount'])

    def test_account_transaction_ui_is_separate_from_public_billing_terms(self):
        url = 'https://z.ai/manage-apikey/billing'
        page = self.PAGE + ('<a href="' + url + '">Account billing</a>').encode()
        document = self.document(); review = document['completeListReviews'][0]
        review['pages'][0]['contentSha256'] = generator.billing_content(page)['sha256']
        self.assertEqual(0, self.build(document, page)['filledCount'])
        review['accountDashboards'] = [dict(sourceUrl=url, kind='account-transaction-dashboard', reason='Account transactions are not public billing terms; no login')]
        self.assertEqual(1, self.build(document, page)['filledCount'])
        review['accountDashboards'][0]['sourceUrl'] = 'https://docs.z.ai/help/billing'
        self.assertEqual(0, self.build(document, page)['filledCount'])

    def test_checked_in_proof_is_current_for_each_real_provider(self):
        proofs = json.loads((ROOT / 'src/Marrow.Services/Providers/Pricing/provider-tool-proof.json').read_text())['providers']
        self.assertEqual({'meta', 'zai'}, {proof['service'] for proof in proofs})
        for proof in proofs: generator.validate_provider_tool_proof(proof['service'], proof)

    def test_missing_production_source_leaves_only_that_model_unfilled(self):
        document = self.document()
        with tempfile.TemporaryDirectory() as directory, patch.object(generator, 'ROOT', pathlib.Path(directory)):
            self.assertEqual(0, self.build(document)['filledCount'])

    def test_an_additional_listed_request_fee_is_not_excluded_with_web_search(self):
        page = self.PAGE + b'<p>Minimum request fee $1.</p>'
        document = self.document(); review = document['completeListReviews'][0]
        review['pages'][0]['contentSha256'] = generator.billing_content(page)['sha256']
        review['listedCharges'].append(dict(sourceUrl=self.URL, quote='Minimum request fee $1.', disposition='unfilled'))
        self.assertEqual(0, self.build(document, page)['filledCount'])


if __name__ == '__main__': unittest.main()
