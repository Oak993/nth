# Generates v2.json, the strings fixture of WV-1 (OD-0098 welcome-voice pick "Place A, Words V2"), from
# porting/first-release/design-src/welcome-voice/wv_lib.py: its WORDS['V2'] and PATH, read as literals (the file is not
# imported: it needs gr_lib from another folder). Run from anywhere: python3 tests/fixtures/welcome-voice/gen_v2.py
import ast, json, os

HERE = os.path.dirname(os.path.abspath(__file__))
SOURCE = os.path.join(HERE, '..', '..', '..', 'porting', 'first-release', 'design-src', 'welcome-voice', 'wv_lib.py')


def value(node):
    # WORDS is written with dict(...) calls and string literals only
    if isinstance(node, ast.Constant):
        return node.value
    if isinstance(node, ast.Dict):
        return {value(k): value(v) for k, v in zip(node.keys, node.values)}
    if isinstance(node, ast.Call) and isinstance(node.func, ast.Name) and node.func.id == 'dict' and not node.args:
        return {k.arg: value(k.value) for k in node.keywords}
    raise ValueError('not a literal: line %d' % node.lineno)


with open(SOURCE, encoding='utf-8') as f:
    tree = ast.parse(f.read())
found = {}
for node in tree.body:
    if isinstance(node, ast.Assign) and len(node.targets) == 1 and isinstance(node.targets[0], ast.Name) and node.targets[0].id in ('WORDS', 'PATH'):
        found[node.targets[0].id] = value(node.value)
out = {'source': 'porting/first-release/design-src/welcome-voice/wv_lib.py', 'set': 'V2', 'path': found['PATH'], 'words': found['WORDS']['V2']}
with open(os.path.join(HERE, 'v2.json'), 'w', encoding='utf-8', newline='\n') as f:
    json.dump(out, f, ensure_ascii=False, indent=2)
    f.write('\n')
