# Reproducing T24 CPU evidence

`before.csv` runs the original GAP-2 UI control/scalar at 4f0f2ac07. `after.csv` uses the same probe on merge 6621f2c7. `after-page.csv` samples the actual merged Customization page and scalar. No repaired motion is claimed.

`run-probe.py` copies the checked-in C# source/project templates into a temporary project; it does not edit the port. With a .NET 10 SDK available:

```bash
python3 run-probe.py cpu /path/to/baseline-checkout before /tmp/before.csv /path/to/dotnet
python3 run-probe.py cpu /path/to/merged-checkout after /tmp/after.csv /path/to/dotnet
python3 run-probe.py page /path/to/merged-checkout after /tmp/after-page.csv /path/to/dotnet
```

The baseline checkout must be at 4f0f2ac07eb135609c2751c356c3c1355b9c964a. The merged code must be at 6621f2c7 (or the later evidence-only delivery commit). The page probe needs the existing GAP-2 real ICU bridge on Linux; `commands.json` records its build, and `GAP-2-evidence/icu-aliases.S` is its unchanged source. Set LD_LIBRARY_PATH to the bridge's directory. CPU mode never measures fonts or loads ICU.

`commands.json` also records the actual build, filtered tests and verification commands. The existing GAP/curve filtered command JSON files are retained. Raw logs and TRX files are included even though the repository normally ignores `.log` files.

The checker consumes the checked-in CSVs, independently solves CSS ease, verifies all 25 frames and compares baseline/merged/page/scalar values. Run `python3 check-frames.py`; it intentionally exits **1 / NOT READY** because the unchanged 15% cap fails. The native Windows test remains unverified.
