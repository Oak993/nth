# T34 RR-12 — live words and editor parity

**BLOCKED: automatic RTL paragraph direction, wrapped-fragment motion and Windows visual proof remain incomplete.** The safe Add/confirm visual difference is repaired. Authority: audit RR-12, OD-0091, voice-SPEC V-7.5/V-8.3/V-8.9/V-8.10, BUILD-PLAN §1.1/VT-11, and unchanged STATE line 101. Base: `285b6b7a99b39e0e517f34c8d34a1860c945f83f`. This row was not ALREADY DONE.

## Missing-path proof

`DictationLayer.MakeLayout` uses the editor's global `FlowDirection`/alignment. Bidi runs within that layout do not supply automatic direction/alignment independently for each paragraph. In `Relayout`, `word.Separable = rects.Length == 1` and only `rects[0]` is retained (`DictationLayer.cs:435–439`). Thus a word split across wrapped lines cannot rise as independently shaped fragments; the final-fragment caret/scroll path also needs work. STATE line 101 explicitly retains both limitations. The existing Urdu tests prove RTL runs in the editor's current layout; they do not establish RTL paragraph parity.

## Safe repair and held-out work

Before adding a word, the layer now runs the preceding logical word's ink to the confirmed visual color using the existing 0.45-second motion. The transcriber's logical `Tentative` flag remains revisable until its Confirm operation. This reconciles the design's visible Add behavior with the existing immutable transcriber/DictationWords assertions. The new fake-clock/frame/headless regression proves prior-word ink is confirmed while the logical tail remains tentative. No existing test assertion was changed.

A proposed fragment renderer was held out after independent review found empty-word indexing, incomplete fragment-motion lifetime tracking, and old-text shaping problems when revisions changed wrap counts. Geometry/flags alone would not prove rendered parity. None of that renderer is in these commits; existing fragment behavior remains explicitly incomplete. The earlier new proposal's run stopped at `user32.dll` in the unfaked motion fixture, so it was not visual acceptance evidence.

The unchanged Urdu FLIP test asserts that the first word moves when the RTL run grows in the current left-aligned editor. Replacing the default editor with paragraph-detected right alignment would change that pinned expectation. A complete repair must define a source-faithful paragraph layout shared by editor and animation, preserve existing run behavior, and verify mixed paragraphs and full-context shaping through Add/Revise/Remove/Landing/Withdraw. No test, tolerance or reference is to be weakened to force that outcome. This is unfinished implementation work as well as a Windows proof requirement, not merely a missing screenshot.

Windows run: existing `ComposerDictation*` and `DictationWordsTests`, plus `DictationAddConfirmationTests`, using scripted sessions. Then add pixel parity cases for Urdu/Arabic/Hebrew, mixed LTR/RTL paragraphs, wrapped Latin and RTL words, revisions changing fragment count, empty words, final-line caret and scrolling, landing/undo and cancel restoration. Compare moving/settled frames against the same editor layout and the pinned source/design at all supported scale factors and Reduce Motion. The initial unchanged App baseline already failed at missing `icu.dll`; the scoped ICU-forwarded Urdu baseline also encountered a headless Inter typeface failure. These are not proof that paragraph/wrap gaps are closed.

Engineering entry: ED-NEW-voice-gaps-3. Final validation and limitations are in `T34-voice-gaps-validation.md`.


## Round 2 (2026-10-07)

**DONE implementation; READY for T9 Windows verification.** This supersedes the Round 1 implementation blockers above. Windows proof remains; this is the lead's defined Round 2 verdict, not a release acceptance claim. Ruling: `45096401f90e1d0ae74a9e156cb9f0cb3fa67e60:R-gaps-round2.md`, T34.

### Locked authority and reproduced gaps

The design mirror was read at `26f8fbe89ee8480c9b18b4afe16d6c57bccb3610`, without merging it. Under `porting/first-release/design-src/work-design/`: `build-specs/voice/SPEC.md` V-7.5 and V-8.1 through V-8.10 (drawing/RTL/caret/scroll at 408-435), `published/vcpub/voice-composer.html:123-133`, and `published/vcpub/voice-composer.js:255-281,325-343,427-437`. OD-0091 and BUILD-PLAN section 1.1 remain the authority; the B3 pin remains `52bc7926097cd622d08d6294e0d8b08a48c7516e`, tree `115745914ccc103dfe4e74d96c6c539faab2eedb`.

At Round 2 base `0f13cc3c52f95b04c10391d03bd49ce0b6edb550`, `DictationLayer.Relayout` kept only the first range rectangle, marked a multi-rectangle word nonseparable, and `DrawWord` reshaped the isolated word NoWrap. `MakeLayout` and the actual editor presenter used a single control direction for every paragraph. `PlaceCaret` and `FollowTheEnd` used the first word rectangle. New mounted tests against those paths produced 6 behavior failures out of 7: wrapped arrivals showed full ink on their first frame, carets stayed on the first line, and paragraph alignment/base differed. The empty-word path already passed. A line-fragment attempt then failed the first-frame width-reflow pixel check (37/38 passed); it was repaired before delivery.

### Delivered behavior and engineering choices

`ComposerParagraphLayout` supplies the actual editor, measurement, controller geometry, live layer and selection ghost with the same native paragraph formatting. Base direction is the first strong Unicode class outside isolates for each paragraph, including leading digits/marks, CRLF and Unicode paragraph separators; a soft line separator retains its paragraph's base. Physical left alignment preserves the prior editor geometry and the immutable Urdu FLIP guard. No UI strings or design timings changed.

Avalonia 12.1.2 has no TextLayout per-paragraph callback. An adapter delegates its real TextFormatter for a private composer marker source; other sources, both overloads and the native cache argument pass through unchanged. Native lines retain separate immutable paragraph properties for hit tests. A narrow version-pinned locator binding uses reflection because Avalonia omits that public runtime API from reference assemblies; an API change fails explicitly. The composer TextPresenter retains native editor interaction and recreates selection/IME spans on this shared factory using the native presenter's exact layout constraints.

`DictationPicture` owns complete native shaped glyph clusters copied from the entire target paragraph, preserving glyph indices/advances/offsets, fallback face, bidi level and baseline. No word-only reshape or clipping of glyph ink is used. Each cluster follows its actual native draw origin on .3-second ez FLIP across wrapping and line baseline changes. Prefix/suffix context also follows reflow. Captured revision pictures keep their independent geometry, shaping, opacity/rise and expiry through repeated revisions and changing wrap counts; their resources are disposed on expiry/end/detach. Landing waits for all current, old and context cluster motion and the extra settled frame.

Mask/ink/ghost overrides preserve font features. Shared layout and cache invalidation include line height, inherited letter spacing, font stretch/size/features, wrapping, alignment and flow; theme ink is in the paint cache. Final-character native caret hits select the final wrapped line, with the locked 1.5 by 17 geometry and correct local bidi edge. A required terminal break uses the next empty line; LF/CRLF red tests reproduced the preceding-line error before repair. Scroll follows the final line on the original .36-second ez.

### Verification and review

Core voice/traceability: **656 passed**. Services voice/traceability: **491 passed, 12 Windows-native skipped**. Combined affected App/traceability: **121 passed**, including **57 new** RR-12 checks. New checks use fake time/frames/native motion preferences, the actual CPU raster, and an independently formatted native reference; whole surfaces compare byte-exact (the separately verified live caret is excluded only from text-picture comparisons). They cover Latin/Urdu/combining marks/ligatures, full fractional arrival and revision/withdraw pixels, wrap count changes, repeated revisions, first-frame reflow/baseline continuity, all-fragment landing lifetime, mixed paragraph selection landing/one undo/cancel, Reduce Motion, final caret/scroll and each style property independently. The separate Urdu replay retains the original >5px move, .3-second curve and 1e-3 position guard; it supplements the original test.

Test-first font-feature and spacing witnesses: 5/6 and 5/7 passed respectively before repairs, with full-picture pixel differences. The terminal-break run had two real LF/CRLF failures; its other failure was a newly authored oracle's hardcoded line-height assumption. That new oracle was corrected to native line metrics. Subsequent exact comparisons exposed floating point associativity in the new expected expression; summing preceding native lines before centering preserves exact equality. No original test, assertion, tolerance, fixture or reference was edited or retired.

Unchanged composer tests: **26 passed, 24 failed**; all 24 stop at Linux environment dependencies (19 glyphTypeface creation failures (6 default, 13 Inter), 5 missing user32.dll), before the parity assertions. They are unverified and on T9's list. No GPU, microphone, model or real application download/network was used. The external Linux ICU ABI forwarder delegates real ICU 76 and changes no repo file; explicit DejaVu Sans in the new Linux-only fixture is not native Windows font proof.

Independent contract section 15 review found font-feature, inherited spacing/line-height and terminal-break issues; each was repaired and retested. Its final read of source and completed evidence found no remaining concrete implementation/resource/interaction issue and cleared RR-12 for T9 Windows proof. It also required isolated property invalidation cases and preserved exact baseline/Urdu guards; these pass.

### Needs Windows — T9

1. Run all new tests with normal Windows fonts/ICU, plus unchanged `ComposerDictation*`, `ComposerTextViewTests`, `DictationWordsTests` and full Core/Services/App suites. The 12 Services native skips and original Linux dependency failures remain unverified.
2. Compare native editor/layer and locked design at supported 100/125/150/200 percent scales, light/dark themes and Reduce Motion: Urdu/Arabic/Persian/Hebrew, mixed paragraphs, leading digits/marks/isolates, terminal breaks, combining/ligature/emoji/fallback-font overhangs and baselines. Prove Add/Confirm/Revise/Remove, repeated revisions, changing wrap counts, width reflow, zero-length words, old pictures and final-line caret/scroll.
3. Capture last-layer/first-editor pixels for landing, cancel/selection restoration and one undo; verify real pointer hit testing, keyboard bidi selection, IME/preedit, read-only dictation, caret and composer height/scroll integration. Exercise native font features/line-height/letter-spacing changes and long-draft performance.
4. Repeat RR-10 native companion publication/restart/loader/backend and CUDA A appearance/accessibility from its existing report. RR-11 integration belongs to T33 after its local dependencies; it is outside this Round 2 implementation verdict.

Gate B passes all 8 implemented checks. Workspace protected paths are unchanged; the protected script's printed "reference checkout clean" is unusable because all E:/ reference directories are absent. Gates/full release/native rendering need the real Windows reference environment. Byte-exact ledger append ED-NEW-voice-gaps-4 and ownership/original-test checks are recorded in the validation report.
