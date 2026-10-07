# WL-1 polished-source refresh

Merged staging e17abf8af3c3b7bec24214a0e2ba93b3a15903da, including animated snapshot 58ba80da4 and static snapshot 00e2c2670. Both embedded resources still point directly at the design JSON files; rebuilt assemblies contain the new bytes. No shader or proof changes were necessary.

Validation: 378 UI cases passed (54 shader compile cases, 240 animated pack/theme loop plus T/2 motion cases, catalogue and behavioral tests, backdrop/chrome and traceability). CPU smoke: 300 static pack/theme frames, opaque, cached and idle. App build completed. App regression filter: 80 passed, 95 failed, matching the prior WL-1 Linux counts; failures require user32, Windows image decoding or platform/font setup. Native Windows GPU/glass/performance and live custom-image dialog remain unverified on this Linux executor. Test filters end with &FullyQualifiedName!~Native; no test calls a network or real provider CLI.

Counts: 30 static, 24 animated; 270 packs, 540 palettes. The merge keeps staging's new window-edge glass behavior and the library binding. Both engineering-decision sections are retained without normalizing their existing line endings.

Raw refresh TRX evidence is in codex-wl-refresh-test-evidence.zip.
