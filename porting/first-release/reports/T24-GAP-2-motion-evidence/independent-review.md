# Independent read-only review

Reviewer: `/root/review_motion`, separate subagent invoked under PORT_CONTRACT section 15. No edits or commits; no independent .NET execution.

Verdict: NOT READY. Locked gen.py:231 `.xf>span` uses `opacity .1s ease`; `CzCrossfadeText` and `CzMotion.Ease` implement that correctly. Independent 80-iteration solve: opacity frame0=1, frame1=.779326255418, frame2=.424137682422, frame3=.197596612415. The unchanged curve assertion allows frame1 <=.799327255418; GAP requires >=.85. Six 15% steps cannot finish a unit of travel. Placeholder 150ms ease likewise has a 23.34056% ideal second step.

Frame 0 is present in FrameCurves.Sample; the rig advances both clocks together; CzNumber uses action-time monotonic elapsed seconds; the outgoing scalar is synchronized before fading; height is independent; +1ms endpoint completion is unrelated to first/second violations. No clock, first-frame, layout or discrete-property fix can satisfy the disjoint assertions.

Ledger review: engineering prefix 1,441,968 bytes and STATE prefix 47,870 bytes exactly match the original GAP-2 branch. Every incoming changed/new chunk is present byte-exact: engineering 715-byte replacement and 344,462-byte insertion; STATE 603-byte replacement, 13,679-byte middle insertion, 22,229-byte tail insertion. Pure append diffs 756/0 and 131/0 confirmed.
