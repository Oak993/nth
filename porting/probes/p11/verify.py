# P11 verifier: compares the probe's lastError bytes with the literal bytes in the PINNED Swift source,
# extracted mechanically with read-only `git show` (never from memory, never from the spec prose).
# Usage: py verify.py <evidence dir holding the probe's .txt outputs>
# Exit 0 only if every check passes AND the negative controls are rejected.
import subprocess, sys, pathlib, re

REF = r"E:\Droppy\reference\droppy-code"
PIN = "dd2b86efef9e77168f304a86e8a8411877578bbe"
src = subprocess.run(["git", "-C", REF, "show", f"{PIN}:DroppyCode/App/UpdateChecker.swift"],
                     capture_output=True, check=True).stdout
lines = src.split(b"\n")

def literal(lineno, pattern):
    m = re.search(pattern, lines[lineno - 1])
    assert m, f"line {lineno} did not match"
    return m.group(1)

COULDNT = literal(182, rb'\?\? "([^"]*)"')                       # PR-UPDATE-019
BAD_TMPL = literal(272, rb'case \.badResponse\(let status\): "([^"]*)"')  # PR-UPDATE-020
NO_REL = literal(271, rb'case \.noRelease: "([^"]*)"')
def bad(status): return BAD_TMPL.replace(rb"\(status)", str(status).encode())

print("source line 182 literal bytes:", COULDNT.hex(" ").upper())
print("source line 272 template     :", BAD_TMPL.decode())
print("source line 271 literal      :", NO_REL.decode())
i = COULDNT.index(b"Couldn") + 6
print(f"apostrophe byte(s) in source : {COULDNT[i:i+3].hex(' ').upper()} (first byte 0x{COULDNT[i]:02X})")

d = pathlib.Path(sys.argv[1])
def field(name, key):
    for ln in (d / name).read_bytes().split(b"\n"):
        if ln.startswith(key + b"="): return ln[len(key) + 1:]
    return None

def same(a, b): return a is not None and a == b

checks = [
    # (output file, expected lastError bytes, expected requests_sent)
    ("install3_setup_launch.txt", b"<nil>", b"1"),
    ("shortcut_launch.txt",       b"<nil>", b"1"),
    ("unreachable.txt",           COULDNT,  b"1"),
    ("http_non2xx.txt",           bad(404), b"1"),
    ("loopback.txt",              NO_REL,   b"1"),
    ("fx_valid.txt",              b"<nil>", b"0"),
    ("fx_nulls.txt",              b"<nil>", b"0"),
    ("fx_minimal.txt",            NO_REL,   b"0"),
    ("fx_missing_tag.txt",        COULDNT,  b"0"),
    ("fx_type_mismatch.txt",      COULDNT,  b"0"),
]
ok = True
for name, exp, req in checks:
    got = field(name, b"lastError"); n = field(name, b"requests_sent")
    good = same(got, exp) and n == req
    ok &= good
    print(f"{'OK  ' if good else 'FAIL'} {name:28} lastError={got!r} requests_sent={n!r} expected={exp!r}/{req!r}")

# Negative controls: the comparator must REJECT these, or its OK lines above mean nothing.
typographic = COULDNT.replace(b"'", "\u2019".encode())         # what the task text expected (E2 80 99)
unreach = field("unreachable.txt", b"lastError")
ctl = [
    ("output vs U+2019 variant (task text)", same(unreach, typographic)),
    ("U+2019 variant vs source",             same(typographic, COULDNT)),
    ("output vs trailing-dot-dropped",       same(unreach, COULDNT[:-1])),
    ("404 output vs HTTP 403 template",      same(field("http_non2xx.txt", b"lastError"), bad(403))),
]
for label, matched in ctl:
    print(f"{'CONTROL REJECTED' if not matched else 'CONTROL WRONGLY ACCEPTED'}: {label}")
    ok &= not matched

has_2019 = b"\xe2\x80\x99" in (d / "unreachable.txt").read_bytes()
print("E2 80 99 present in unreachable output:", has_2019)
print("VERIFY:", "PASS" if ok else "FAIL")
sys.exit(0 if ok else 1)
