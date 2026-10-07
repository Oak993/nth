"""Offline reproduction against byte-exact mirrored IR-3, no generated reference edits."""
import importlib.util
import json
from pathlib import Path

root = Path(__file__).resolve().parents[4]
design = root / 'porting/first-release/design-src/models-palette'
source = design / 'auto-colour/hardening/r3/intensity_r3.py'
spec = importlib.util.spec_from_file_location('t37_ir3', source)
ir3 = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ir3)
ir3.HERE = str(design / 'intensity')
ir3.RAW_OR = str(design / 'intensity/openrouter-catalogue-snapshot.json')
builtin, catalogue, raw = ir3.load()
catalogue, raw = ir3._with(catalogue, raw, ir3._row('openai', 'openai/gpt-7', 0, 2000000000, 1000000))
lineups = ir3.build_lineups(builtin, catalogue, raw)
print(json.dumps({
    'source': str(source.relative_to(root)),
    'input': {'id': 'openai/gpt-7', 'price': 0, 'created': 2000000000, 'context': 1000000},
    'gpt-7': ir3.lookup('openai', 'gpt-7', lineups),
    'gpt-7-mini': ir3.lookup('openai', 'gpt-7-mini', lineups),
    'scope': 'off-catalogue placement with all older family members made legacy; PLAN 12 acknowledges id-only legacy residuals',
}, indent=2))
