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
