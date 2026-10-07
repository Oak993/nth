# T14 Strike tests evidence

A-17 phase inputs follow the locked gen-vectors.mjs:455-458 construction for every one of the 80 cases. Each phase and start remains checked against its stored JSON value at the unchanged 1e-7 precision. Frame counts, wait seconds, Tfrom, jump offsets, draws, vectors and tolerances remain unchanged. Lead approved this reconstruction on 2026-10-07.

The independent scratch probe runs the unchanged generator loader (including all six source SHA checks), the locked engine plan, and the locked page IsQuiet predicate. It found no full-precision failures in all 80 cases. C# acceptance validation remains pending.

The 19 discrete rounding ties below are zero-based case indices. They are reported, without adding epsilon to production comparisons.

| Case | k | Phase | Stored Tstart | Full precision Tstart | Expected frames | Rounded replay frames |
| --- | --- | --- | --- | --- | --- | --- |
| 2 | 36860 | -0.05 | 88464.26163185 | 88464.26163184972 | 118 | 117 |
| 3 | 36860 | 0 | 88464.31163185 | 88464.31163184972 | 115 | 114 |
| 4 | 36860 | 0.5 | 88464.81163185 | 88464.81163184972 | 85 | 84 |
| 5 | 36860 | 1 | 88465.31163185 | 88465.31163184972 | 55 | 54 |
| 14 | 36861 | 1.85 | 88468.645455065 | 88468.64545506502 | 3 | 4 |
| 30 | 36863 | 1.85 | 88473.508617224 | 88473.50861722435 | 3 | 4 |
| 34 | 36864 | -0.05 | 88474.305126202 | 88474.30512620184 | 118 | 117 |
| 35 | 36864 | 0 | 88474.355126202 | 88474.35512620184 | 115 | 114 |
| 36 | 36864 | 0.5 | 88474.855126202 | 88474.85512620184 | 85 | 84 |
| 37 | 36864 | 1 | 88475.355126202 | 88475.35512620184 | 55 | 54 |
| 46 | 36870 | 1.85 | 88490.256741945 | 88490.2567419453 | 3 | 4 |
| 66 | 36898 | -0.05 | 88555.138207438 | 88555.13820743797 | 118 | 117 |
| 67 | 36898 | 0 | 88555.188207438 | 88555.18820743797 | 115 | 114 |
| 68 | 36898 | 0.5 | 88555.688207438 | 88555.68820743797 | 85 | 84 |
| 69 | 36898 | 1 | 88556.188207438 | 88556.18820743797 | 55 | 54 |
| 74 | 36918 | -0.05 | 88603.640627074 | 88603.64062707352 | 118 | 117 |
| 75 | 36918 | 0 | 88603.690627074 | 88603.69062707352 | 115 | 114 |
| 76 | 36918 | 0.5 | 88604.190627074 | 88604.19062707352 | 85 | 84 |
| 77 | 36918 | 1 | 88604.690627074 | 88604.69062707352 | 55 | 54 |

A-17 C# validation completed after the uniform reconstruction: Phase A 34/34 and the later Composer/Traceability sweeps include the 80 model-wait cases, with their original expected frames/timestamps/tolerances and byte-identical vectors. The preceding pending statement is historical. Native Phase E needs the lead's quiet window; the lead waives mark.mjs specifically for T14, with no queue or T-M7 edits.
