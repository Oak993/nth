# SF Symbols to Lucide (OD-0010)

Mapping of every SF Symbol that Droppy Code 1.6.2 can draw to a Lucide icon, for the Windows port. Source tree: `E:/Droppy/reference/droppy-code-v1.6.2 (1.6.2)`. Lucide: `lucide-static` 1.47.0 from npm (`npm pack lucide-static`), ISC licence, copied to `lucide/LICENSE`.

Files:

- `sf-symbols-to-lucide.json`: `symbols` (one entry per SF name: `sf`, `lucide`, `sites`, `confidence`, `note`, plus `used_in`, `variant`, `composed_rendering`, `fill_rendering`) and `catalogue` (Popular shortlist and the 27 picker categories in source order, each symbol mapped).
- `lucide/`: the 713 chosen Lucide SVGs, unmodified, and the Lucide LICENSE.
- `build_mapping.mjs`, `gen_readme.mjs`: the scripts that produced the JSON, the SVG copies and this file.

## Counts

2753 distinct SF names: 146 drawn by app code at 398 sites in 57 files, 56 in the Popular shortlist (`ProjectIconPicker.swift:87-102`), 2630 in the picker catalogue (`ProjectSymbolCatalogue.swift:25-51`). The groups overlap.

| Set | Names | exact | close | approximate | composed |
| --- | --- | --- | --- | --- | --- |
| App code | 146 | 98 | 44 | 4 | 4 |
| Popular | 56 | 36 | 15 | 5 | 1 |
| Catalogue | 2630 | 293 | 791 | 1546 | 1232 |
| All | 2753 | 372 | 830 | 1551 | 1235 |

Unmapped: 0. Lucide icons used: 713. SF names with a `.fill` form: 2626.

Confidence: `exact` = same glyph and meaning; `close` = same meaning, small drawing difference (named in the note); `approximate` = Lucide has no equivalent, the nearest icon is proposed and the note says what is lost.

## How the list was derived at B2

- B1 list `porting/evidence/sf_symbols_used.txt`: 70 names. All 70 are still used at B2; 128 more names were found (most are the Popular shortlist and helper arguments B1 did not follow).
- String literals were extracted from every `.swift` file under `DroppyCode/` (catalogue excluded) where the line or the enclosing declaration names a symbol (`systemName:`, `systemImage:`, `systemSymbolName:`, `symbol:`, `Symbol`, `icon`, `glyph`, `Image(`), plus every dotted literal. Non-symbols (hosts, file names, RPC methods, search keywords, keycap ids) were removed by hand. A second pass checked every remaining literal against the catalogue vocabulary and added `HydraGlyph.swift:48-49`.
- Helpers that take a symbol name and pass it to `Image(systemName:)` were followed to their call sites: `PopoverItem`, `RowAction`, `ChromeCircleButton`, `ChromeIconButton`, `ChromeMenuButton`, `RoundIconButton`, `QueueIconButton`, `SidebarSymbol`, `RowAccessoryIcon`, `ToolStatusIcon`, `ChromeSegmentedOption`, `ShortcutSectionHeader`, `fact(symbol:)`, `Suggestion`, `CommandPalette.Item`, `ProjectScript`, `Stave`, the `symbol` properties of `Requests`, `Provider`, `Shortcuts`, `SettingsView` pages, `ToolPresentation`, `WorkGroupSummary`, and `ProjectFile.symbol(for:)` (`AppModel.swift:1346-1353`, which maps script icons test/lint/configure/build/debug to symbols).
- `ProjectIcon.symbol(name)` stores any catalogue name, so every catalogue name is a drawable symbol. The catalogue comment says every entry is a filled mark; four entries have no `.fill` form (`apple.logo`, `curlybraces`, `swift`, `terminal`).

## Rendering rules

- Lucide is stroke-only (`fill="none"`, `stroke="currentColor"`, width 2 on a 24 grid). An SF `.fill` name is a solid glyph: render the Lucide outline, or fill its closed paths with currentColor. `fill_rendering` marks these.
- Containers (`.circle`, `.square`, `.app`, `.rectangle`, `.diamond`, ...): the Lucide `circle-*` or `square-*` form is used where it exists; otherwise `composed_rendering` is true and the note says to enclose the glyph. An `.app` container is the SF squircle, drawn with the Lucide rounded square.
- `.slash`: the Lucide `-off` form is used where it exists; otherwise the note says to draw a diagonal slash.
- `.badge.*` and `.trianglebadge.*`: Lucide has a few badged forms (`bell-dot`, `file-plus`, `user-plus`, ...); the rest are composed with a corner badge.
- Lucide 1.x renamed some icons; this table uses the canonical 1.x names (for example `trash`, formerly `trash-2`; `square-centerline-dashed-horizontal`, formerly `flip-horizontal`). The old names ship as aliases with identical SVG bodies.

## Limits of this table

- SF Symbols cannot be rendered on this Windows machine and the SF Symbols app was not available, so shape judgments come from the symbol names and knowledge of the SF glyphs, not from a side-by-side render. The Lucide side was checked against the SVG sources. A visual review on a Mac is still needed before the table counts as verified.
- Upstream quirk kept as found: `DiffInspector.swift:57` labels `arrow.down.right.and.arrow.up.left` (inward arrows) "Expand all" and `:58` labels `arrow.up.left.and.arrow.down.right` (outward arrows) "Collapse all". The table maps the glyphs, not the labels.

## Approximate mappings used by the app or the Popular shortlist (9)

| SF | Lucide | Sites | Note |
| --- | --- | --- | --- |
| `macwindow.on.rectangle` | `app-window-mac` | DroppyCode/App/Views/Settings/SettingsView.swift:434 | composed: window floating over a rectangle |
| `network` | `globe` | DroppyCode/App/Views/Settings/MCPSettingsPage.swift:639 | SF network is a wireframe sphere; Lucide network is a node tree. globe is nearer in shape (not rendered here, see README) |
| `point.3.connected.trianglepath.dotted` | `share-2` | DroppyCode/App/Views/Settings/SettingsView.swift:43, DroppyCode/App/Views/Sidebar/SidebarView.swift:1160 | SF: three nodes joined by dotted lines in a triangle; Lucide share-2 joins three nodes with two solid lines |
| `point.3.filled.connected.trianglepath.dotted` | `share-2` | DroppyCode/UI/Chrome/ProjectIconPicker.swift:99 | SF: three filled nodes joined by dotted lines in a triangle; Lucide share-2 joins three nodes with two solid lines |
| `takeoutbag.and.cup.and.straw.fill` | `cup-soda` | DroppyCode/UI/Chrome/ProjectIconPicker.swift:101 | SF shows a takeout bag and a cup with a straw; Lucide cup-soda is the cup alone |
| `tray.full.fill` | `inbox` | DroppyCode/UI/Chrome/ProjectIconPicker.swift:90 | SF shows papers in the tray |
| `waterbottle.fill` | `milk` | DroppyCode/UI/Chrome/ProjectIconPicker.swift:101 | Lucide has no water bottle; milk is the nearest bottle |
| `wrench.and.screwdriver` | `wrench` | DroppyCode/App/Views/Chat/TimelineRows.swift:1618, DroppyCode/App/Views/Chat/TimelineRows.swift:2231, DroppyCode/Core/Models/Requests.swift:48 | Lucide has no crossed wrench and screwdriver |
| `wrench.and.screwdriver.fill` | `wrench` | DroppyCode/UI/Chrome/ProjectIconPicker.swift:88 | Lucide has no crossed wrench and screwdriver |

## Every approximate mapping (1551), grouped by the Lucide icon proposed

Each SF name has its own note in the JSON. Most catalogue approximations are vehicle states, fitness poses, device models, letters and numerals in a container (drawn as text in a Lucide circle or square), and currency signs Lucide lacks.

- `car` (113): `bolt.car.circle.fill`, `bolt.car.fill`, `car.2.fill`, `car.front.waves.down.fill`, `car.front.waves.left.and.right.and.up.fill`, `car.front.waves.up.fill`, `car.side.air.circulate.fill`, `car.side.air.fresh.fill`, `car.side.and.exclamationmark.fill`, `car.side.arrow.left.and.right.fill`, `car.side.arrowtriangle.down.fill`, `car.side.arrowtriangle.up.arrowtriangle.down.fill`, `car.side.arrowtriangle.up.fill`, `car.side.front.open.crop.fill`, `car.side.front.open.fill`, `car.side.hill.descent.control.fill`, `car.side.hill.down.and.gauge.open.with.lines.needle.25percent.and.arrowtriangle.fill`, `car.side.hill.down.fill`, `car.side.hill.up.fill`, `car.side.lock.fill`, `car.side.lock.open.fill`, `car.side.rear.crop.trunk.partition.fill`, `car.side.rear.open.crop.fill`, `car.side.rear.open.fill`, `car.side.rear.tow.hitch.fill`, `car.side.roof.cargo.carrier.fill`, `car.side.roof.cargo.carrier.slash.fill`, `car.top.arrowtriangle.front.left.fill`, `car.top.arrowtriangle.front.right.fill`, `car.top.arrowtriangle.rear.left.fill`, `car.top.arrowtriangle.rear.right.fill`, `car.top.door.front.left.and.front.right.and.rear.left.and.rear.right.open.fill`, `car.top.door.front.left.and.front.right.and.rear.left.open.fill`, `car.top.door.front.left.and.front.right.and.rear.right.open.fill`, `car.top.door.front.left.and.front.right.open.fill`, `car.top.door.front.left.and.rear.left.and.rear.right.open.fill`, `car.top.door.front.left.and.rear.left.open.fill`, `car.top.door.front.left.and.rear.right.open.fill`, `car.top.door.front.left.open.fill`, `car.top.door.front.right.and.rear.left.and.rear.right.open.fill`, `car.top.door.front.right.and.rear.left.open.fill`, `car.top.door.front.right.and.rear.right.open.fill`, `car.top.door.front.right.open.fill`, `car.top.door.rear.left.and.rear.right.open.fill`, `car.top.door.rear.left.open.fill`, `car.top.door.rear.right.open.fill`, `car.top.door.sliding.left.open.fill`, `car.top.door.sliding.right.open.fill`, `car.top.front.radiowaves.front.left.and.front.and.front.right.fill`, `car.top.frontleft.arrowtriangle.fill`, `car.top.frontright.arrowtriangle.fill`, `car.top.lane.dashed.arrowtriangle.inward.fill`, `car.top.lane.dashed.badge.steeringwheel.fill`, `car.top.lane.dashed.departure.left.fill`, `car.top.lane.dashed.departure.left.slash.fill`, `car.top.lane.dashed.departure.right.fill`, `car.top.lane.dashed.departure.right.slash.fill`, `car.top.radiowaves.2.front.left.front.front.right.fill`, `car.top.radiowaves.2.rear.left.rear.rear.right.fill`, `car.top.radiowaves.front.fill`, `car.top.radiowaves.rear.fill`, `car.top.radiowaves.rear.left.and.rear.right.fill`, `car.top.radiowaves.rear.left.car.top.front.fill`, `car.top.radiowaves.rear.left.fill`, `car.top.radiowaves.rear.right.badge.exclamationmark.fill`, `car.top.radiowaves.rear.right.badge.xmark.fill`, `car.top.radiowaves.rear.right.car.top.front.fill`, `car.top.radiowaves.rear.right.fill`, `car.top.rear.radiowaves.rear.left.and.rear.and.rear.right.fill`, `car.top.rearleft.arrowtriangle.fill`, `car.top.rearright.arrowtriangle.fill`, `car.top.video.rear.left.fill`, `car.top.video.rear.right.fill`, `convertible.side.air.circulate.fill`, `convertible.side.air.fresh.fill`, `convertible.side.and.exclamationmark.fill`, `convertible.side.arrow.left.and.right.fill`, `convertible.side.arrow.trianglehead.backward.fill`, `convertible.side.arrow.trianglehead.forward.and.backward.fill`, `convertible.side.arrow.trianglehead.forward.fill`, `convertible.side.arrowtriangle.down.fill`, `convertible.side.arrowtriangle.up.arrowtriangle.down.fill`, `convertible.side.arrowtriangle.up.fill`, `convertible.side.fill`, `convertible.side.front.open.crop.fill`, `convertible.side.front.open.fill`, `convertible.side.hill.descent.control.fill`, `convertible.side.hill.down.and.gauge.open.with.lines.needle.25percent.and.arrowtriangle.fill`, `convertible.side.hill.down.fill`, `convertible.side.hill.up.fill`, `convertible.side.lock.fill`, `convertible.side.lock.open.fill`, `dot.car.top.radiowaves.2.rear.left.rear.rear.right.fill`, `suv.side.air.circulate.fill`, `suv.side.air.fresh.fill`, `suv.side.and.exclamationmark.fill`, `suv.side.arrow.left.and.right.fill`, `suv.side.arrowtriangle.down.fill`, `suv.side.arrowtriangle.up.arrowtriangle.down.fill`, `suv.side.arrowtriangle.up.fill`, `suv.side.fill`, `suv.side.front.open.crop.fill`, `suv.side.front.open.fill`, `suv.side.hill.descent.control.fill`, `suv.side.hill.down.and.gauge.open.with.lines.needle.25percent.and.arrowtriangle.fill`, `suv.side.hill.down.fill`, `suv.side.hill.up.fill`, `suv.side.lock.fill`, `suv.side.lock.open.fill`, `suv.side.rear.open.crop.fill`, `suv.side.rear.open.fill`, `suv.side.roof.cargo.carrier.fill`, `suv.side.roof.cargo.carrier.slash.fill`
- `circle` (103): `0.circle.fill`, `00.circle.fill`, `01.circle.fill`, `02.circle.fill`, `03.circle.fill`, `04.circle.fill`, `05.circle.fill`, `06.circle.fill`, `07.circle.fill`, `08.circle.fill`, `09.circle.fill`, `1.circle.fill`, `10.circle.fill`, `11.circle.fill`, `12.circle.fill`, `13.circle.fill`, `14.circle.fill`, `15.circle.fill`, `16.circle.fill`, `17.circle.fill`, `18.circle.fill`, `19.circle.fill`, `2.circle.fill`, `20.circle.fill`, `21.circle.fill`, `22.circle.fill`, `23.circle.fill`, `24.circle.fill`, `25.circle.fill`, `26.circle.fill`, `27.circle.fill`, `28.circle.fill`, `29.circle.fill`, `2h.circle.fill`, `3.circle.fill`, `30.circle.fill`, `31.circle.fill`, `32.circle.fill`, `33.circle.fill`, `34.circle.fill`, `35.circle.fill`, `36.circle.fill`, `37.circle.fill`, `38.circle.fill`, `39.circle.fill`, `4.alt.circle.fill`, `4.circle.fill`, `40.circle.fill`, `41.circle.fill`, `42.circle.fill`, `43.circle.fill`, `44.circle.fill`, `45.circle.fill`, `46.circle.fill`, `47.circle.fill`, `48.circle.fill`, `49.circle.fill`, `4a.circle.fill`, `4h.circle.fill`, `4l.circle.fill`, `5.circle.fill`, `50.circle.fill`, `6.alt.circle.fill`, `6.circle.fill`, `7.circle.fill`, `8.circle.fill`, `9.alt.circle.fill`, `9.circle.fill`, `a.circle.fill`, `b.circle.fill`, `c.circle.fill`, `d.circle.fill`, `e.circle.fill`, `f.circle.fill`, `g.circle.fill`, `h.circle.fill`, `i.circle.fill`, `j.circle.fill`, `k.circle.fill`, `l.circle.fill`, `l1.circle.fill`, `l2.circle.fill`, `lb.circle.fill`, `left.circle.fill`, `lt.circle.fill`, `m.circle.fill`, `n.circle.fill`, `o.circle.fill`, `q.circle.fill`, `r.circle.fill`, `r1.circle.fill`, `r2.circle.fill`, `rb.circle.fill`, `right.circle.fill`, `rt.circle.fill`, `s.circle.fill`, `sos.circle.fill`, `t.circle.fill`, `u.circle.fill`, `v.circle.fill`, `w.circle.fill`, `y.circle.fill`, `z.circle.fill`
- `square` (89): `0.square.fill`, `00.square.fill`, `01.square.fill`, `02.square.fill`, `03.square.fill`, `04.square.fill`, `05.square.fill`, `06.square.fill`, `07.square.fill`, `08.square.fill`, `09.square.fill`, `1.square.fill`, `10.square.fill`, `11.square.fill`, `12.square.fill`, `13.square.fill`, `14.square.fill`, `15.square.fill`, `16.square.fill`, `17.square.fill`, `18.square.fill`, `19.square.fill`, `2.square.fill`, `20.square.fill`, `21.square.fill`, `22.square.fill`, `23.square.fill`, `24.square.fill`, `25.square.fill`, `26.square.fill`, `27.square.fill`, `28.square.fill`, `29.square.fill`, `3.square.fill`, `30.square.fill`, `31.square.fill`, `32.square.fill`, `33.square.fill`, `34.square.fill`, `35.square.fill`, `36.square.fill`, `37.square.fill`, `38.square.fill`, `39.square.fill`, `4.alt.square.fill`, `4.square.fill`, `40.square.fill`, `41.square.fill`, `42.square.fill`, `43.square.fill`, `44.square.fill`, `45.square.fill`, `46.square.fill`, `47.square.fill`, `48.square.fill`, `49.square.fill`, `5.square.fill`, `50.square.fill`, `6.alt.square.fill`, `6.square.fill`, `7.square.fill`, `8.square.fill`, `9.alt.square.fill`, `9.square.fill`, `a.square.fill`, `b.square.fill`, `c.square.fill`, `d.square.fill`, `e.square.fill`, `f.square.fill`, `g.square.fill`, `h.square.fill`, `heart.text.square.fill`, `i.square.fill`, `j.square.fill`, `k.square.fill`, `l.square.fill`, `n.square.fill`, `o.square.fill`, `q.square.fill`, `r.square.fill`, `s.square.fill`, `square.text.square.fill`, `t.square.fill`, `u.square.fill`, `v.square.fill`, `w.square.fill`, `y.square.fill`, `z.square.fill`
- `rectangle-horizontal` (55): `button.angledbottom.horizontal.left.fill`, `button.angledbottom.horizontal.right.fill`, `button.angledtop.vertical.left.fill`, `button.angledtop.vertical.right.fill`, `button.horizontal.fill`, `button.horizontal.top.press.fill`, `button.roundedbottom.horizontal.fill`, `button.roundedtop.horizontal.fill`, `button.vertical.left.press.fill`, `button.vertical.right.press.fill`, `capsule.fill`, `capsule.on.capsule.fill`, `capsule.on.rectangle.fill`, `l1.button.roundedbottom.horizontal.fill`, `l1.rectangle.roundedbottom.fill`, `l2.button.angledtop.vertical.left.fill`, `l2.button.roundedtop.horizontal.fill`, `l2.rectangle.roundedtop.fill`, `l3.button.angledbottom.horizontal.left.fill`, `l4.button.horizontal.fill`, `lb.button.roundedbottom.horizontal.fill`, `lb.rectangle.roundedbottom.fill`, `licenseplate.fill`, `lm.button.horizontal.fill`, `lsb.button.angledbottom.horizontal.left.fill`, `lt.button.roundedtop.horizontal.fill`, `lt.rectangle.roundedtop.fill`, `m1.button.horizontal.fill`, `m2.button.horizontal.fill`, `m3.button.horizontal.fill`, `m4.button.horizontal.fill`, `p1.button.horizontal.fill`, `p2.button.horizontal.fill`, `p3.button.horizontal.fill`, `p4.button.horizontal.fill`, `r1.button.roundedbottom.horizontal.fill`, `r1.rectangle.roundedbottom.fill`, `r2.button.angledtop.vertical.right.fill`, `r2.button.roundedtop.horizontal.fill`, `r2.rectangle.roundedtop.fill`, `r3.button.angledbottom.horizontal.right.fill`, `r4.button.horizontal.fill`, `rb.button.roundedbottom.horizontal.fill`, `rb.rectangle.roundedbottom.fill`, `rectangle.roundedbottom.fill`, `rectangle.roundedtop.fill`, `rectangle.slash.fill`, `rm.button.horizontal.fill`, `rsb.button.angledbottom.horizontal.right.fill`, `rt.button.roundedtop.horizontal.fill`, `rt.rectangle.roundedtop.fill`, `zl.button.roundedtop.horizontal.fill`, `zl.rectangle.roundedtop.fill`, `zr.button.roundedtop.horizontal.fill`, `zr.rectangle.roundedtop.fill`
- `currency` (54): `australsign.circle.fill`, `australsign.square.fill`, `bahtsign.circle.fill`, `bahtsign.square.fill`, `brazilianrealsign.circle.fill`, `brazilianrealsign.square.fill`, `cedisign.circle.fill`, `cedisign.square.fill`, `coloncurrencysign.circle.fill`, `coloncurrencysign.square.fill`, `cruzeirosign.circle.fill`, `cruzeirosign.square.fill`, `danishkronesign.circle.fill`, `danishkronesign.square.fill`, `dongsign.circle.fill`, `dongsign.square.fill`, `eurozonesign.circle.fill`, `eurozonesign.square.fill`, `florinsign.circle.fill`, `florinsign.square.fill`, `guaranisign.circle.fill`, `guaranisign.square.fill`, `hryvniasign.circle.fill`, `hryvniasign.square.fill`, `kipsign.circle.fill`, `kipsign.square.fill`, `malaysianringgitsign.circle.fill`, `malaysianringgitsign.square.fill`, `manatsign.circle.fill`, `manatsign.square.fill`, `millsign.circle.fill`, `millsign.square.fill`, `nairasign.circle.fill`, `nairasign.square.fill`, `norwegiankronesign.circle.fill`, `norwegiankronesign.square.fill`, `peruviansolessign.circle.fill`, `peruviansolessign.square.fill`, `pesetasign.circle.fill`, `pesetasign.square.fill`, `polishzlotysign.circle.fill`, `polishzlotysign.square.fill`, `shekelsign.circle.fill`, `shekelsign.square.fill`, `sheqelsign.circle.fill`, `sheqelsign.square.fill`, `swedishkronasign.circle.fill`, `swedishkronasign.square.fill`, `tengesign.circle.fill`, `tengesign.square.fill`, `tugriksign.circle.fill`, `tugriksign.square.fill`, `wonsign.circle.fill`, `wonsign.square.fill`
- `person-standing` (51): `figure.american.football.circle.fill`, `figure.australian.football.circle.fill`, `figure.badminton.circle.fill`, `figure.barre.circle.fill`, `figure.baseball.circle.fill`, `figure.basketball.circle.fill`, `figure.bowling.circle.fill`, `figure.cooldown.circle.fill`, `figure.cricket.circle.fill`, `figure.dance.circle.fill`, `figure.elliptical.circle.fill`, `figure.equestrian.sports.circle.fill`, `figure.fall.circle.fill`, `figure.field.hockey.circle.fill`, `figure.flexibility.circle.fill`, `figure.gymnastics.circle.fill`, `figure.handball.circle.fill`, `figure.hockey.circle.fill`, `figure.ice.hockey.circle.fill`, `figure.ice.skating.circle.fill`, `figure.indoor.rowing.circle.fill`, `figure.jumprope.circle.fill`, `figure.lacrosse.circle.fill`, `figure.outdoor.rowing.circle.fill`, `figure.pickleball.circle.fill`, `figure.play.circle.fill`, `figure.racquetball.circle.fill`, `figure.rugby.circle.fill`, `figure.seated.seatbelt.left.drive.seats.1.1.fill`, `figure.seated.seatbelt.left.drive.seats.1.2.fill`, `figure.seated.seatbelt.left.drive.seats.1.fill`, `figure.seated.seatbelt.left.drive.seats.2.2.2.fill`, `figure.seated.seatbelt.left.drive.seats.2.2.3.fill`, `figure.seated.seatbelt.left.drive.seats.2.2.fill`, `figure.seated.seatbelt.left.drive.seats.2.3.2.fill`, `figure.seated.seatbelt.left.drive.seats.2.3.3.fill`, `figure.seated.seatbelt.left.drive.seats.2.3.fill`, `figure.seated.seatbelt.left.drive.seats.2.fill`, `figure.seated.seatbelt.left.drive.seats.3.3.3.fill`, `figure.seated.seatbelt.left.drive.seats.3.3.fill`, `figure.seated.seatbelt.left.drive.seats.3.fill`, `figure.skateboarding.circle.fill`, `figure.socialdance.circle.fill`, `figure.softball.circle.fill`, `figure.squash.circle.fill`, `figure.stair.stepper.circle.fill`, `figure.stairs.circle.fill`, `figure.step.training.circle.fill`, `figure.table.tennis.circle.fill`, `figure.tennis.circle.fill`, `figure.wave.circle.fill`
- `landmark` (43): `australiandollarsign.bank.building.fill`, `australsign.bank.building.fill`, `bahtsign.bank.building.fill`, `bitcoinsign.bank.building.fill`, `brazilianrealsign.bank.building.fill`, `cedisign.bank.building.fill`, `centsign.bank.building.fill`, `chineseyuanrenminbisign.bank.building.fill`, `coloncurrencysign.bank.building.fill`, `cruzeirosign.bank.building.fill`, `danishkronesign.bank.building.fill`, `dollarsign.bank.building.fill`, `dongsign.bank.building.fill`, `eurosign.bank.building.fill`, `eurozonesign.bank.building.fill`, `florinsign.bank.building.fill`, `francsign.bank.building.fill`, `guaranisign.bank.building.fill`, `hryvniasign.bank.building.fill`, `indianrupeesign.bank.building.fill`, `kipsign.bank.building.fill`, `larisign.bank.building.fill`, `lirasign.bank.building.fill`, `malaysianringgitsign.bank.building.fill`, `manatsign.bank.building.fill`, `millsign.bank.building.fill`, `nairasign.bank.building.fill`, `norwegiankronesign.bank.building.fill`, `peruviansolessign.bank.building.fill`, `pesetasign.bank.building.fill`, `pesosign.bank.building.fill`, `polishzlotysign.bank.building.fill`, `rublesign.bank.building.fill`, `rupeesign.bank.building.fill`, `shekelsign.bank.building.fill`, `singaporedollarsign.bank.building.fill`, `sterlingsign.bank.building.fill`, `swedishkronasign.bank.building.fill`, `tengesign.bank.building.fill`, `tugriksign.bank.building.fill`, `turkishlirasign.bank.building.fill`, `wonsign.bank.building.fill`, `yensign.bank.building.fill`
- `speaker` (39): `hifispeaker.2.badge.minus.fill`, `hifispeaker.2.badge.plus.fill`, `hifispeaker.2.fill`, `hifispeaker.and.appletv.fill`, `hifispeaker.and.homepod.badge.minus.fill`, `hifispeaker.and.homepod.badge.plus.fill`, `hifispeaker.and.homepod.fill`, `hifispeaker.and.homepod.mini.badge.minus.fill`, `hifispeaker.and.homepod.mini.badge.plus.fill`, `hifispeaker.and.homepod.mini.fill`, `hifispeaker.and.homepodmini.fill`, `hifispeaker.arrow.forward.fill`, `hifispeaker.badge.minus.fill`, `hifispeaker.badge.plus.fill`, `homepod.2.badge.minus.fill`, `homepod.2.badge.plus.fill`, `homepod.2.fill`, `homepod.and.appletv.fill`, `homepod.and.homepod.mini.badge.minus.fill`, `homepod.and.homepod.mini.badge.plus.fill`, `homepod.and.homepod.mini.fill`, `homepod.and.homepodmini.fill`, `homepod.arrow.forward.fill`, `homepod.badge.checkmark.fill`, `homepod.badge.minus.fill`, `homepod.badge.plus.fill`, `homepod.fill`, `homepod.mini.2.badge.minus.fill`, `homepod.mini.2.badge.plus.fill`, `homepod.mini.2.fill`, `homepod.mini.and.appletv.fill`, `homepod.mini.arrow.forward.fill`, `homepod.mini.badge.checkmark.fill`, `homepod.mini.badge.minus.fill`, `homepod.mini.badge.plus.fill`, `homepod.mini.fill`, `homepodmini.2.fill`, `homepodmini.and.appletv.fill`, `homepodmini.fill`
- `plug` (31): `poweroutlet.strip.fill`, `poweroutlet.type.a.fill`, `poweroutlet.type.a.square.fill`, `poweroutlet.type.b.fill`, `poweroutlet.type.b.square.fill`, `poweroutlet.type.c.fill`, `poweroutlet.type.c.square.fill`, `poweroutlet.type.d.fill`, `poweroutlet.type.d.square.fill`, `poweroutlet.type.e.fill`, `poweroutlet.type.e.square.fill`, `poweroutlet.type.f.fill`, `poweroutlet.type.f.square.fill`, `poweroutlet.type.g.fill`, `poweroutlet.type.g.square.fill`, `poweroutlet.type.h.fill`, `poweroutlet.type.h.square.fill`, `poweroutlet.type.i.fill`, `poweroutlet.type.i.square.fill`, `poweroutlet.type.j.fill`, `poweroutlet.type.j.square.fill`, `poweroutlet.type.k.fill`, `poweroutlet.type.k.square.fill`, `poweroutlet.type.l.fill`, `poweroutlet.type.l.square.fill`, `poweroutlet.type.m.fill`, `poweroutlet.type.m.square.fill`, `poweroutlet.type.n.fill`, `poweroutlet.type.n.square.fill`, `poweroutlet.type.o.fill`, `poweroutlet.type.o.square.fill`
- `armchair` (23): `carseat.left.1.fill`, `carseat.left.2.fill`, `carseat.left.3.fill`, `carseat.left.and.heat.waves.fill`, `carseat.left.backrest.up.and.down.fill`, `carseat.left.fan.fill`, `carseat.left.fill`, `carseat.left.forward.and.backward.fill`, `carseat.left.massage.fill`, `carseat.left.up.and.down.fill`, `carseat.right.1.fill`, `carseat.right.2.fill`, `carseat.right.3.fill`, `carseat.right.and.heat.waves.fill`, `carseat.right.backrest.up.and.down.fill`, `carseat.right.fan.fill`, `carseat.right.fill`, `carseat.right.forward.and.backward.fill`, `carseat.right.massage.fill`, `carseat.right.up.and.down.fill`, `chair.fill`, `chair.lounge.fill`, `table.furniture.fill`
- `headphones` (20): `airpods.chargingcase.fill`, `airpods.chargingcase.wireless.fill`, `airpods.gen3.chargingcase.wireless.fill`, `airpods.gen4.chargingcase.wireless.fill`, `airpods.pro.chargingcase.wireless.fill`, `airpods.pro.chargingcase.wireless.radiowaves.left.and.right.fill`, `airpodspro.chargingcase.wireless.fill`, `airpodspro.chargingcase.wireless.radiowaves.left.and.right.fill`, `beats.fit.pro.chargingcase.fill`, `beats.fitpro.chargingcase.fill`, `beats.pill.fill`, `beats.powerbeats.pro.2.chargingcase.fill`, `beats.powerbeats.pro.chargingcase.fill`, `beats.powerbeatspro.chargingcase.fill`, `beats.solobuds.chargingcase.fill`, `beats.studiobuds.chargingcase.fill`, `beats.studiobuds.plus.chargingcase.fill`, `beats.studiobudsplus.chargingcase.fill`, `earbuds.case.fill`, `headphones.sensor.tag.radiowaves.left.and.right.fill`
- `truck` (20): `box.truck.badge.clock.fill`, `truck.box.badge.clock.fill`, `truck.pickup.side.air.circulate.fill`, `truck.pickup.side.air.fresh.fill`, `truck.pickup.side.and.exclamationmark.fill`, `truck.pickup.side.arrow.left.and.right.fill`, `truck.pickup.side.arrowtriangle.down.fill`, `truck.pickup.side.arrowtriangle.up.arrowtriangle.down.fill`, `truck.pickup.side.arrowtriangle.up.fill`, `truck.pickup.side.fill`, `truck.pickup.side.front.open.crop.fill`, `truck.pickup.side.front.open.fill`, `truck.pickup.side.hill.down.and.gauge.open.with.lines.needle.25percent.and.arrowtriangle.fill`, `truck.pickup.side.hill.down.fill`, `truck.pickup.side.hill.up.fill`, `truck.pickup.side.lock.fill`, `truck.pickup.side.lock.open.fill`, `truck.side.hill.descent.control.fill`, `truck.side.roof.cargo.carrier.fill`, `truck.side.roof.cargo.carrier.slash.fill`
- `volleyball` (20): `american.football.circle.fill`, `american.football.fill`, `american.football.professional.circle.fill`, `american.football.professional.fill`, `australian.football.circle.fill`, `australian.football.fill`, `baseball.circle.fill`, `baseball.fill`, `basketball.circle.fill`, `basketball.fill`, `cricket.ball.circle.fill`, `cricket.ball.fill`, `figure.volleyball.circle.fill`, `football.circle.fill`, `football.fill`, `rugbyball.circle.fill`, `rugbyball.fill`, `tennis.racket.circle.fill`, `tennisball.circle.fill`, `tennisball.fill`
- `copy` (18): `h.square.fill.on.square.fill`, `h.square.on.square.fill`, `j.square.fill.on.square.fill`, `j.square.on.square.fill`, `r.square.fill.on.square.fill`, `r.square.on.square.fill`, `rectangle.fill.on.rectangle.fill`, `rectangle.fill.on.rectangle.fill.circle.fill`, `rectangle.fill.on.rectangle.fill.slash.fill`, `rectangle.on.rectangle.button.angledtop.vertical.left.fill`, `rectangle.on.rectangle.circle.fill`, `rectangle.on.rectangle.slash.circle.fill`, `rectangle.on.rectangle.slash.fill`, `rectangle.on.rectangle.square.fill`, `rectangle.portrait.on.rectangle.portrait.angled.fill`, `rectangle.portrait.on.rectangle.portrait.fill`, `rectangle.portrait.on.rectangle.portrait.slash.fill`, `square.on.square.badge.person.crop.fill`
- `joystick` (18): `l.button.roundedbottom.horizontal.fill`, `l.joystick.down.fill`, `l.joystick.fill`, `l.joystick.press.down.fill`, `l.joystick.tilt.down.fill`, `l.joystick.tilt.left.fill`, `l.joystick.tilt.right.fill`, `l.joystick.tilt.up.fill`, `l.rectangle.roundedbottom.fill`, `r.button.roundedbottom.horizontal.fill`, `r.joystick.down.fill`, `r.joystick.fill`, `r.joystick.press.down.fill`, `r.joystick.tilt.down.fill`, `r.joystick.tilt.left.fill`, `r.joystick.tilt.right.fill`, `r.joystick.tilt.up.fill`, `r.rectangle.roundedbottom.fill`
- `lightbulb` (16): `auto.headlight.high.beam.fill`, `auto.headlight.low.beam.fill`, `automatic.headlight.high.beam.fill`, `automatic.headlight.low.beam.fill`, `headlight.daytime.fill`, `headlight.fog.fill`, `headlight.high.beam.fill`, `headlight.low.beam.fill`, `lightbulb.2.fill`, `lightbulb.led.fill`, `lightbulb.led.wide.fill`, `lightbulb.max.fill`, `lightbulb.min.badge.exclamationmark.fill`, `lightbulb.min.fill`, `parkinglight.fill`, `taillight.fog.fill`
- `smartphone` (13): `arrow.turn.up.forward.iphone.fill`, `circle.filled.iphone.fill`, `iphone.gen1.circle.fill`, `iphone.gen1.radiowaves.left.and.right.circle.fill`, `iphone.gen1.slash.circle.fill`, `iphone.gen2.circle.fill`, `iphone.gen2.radiowaves.left.and.right.circle.fill`, `iphone.gen2.slash.circle.fill`, `iphone.gen3.circle.fill`, `iphone.gen3.radiowaves.left.and.right.circle.fill`, `iphone.gen3.slash.circle.fill`, `iphone.homebutton.slash.circle.fill`, `iphone.slash.circle.fill`
- `grid-3x3` (11): `grid.circle.fill`, `square.grid.3x3.bottomleft.fill`, `square.grid.3x3.bottommiddle.fill`, `square.grid.3x3.bottomright.fill`, `square.grid.3x3.middle.fill`, `square.grid.3x3.middleleft.fill`, `square.grid.3x3.middleright.fill`, `square.grid.3x3.topleft.fill`, `square.grid.3x3.topmiddle.fill`, `square.grid.3x3.topright.fill`, `square.grid.4x3.fill`
- `lamp-ceiling` (11): `chandelier.fill`, `light.beacon.max.fill`, `light.beacon.min.fill`, `light.cylindrical.ceiling.fill`, `light.overhead.left.fill`, `light.overhead.right.fill`, `light.panel.fill`, `light.recessed.3.fill`, `light.recessed.fill`, `light.ribbon.fill`, `light.strip.2.fill`
- `users` (11): `figure.2.circle.fill`, `person.2.badge.fill`, `person.2.badge.gearshape.fill`, `person.2.badge.key.fill`, `person.2.badge.minus.fill`, `person.2.badge.plus.fill`, `person.2.slash.fill`, `person.2.wave.2.fill`, `person.3.fill`, `person.3.sequence.fill`, `person.line.dotted.person.fill`
- `layers` (10): `square.3.stack.3d.bottom.fill`, `square.3.stack.3d.middle.fill`, `square.3.stack.3d.top.fill`, `square.stack.3d.down.forward.fill`, `square.stack.3d.down.right.fill`, `square.stack.3d.forward.dottedline.fill`, `square.stack.3d.up.badge.a.fill`, `square.stack.3d.up.badge.automatic.fill`, `square.stack.3d.up.slash.fill`, `square.stack.3d.up.trianglebadge.exclamationmark.fill`
- `message-square` (10): `bubble.middle.top.fill`, `info.bubble.fill`, `person.bubble.fill`, `phone.bubble.fill`, `phone.bubble.left.fill`, `speaker.wave.2.bubble.fill`, `speaker.wave.2.bubble.left.fill`, `star.bubble.fill`, `video.bubble.fill`, `video.bubble.left.fill`
- `pc-case` (10): `macmini.badge.checkmark.fill`, `macmini.fill`, `macmini.gen2.fill`, `macmini.gen3.fill`, `macpro.gen1.fill`, `macpro.gen2.fill`, `macpro.gen3.badge.ckeckmark.fill`, `macpro.gen3.fill`, `macstudio.badge.checkmark.fill`, `macstudio.fill`
- `rss` (10): `wave.3.backward.circle.fill`, `wave.3.down.car.side.fill`, `wave.3.down.circle.fill`, `wave.3.down.convertible.side.fill`, `wave.3.down.pickup.side.fill`, `wave.3.down.suv.side.fill`, `wave.3.forward.circle.fill`, `wave.3.left.circle.fill`, `wave.3.right.circle.fill`, `wave.3.up.circle.fill`
- `car-battery` (9): `batteryblock.stack.badge.snowflake.fill`, `batteryblock.stack.fill`, `batteryblock.stack.trianglebadge.exclamationmark.fill`, `minus.plus.batteryblock.exclamationmark.fill`, `minus.plus.batteryblock.stack.arrowtriangle.left.fill`, `minus.plus.batteryblock.stack.arrowtriangle.right.and.arrowtriangle.left.fill`, `minus.plus.batteryblock.stack.arrowtriangle.right.fill`, `minus.plus.batteryblock.stack.exclamationmark.fill`, `minus.plus.batteryblock.stack.fill`
- `footprints` (8): `figure.run.circle.fill`, `figure.run.square.stack.fill`, `figure.run.treadmill.circle.fill`, `figure.walk.circle.fill`, `figure.walk.treadmill.circle.fill`, `pedal.accelerator.fill`, `pedal.brake.fill`, `pedal.clutch.fill`
- `hexagon` (8): `circle.grid.hex.fill`, `circle.hexagongrid.circle.fill`, `circle.hexagongrid.fill`, `circle.hexagonpath.fill`, `circles.hexagongrid.fill`, `circles.hexagonpath.fill`, `spatial.capture.on.hexagon.fill`, `star.hexagon.fill`
- `house` (8): `house.and.flag.circle.fill`, `house.and.flag.fill`, `house.lodge.circle.fill`, `house.lodge.fill`, `house.slash.fill`, `music.house.fill`, `music.note.house.fill`, `play.house.fill`
- `key` (8): `key.car.radiowaves.forward.fill`, `key.car.side.fill`, `key.convertible.side.fill`, `key.radiowaves.forward.fill`, `key.radiowaves.forward.slash.fill`, `key.sensor.tag.radiowaves.left.and.right.fill`, `key.suv.side.fill`, `key.truck.pickup.side.fill`
- `plug-zap` (8): `ev.plug.ac.gb.t.fill`, `ev.plug.ac.type.1.fill`, `ev.plug.ac.type.2.fill`, `ev.plug.dc.ccs1.fill`, `ev.plug.dc.ccs2.fill`, `ev.plug.dc.chademo.fill`, `ev.plug.dc.gb.t.fill`, `ev.plug.dc.nacs.fill`
- `rectangle-goggles` (8): `vision.pro.and.arrow.forward.fill`, `vision.pro.badge.checkmark.fill`, `vision.pro.badge.exclamationmark.fill`, `vision.pro.badge.play.fill`, `vision.pro.trianglebadge.exclamationmark.fill`, `visionpro.and.arrow.forward.fill`, `visionpro.badge.exclamationmark.fill`, `visionpro.badge.play.fill`
- `rectangle-vertical` (8): `appletvremote.gen1.fill`, `appletvremote.gen2.fill`, `appletvremote.gen3.fill`, `appletvremote.gen4.fill`, `av.remote.fill`, `capsule.portrait.fill`, `rectangle.portrait.badge.plus.fill`, `rectangle.portrait.slash.fill`
- `route` (8): `point.bottomleft.forward.to.arrow.triangle.scurvepath.fill`, `point.bottomleft.forward.to.arrow.triangle.uturn.scurvepath.fill`, `point.bottomleft.forward.to.arrowtriangle.uturn.scurvepath.fill`, `point.bottomleft.forward.to.point.topright.scurvepath.fill`, `point.forward.to.point.capsulepath.fill`, `point.topleft.down.curvedto.point.bottomright.up.fill`, `point.topleft.down.to.point.bottomright.curvepath.fill`, `point.topright.arrow.triangle.backward.to.point.bottomleft.scurvepath.fill`
- `circle-user-round` (7): `person.crop.circle.badge.clock.fill`, `person.crop.circle.badge.ellipsis.fill`, `person.crop.circle.badge.exclamationmark.fill`, `person.crop.circle.badge.fill`, `person.crop.circle.badge.moon.fill`, `person.crop.circle.badge.questionmark.fill`, `person.crop.circle.dashed.circle.fill`
- `contact` (7): `inset.filled.rectangle.and.person.filled.circle.fill`, `person.2.crop.square.stack.fill`, `person.2.square.stack.fill`, `person.crop.rectangle.badge.plus.fill`, `person.crop.rectangle.stack.fill`, `person.crop.square.filled.and.at.rectangle.fill`, `rectangle.stack.person.crop.fill`
- `droplets` (7): `dehumidifier.fill`, `humidifier.and.droplets.fill`, `humidifier.and.ellipsis.fill`, `humidifier.fill`, `humidity.fill`, `sprinkler.and.droplets.fill`, `sprinkler.fill`
- `luggage` (7): `duffle.bag.fill`, `figure.walk.suitcase.rolling.circle.fill`, `gym.bag.fill`, `suitcase.rolling.and.film.circle.fill`, `suitcase.rolling.and.film.fill`, `suitcase.rolling.and.suitcase.circle.fill`, `suitcase.rolling.and.suitcase.fill`
- `mail` (7): `envelope.and.arrow.3.down.fill`, `envelope.and.arrow.trianglehead.branch.fill`, `envelope.and.hand.raised.fill`, `envelope.arrow.triangle.branch.fill`, `envelope.badge.person.crop.fill`, `envelope.badge.shield.half.filled.fill`, `envelope.badge.shield.leadinghalf.fill`
- `sunrise` (7): `moonrise.circle.fill`, `moonrise.fill`, `sun.and.horizon.circle.fill`, `sun.and.horizon.fill`, `sun.horizon.circle.fill`, `sun.horizon.fill`, `wake.circle.fill`
- `user` (7): `person.badge.clock.fill`, `person.bust.fill`, `person.spatialaudio.3d.fill`, `person.spatialaudio.fill`, `person.spatialaudio.stereo.3d.fill`, `person.spatialaudio.stereo.fill`, `person.wave.2.fill`
- `columns-2` (6): `rectangle.portrait.split.2x1.fill`, `rectangle.portrait.split.2x1.slash.fill`, `rectangle.split.2x1.slash.fill`, `square.and.line.vertical.and.square.fill`, `square.fill.and.line.vertical.and.square.fill`, `square.fill.and.line.vertical.square.fill`
- `flower-2` (6): `apple.meditate.circle.fill`, `apple.meditate.square.stack.fill`, `figure.mind.and.body.circle.fill`, `figure.pilates.circle.fill`, `figure.taichi.circle.fill`, `figure.yoga.circle.fill`
- `gauge` (6): `dial.fill`, `dial.high.fill`, `dial.low.fill`, `dial.max.fill`, `dial.medium.fill`, `dial.min.fill`
- `grip` (6): `circle.grid.2x2.fill`, `circle.grid.cross.down.fill`, `circle.grid.cross.fill`, `circle.grid.cross.left.fill`, `circle.grid.cross.right.fill`, `circle.grid.cross.up.fill`
- `heart` (6): `arrow.clockwise.heart.fill`, `arrow.down.heart.fill`, `arrow.trianglehead.clockwise.heart.fill`, `arrow.up.heart.fill`, `heart.rectangle.fill`, `lock.heart.fill`
- `lock` (6): `exclamationmark.lock.fill`, `lock.rectangle.fill`, `lock.rectangle.on.rectangle.fill`, `lock.rectangle.stack.fill`, `lock.square.stack.fill`, `tsa.circle.fill`
- `moon` (6): `moon.dust.circle.fill`, `moon.dust.fill`, `moon.haze.circle.fill`, `moon.haze.fill`, `moon.zzz.fill`, `sleep.circle.fill`
- `mountain` (6): `environments.circle.fill`, `environments.fill`, `environments.slash.circle.fill`, `environments.slash.fill`, `figure.climbing.circle.fill`, `figure.hiking.circle.fill`
- `pointer` (6): `hand.point.down.fill`, `hand.point.left.fill`, `hand.point.right.fill`, `hand.point.up.braille.badge.ellipsis.fill`, `hand.point.up.braille.fill`, `hand.point.up.left.and.text.fill`
- `share` (6): `square.and.arrow.up.badge.checkmark.fill`, `square.and.arrow.up.badge.clock.fill`, `square.and.arrow.up.circle.fill`, `square.and.arrow.up.on.square.fill`, `square.and.arrow.up.trianglebadge.exclamationmark.fill`, `tray.and.arrow.up.fill`
- `signpost` (6): `arrow.triangle.turn.up.right.diamond.fill`, `arrow.trianglehead.turn.up.right.diamond.fill`, `arrow.up.right.diamond.fill`, `figure.walk.diamond.fill`, `signpost.and.arrowtriangle.up.circle.fill`, `signpost.and.arrowtriangle.up.fill`
- `tv` (6): `4k.tv.fill`, `music.note.tv.fill`, `sparkles.tv.fill`, `tv.and.mediabox.fill`, `tv.badge.wifi.fill`, `tv.music.note.fill`
- `watch` (6): `digitalcrown.arrow.clockwise.fill`, `digitalcrown.arrow.counterclockwise.fill`, `digitalcrown.horizontal.arrow.clockwise.fill`, `digitalcrown.horizontal.arrow.counterclockwise.fill`, `digitalcrown.horizontal.press.fill`, `digitalcrown.press.fill`
- `waves-horizontal` (6): `figure.open.water.swim.circle.fill`, `figure.pool.swim.circle.fill`, `figure.surfing.circle.fill`, `figure.water.fitness.circle.fill`, `figure.waterpolo.circle.fill`, `surfboard.fill`
- `cloud` (5): `carbon.dioxide.cloud.fill`, `carbon.monoxide.cloud.fill`, `key.icloud.fill`, `lock.icloud.fill`, `person.icloud.fill`
- `credit-card` (5): `creditcard.rewards.fill`, `electronic.toll.collection.rectangle.fill`, `electronic.toll.collection.rectangle.slash.fill`, `electronic.toll.collection.rectangle.trianglebadge.exclamationmark.fill`, `tram.card.fill`
- `droplet` (5): `drop.degreesign.fill`, `drop.keypad.rectangle.fill`, `drop.triangle.fill`, `oilcan.fill`, `pipe.and.drop.fill`
- `flag` (5): `flag.2.crossed.circle.fill`, `flag.2.crossed.fill`, `flag.badge.ellipsis.fill`, `flag.checkered.circle.fill`, `flag.pattern.checkered.circle.fill`
- `hand` (5): `hand.app.fill`, `hand.pinch.fill`, `hand.raised.square.on.square.fill`, `hand.rays.fill`, `hands.clap.fill`
- `heart-pulse` (5): `blood.pressure.cuff.badge.gauge.with.needle.fill`, `blood.pressure.cuff.fill`, `bolt.heart.fill`, `figure.mixed.cardio.circle.fill`, `heart.badge.bolt.fill`
- `keyboard` (5): `keyboard.chevron.compact.left.fill`, `keyboard.onehanded.left.fill`, `keyboard.onehanded.right.fill`, `teletype.answer.circle.fill`, `teletype.circle.fill`
- `mountain-snow` (5): `figure.skiing.crosscountry.circle.fill`, `figure.skiing.downhill.circle.fill`, `figure.snowboarding.circle.fill`, `skis.fill`, `snowboard.fill`
- `picture-in-picture-2` (5): `rectangle.center.inset.fill`, `rectangle.inset.bottomleft.fill`, `rectangle.inset.fill`, `rectangle.inset.topleft.fill`, `rectangle.inset.topright.fill`
- `shirt` (5): `coat.circle.fill`, `coat.fill`, `jacket.circle.fill`, `jacket.fill`, `jacket.sensor.tag.radiowaves.left.and.right.fill`
- `triangle` (5): `trapezoid.and.line.horizontal.fill`, `trapezoid.and.line.vertical.fill`, `triangle.lefthalf.fill`, `triangle.righthalf.fill`, `yieldsign.fill`
- `video` (5): `arrow.down.left.video.fill`, `arrow.up.right.video.fill`, `questionmark.video.fill`, `video.and.waveform.fill`, `video.badge.waveform.fill`
- `wind` (5): `air.car.side.fill`, `air.convertible.side.fill`, `air.pickup.side.fill`, `air.suv.side.fill`, `lungs.fill`
- `baby` (4): `figure.child.and.lock.fill`, `figure.child.and.lock.open.fill`, `figure.child.circle.fill`, `stroller.fill`
- `calendar-range` (4): `calendar.day.timeline.leading.circle.fill`, `calendar.day.timeline.left.circle.fill`, `calendar.day.timeline.right.circle.fill`, `calendar.day.timeline.trailing.circle.fill`
- `cloud-sync` (4): `arrow.clockwise.icloud.fill`, `arrow.counterclockwise.icloud.fill`, `arrow.trianglehead.clockwise.icloud.fill`, `arrow.trianglehead.counterclockwise.icloud.fill`
- `disc` (4): `figure.disc.sports.circle.fill`, `hockey.puck.circle.fill`, `hockey.puck.fill`, `opticaldiscdrive.fill`
- `file-text` (4): `doc.richtext.fill`, `info.circle.text.page.fill`, `richtext.page.fill`, `text.rectangle.page.fill`
- `fuel` (4): `fuelpump.arrowtriangle.left.fill`, `fuelpump.arrowtriangle.right.fill`, `fuelpump.exclamationmark.fill`, `fuelpump.thermometer.fill`
- `gallery-vertical-end` (4): `rectangle.stack.badge.person.crop.fill`, `rectangle.stack.badge.play.fill`, `rectangle.stack.slash.fill`, `sparkles.rectangle.stack.fill`
- `gamepad-directional` (4): `dpad.down.fill`, `dpad.left.fill`, `dpad.right.fill`, `dpad.up.fill`
- `hand-fist` (4): `figure.boxing.circle.fill`, `figure.kickboxing.circle.fill`, `figure.martial.arts.circle.fill`, `figure.wrestling.circle.fill`
- `import` (4): `square.and.arrow.down.badge.checkmark.fill`, `square.and.arrow.down.badge.clock.fill`, `square.and.arrow.down.badge.xmark.fill`, `square.and.arrow.down.on.square.fill`
- `inbox` (4): `tray.2.fill`, `tray.and.arrow.down.fill`, `tray.badge.fill`, `tray.full.fill`
- `message-circle` (4): `arrow.down.message.fill`, `arrow.up.message.fill`, `message.and.waveform.fill`, `message.badge.waveform.fill`
- `messages-square` (4): `bubble.left.and.exclamationmark.bubble.right.fill`, `rectangle.3.group.bubble.fill`, `rectangle.3.group.bubble.left.fill`, `rectangle.3.offgrid.bubble.left.fill`
- `pen-tool` (4): `pencil.tip.crop.circle.badge.arrow.forward.fill`, `pencil.tip.crop.circle.badge.minus.fill`, `pencil.tip.crop.circle.badge.plus.fill`, `pencil.tip.crop.circle.fill`
- `ratio` (4): `rectangle.ratio.16.to.9.fill`, `rectangle.ratio.3.to.4.fill`, `rectangle.ratio.4.to.3.fill`, `rectangle.ratio.9.to.16.fill`
- `scan` (4): `field.of.view.ultrawide.fill`, `field.of.view.wide.fill`, `spatial.capture.fill`, `spatial.capture.slash.fill`
- `shapes` (4): `circle.fill.square.fill`, `circle.square.fill`, `square.circle.fill`, `xmark.triangle.circle.square.fill`
- `share-2` (4): `point.3.connected.trianglepath.dotted`, `point.3.filled.connected.trianglepath.dotted`, `shared.with.you.circle.fill`, `sharedwithyou.circle.fill`
- `sport-shoe` (4): `shoe.2.fill`, `shoe.arrow.trianglehead.up.and.down.fill`, `shoe.arrow.trianglehead.up.right.circle.fill`, `shoe.arrow.trianglehead.up.right.fill`
- `toggle-right` (4): `lightswitch.on.fill`, `lightswitch.on.square.fill`, `switch.programmable.fill`, `switch.programmable.square.fill`
- `undo-2` (4): `arrow.uturn.down.circle.fill`, `arrow.uturn.down.square.fill`, `arrow.uturn.up.circle.fill`, `arrow.uturn.up.square.fill`
- `badge-question-mark` (3): `questionmark.app.fill`, `questionmark.diamond.fill`, `questionmark.square.fill`
- `clipboard` (3): `heart.text.clipboard.fill`, `sparkle.text.clipboard.fill`, `waveform.path.ecg.text.clipboard.fill`
- `cooking-pot` (3): `cooktop.fill`, `frying.pan.fill`, `stove.fill`
- `ev-charger` (3): `ev.charger.arrowtriangle.left.fill`, `ev.charger.arrowtriangle.right.fill`, `ev.charger.exclamationmark.fill`
- `file-heart` (3): `doc.text.below.ecg.fill`, `ecg.text.page.fill`, `waveform.path.ecg.text.page.fill`
- `gamepad-2` (3): `formfitting.gamecontroller.fill`, `paddleshifter.left.fill`, `paddleshifter.right.fill`
- `goal` (3): `figure.indoor.soccer.circle.fill`, `figure.outdoor.soccer.circle.fill`, `soccerball.circle.fill`
- `hard-hat` (3): `hat.cap.fill`, `hat.widebrim.fill`, `helmet.fill`
- `image` (3): `apple.image.playground.fill`, `pano.fill`, `text.below.photo.fill`
- `link` (3): `link.icloud.fill`, `tow.hitch.exclamationmark.fill`, `tow.hitch.fill`
- `message-square-text` (3): `character.bubble.fill`, `t.bubble.fill`, `text.bubble.badge.clock.fill`
- `play` (3): `play.diamond.fill`, `play.rectangle.fill`, `playpause.fill`
- `radio` (3): `sensor.fill`, `sensor.radiowaves.left.and.right.fill`, `sensor.tag.radiowaves.forward.fill`
- `shield` (3): `bolt.shield.fill`, `facemask.fill`, `key.shield.fill`
- `shield-user` (3): `person.2.shield.fill`, `person.badge.shield.checkmark.fill`, `person.badge.shield.exclamationmark.fill`
- `square-check` (3): `checkmark.rectangle.fill`, `checkmark.rectangle.portrait.fill`, `checkmark.rectangle.stack.fill`
- `square-plus` (3): `plus.rectangle.fill`, `plus.rectangle.portrait.fill`, `square.badge.plus.fill`
- `square-user-round` (3): `person.crop.square.badge.camera.fill`, `person.crop.square.badge.video.fill`, `person.crop.square.on.square.angled.fill`
- `squircle` (3): `app.badge.checkmark.fill`, `app.badge.clock.fill`, `app.badge.fill`
- `triangle-alert` (3): `figure.walk.triangle.fill`, `hazardsign.fill`, `info.triangle.fill`
- `tv-minimal` (3): `appletv.badge.checkmark.fill`, `appletv.badge.exclamationmark.fill`, `appletv.fill`
- `volume` (3): `speaker.badge.exclamationmark.fill`, `speaker.trianglebadge.exclamationmark.fill`, `speaker.zzz.fill`
- `wrench` (3): `screwdriver.fill`, `wrench.and.screwdriver`, `wrench.and.screwdriver.fill`
- `zap` (3): `bolt.horizontal.circle.fill`, `bolt.horizontal.fill`, `bolt.trianglebadge.exclamationmark.fill`
- `accessibility` (2): `figure.roll.runningpace.circle.fill`, `figure.rolling.circle.fill`
- `airplay` (2): `airplay.audio.circle.fill`, `airplayaudio.circle.fill`
- `asterisk` (2): `staroflife.circle.fill`, `staroflife.fill`
- `atom` (2): `hydrogen.circle.fill`, `hydrogen.square.fill`
- `badge-cent` (2): `centsign.circle.fill`, `centsign.square.fill`
- `battery-charging` (2): `bolt.batteryblock.fill`, `bolt.fill.batteryblock.fill`
- `bell-off` (2): `bell.badge.slash.fill`, `bell.badge.waveform.slash.fill`
- `bell-ring` (2): `bell.and.waveform.fill`, `bell.badge.waveform.fill`
- `bike` (2): `bicycle.sensor.tag.radiowaves.left.and.right.fill`, `figure.hand.cycling.circle.fill`
- `book-open` (2): `apple.books.pages.fill`, `book.and.wrench.fill`
- `briefcase` (2): `briefcase.sensor.tag.radiowaves.left.and.right.fill`, `latch.2.case.fill`
- `camera` (2): `camera.on.rectangle.fill`, `camera.sensor.tag.radiowaves.left.and.right.fill`
- `car-front` (2): `car.rear.hazardsign.fill`, `car.rear.waves.up.fill`
- `card-sim` (2): `sdcard.fill`, `simcard.2.fill`
- `circle-arrow-out-down-left` (2): `arrow.down.backward.circle.fill`, `arrow.down.left.circle.fill`
- `circle-arrow-out-down-right` (2): `arrow.down.forward.circle.fill`, `arrow.down.right.circle.fill`
- `circle-arrow-out-up-left` (2): `arrow.up.backward.circle.fill`, `arrow.up.left.circle.fill`
- `circle-arrow-out-up-right` (2): `arrow.up.forward.circle.fill`, `arrow.up.right.circle.fill`
- `circle-dot` (2): `airtag.fill`, `airtag.radiowaves.forward.fill`
- `circle-gauge` (2): `kph.circle.fill`, `mph.circle.fill`
- `clipboard-list` (2): `chart.line.text.clipboard.fill`, `list.bullet.rectangle.portrait.fill`
- `cloud-hail` (2): `cloud.sleet.circle.fill`, `cloud.sleet.fill`
- `cloud-moon` (2): `cloud.moon.bolt.circle.fill`, `cloud.moon.bolt.fill`
- `cloud-sun` (2): `cloud.sun.bolt.circle.fill`, `cloud.sun.bolt.fill`
- `cloud-sun-rain` (2): `sun.rain.circle.fill`, `sun.rain.fill`
- `contrast` (2): `circle.bottomhalf.fill`, `circle.tophalf.fill`
- `corner-up-right` (2): `arrow.triangle.turn.up.right.circle.fill`, `arrow.trianglehead.turn.up.right.circle.fill`
- `diamond` (2): `checkmark.diamond.fill`, `xmark.diamond.fill`
- `door-closed-locked` (2): `entry.lever.keypad.fill`, `entry.lever.keypad.trianglebadge.exclamationmark.fill`
- `dumbbell` (2): `figure.core.training.circle.fill`, `figure.cross.training.circle.fill`
- `ear` (2): `hearingdevice.and.signal.meter.fill`, `hearingdevice.ear.fill`
- `face-slightly-smiling` (2): `mustache.fill`, `nose.fill`
- `fan` (2): `fan.and.light.ceiling.fill`, `fan.oscillation.fill`
- `file-input` (2): `append.page.fill`, `doc.append.fill`
- `flashlight` (2): `flashlight.on.circle.fill`, `flashlight.on.fill`
- `flower` (2): `allergens.fill`, `fleuron.fill`
- `fold-horizontal` (2): `arrowtriangle.right.and.line.vertical.and.arrowtriangle.left.fill`, `arrowtriangle.right.fill.and.line.vertical.and.arrowtriangle.left.fill`
- `folder` (2): `questionmark.folder.fill`, `text.below.folder.fill`
- `folder-plus` (2): `plus.rectangle.fill.on.folder.fill`, `plus.rectangle.on.folder.fill`
- `gallery-horizontal` (2): `play.rectangle.on.rectangle.circle.fill`, `play.rectangle.on.rectangle.fill`
- `gift` (2): `app.gift.fill`, `giftcard.fill`
- `grid-2x2` (2): `square.split.bottomrightquarter.fill`, `square.split.diagonal.2x2.fill`
- `haze` (2): `smoke.circle.fill`, `smoke.fill`
- `id-card` (2): `key.card.fill`, `person.text.rectangle.trianglebadge.exclamationmark.fill`
- `land-plot` (2): `sportscourt.circle.fill`, `sportscourt.fill`
- `layers-2` (2): `square.2.stack.3d.bottom.fill`, `square.2.stack.3d.top.fill`
- `layout-grid` (2): `mosaic.fill`, `rectangle.3.group.fill`
- `list-filter` (2): `line.2.horizontal.decrease.circle.fill`, `line.horizontal.2.decrease.circle.fill`
- `megaphone` (2): `horn.blast.fill`, `horn.fill`
- `mic` (2): `mic.and.signal.meter.fill`, `microphone.and.signal.meter.fill`
- `mic-vocal` (2): `music.mic.circle.fill`, `music.microphone.circle.fill`
- `mouse-pointer-2` (2): `pointer.arrow.ipad.square.fill`, `pointer.arrow.square.fill`
- `mouse-pointer-2-off` (2): `pointer.arrow.ipad.slash.square.fill`, `pointer.arrow.slash.square.fill`
- `panel-left` (2): `rectangle.lefthalf.fill`, `square.lefthalf.fill`
- `panel-right` (2): `rectangle.righthalf.fill`, `square.righthalf.fill`
- `panel-top` (2): `list.dash.header.rectangle.fill`, `square.tophalf.fill`
- `paw-print` (2): `pet.carrier.circle.fill`, `pet.carrier.fill`
- `phone` (2): `phone.pause.circle.fill`, `phone.pause.fill`
- `rectangle-ellipsis` (2): `123.rectangle.fill`, `text.rectangle.fill`
- `redo` (2): `arrowshape.bounce.forward.fill`, `arrowshape.bounce.right.fill`
- `robot-vacuum` (2): `robotic.vacuum.and.arrowtriangle.up.fill`, `robotic.vacuum.and.ellipsis.fill`
- `signature` (2): `hand.draw.badge.ellipsis.fill`, `hand.draw.fill`
- `siren` (2): `exclamationmark.warninglight.fill`, `warninglight.fill`
- `smartphone-nfc` (2): `iphone.homebutton.radiowaves.left.and.right.circle.fill`, `iphone.radiowaves.left.and.right.circle.fill`
- `sparkles` (2): `hands.and.sparkles.fill`, `hands.sparkles.fill`
- `square-arrow-out-down-left` (2): `arrow.down.backward.toptrailing.rectangle.fill`, `arrow.down.left.topright.rectangle.fill`
- `square-arrow-out-down-right` (2): `arrow.down.forward.topleading.rectangle.fill`, `arrow.down.right.topleft.rectangle.fill`
- `square-arrow-out-up-left` (2): `arrow.up.backward.bottomtrailing.rectangle.fill`, `arrow.up.left.bottomright.rectangle.fill`
- `square-arrow-out-up-right` (2): `arrow.up.forward.bottomleading.rectangle.fill`, `arrow.up.right.bottomleft.rectangle.fill`
- `square-minus` (2): `minus.rectangle.fill`, `minus.rectangle.portrait.fill`
- `square-mouse-pointer` (2): `cursorarrow.slash.square.fill`, `rectangle.and.hand.point.up.left.fill`
- `square-x` (2): `xmark.rectangle.fill`, `xmark.rectangle.portrait.fill`
- `sunset` (2): `moonset.circle.fill`, `moonset.fill`
- `table` (2): `table.badge.more.fill`, `tablecells.badge.ellipsis.fill`
- `tablet` (2): `circle.filled.ipad.fill`, `circle.filled.ipad.landscape.fill`
- `tablets` (2): `pills.circle.fill`, `pills.fill`
- `tent` (2): `tent.2.circle.fill`, `tent.2.fill`
- `thermometer` (2): `oilcan.and.thermometer.fill`, `thermometer.variable.and.figure.circle.fill`
- `thumbs-up` (2): `hand.thumbsdown.hand.thumbsup.fill`, `hand.thumbsup.slash.fill`
- `toggle-left` (2): `lightswitch.off.fill`, `lightswitch.off.square.fill`
- `tornado` (2): `hurricane.circle.fill`, `tropicalstorm.circle.fill`
- `trending-up` (2): `arrowshape.zigzag.forward.fill`, `arrowshape.zigzag.right.fill`
- `turtle` (2): `lizard.circle.fill`, `lizard.fill`
- `umbrella` (2): `umbrella.percent.fill`, `umbrella.sensor.tag.radiowaves.left.and.right.fill`
- `unfold-horizontal` (2): `arrowtriangle.left.and.line.vertical.and.arrowtriangle.right.fill`, `arrowtriangle.left.fill.and.line.vertical.and.arrowtriangle.right.fill`
- `washing-machine` (2): `dishwasher.circle.fill`, `dishwasher.fill`
- `air-vent` (1): `air.purifier.fill`
- `align-horizontal-space-around` (1): `distribute.horizontal.fill`
- `align-vertical-space-around` (1): `distribute.vertical.fill`
- `aperture` (1): `camera.shutter.button.fill`
- `app-window-mac` (1): `macwindow.on.rectangle`
- `apple` (1): `apple.logo`
- `archive` (1): `cabinet.fill`
- `archive-restore` (1): `arrow.up.trash.fill`
- `arrow-left-right` (1): `arrowshape.left.arrowshape.right.fill`
- `at-sign` (1): `square.and.at.rectangle.fill`
- `audio-waveform` (1): `shazam.logo.fill`
- `award` (1): `medal.star.fill`
- `backpack` (1): `backpack.sensor.tag.radiowaves.left.and.right.fill`
- `badge` (1): `burst.fill`
- `battery` (1): `magsafe.batterypack.fill`
- `binary` (1): `numbers.rectangle.fill`
- `bird` (1): `swift`
- `bow-arrow` (1): `figure.archery.circle.fill`
- `box` (1): `cube.transparent.fill`
- `brain` (1): `brain.head.profile.fill`
- `brush` (1): `comb.fill`
- `building-complex` (1): `building.2.crop.circle.fill`
- `bus-front` (1): `bus.doubledecker.fill`
- `cable` (1): `powercord.fill`
- `cctv` (1): `video.doorbell.fill`
- `chart-line` (1): `chart.line.flattrend.xyaxis.circle.fill`
- `circle-alert` (1): `abs.circle.fill`
- `circle-arrow-down` (1): `purchased.circle.fill`
- `circle-chevron-left` (1): `lessthanorequalto.circle.fill`
- `circle-chevron-right` (1): `greaterthanorequalto.circle.fill`
- `circle-dashed` (1): `circle.dashed.inset.fill`
- `circle-ellipsis` (1): `ellipsis.circle.badge.fill`
- `circle-play` (1): `playpause.circle.fill`
- `circle-pound-sterling` (1): `lirasign.circle.fill`
- `circle-small` (1): `circlebadge.2.fill`
- `circle-user` (1): `person.bust.circle.fill`
- `clapperboard` (1): `film.stack.fill`
- `clock` (1): `deskclock.fill`
- `cloud-lightning` (1): `bolt.horizontal.icloud.fill`
- `cloud-off` (1): `xmark.icloud.fill`
- `cloud-snow` (1): `wind.snow.circle.fill`
- `copy-plus` (1): `plus.rectangle.fill.on.rectangle.fill`
- `crosshair` (1): `figure.hunting.circle.fill`
- `cup-soda` (1): `takeoutbag.and.cup.and.straw.fill`
- `database` (1): `cylinder.split.1x2.fill`
- `diff` (1): `plus.minus.capsule.fill`
- `dog` (1): `service.dog.fill`
- `door-closed` (1): `contact.sensor.fill`
- `drama` (1): `theatermask.and.paintbrush.fill`
- `droplet-off` (1): `drop.degreesign.slash.fill`
- `earth` (1): `globe.desk.fill`
- `ellipse` (1): `oval.portrait.fill`
- `engine` (1): `engine.combustion.badge.exclamationmark.fill`
- `eraser` (1): `eraser.line.dashed.fill`
- `faucet` (1): `sink.fill`
- `file` (1): `document.badge.ellipsis.fill`
- `file-chart-column` (1): `chart.bar.horizontal.page.fill`
- `file-check` (1): `checkmark.seal.text.page.fill`
- `file-music` (1): `music.pages.fill`
- `file-pen-line` (1): `long.text.page.and.pencil.fill`
- `file-x` (1): `text.page.slash.fill`
- `flag-triangle-right` (1): `figure.golf.circle.fill`
- `flame` (1): `fireplace.fill`
- `folder-output` (1): `arrow.forward.folder.fill`
- `frame` (1): `photo.artframe.circle.fill`
- `git-commit-horizontal` (1): `circle.and.line.horizontal.fill`
- `globe` (1): `network`
- `grip-horizontal` (1): `circle.grid.2x1.fill`
- `handbag` (1): `handbag.sensor.tag.radiowaves.left.and.right.fill`
- `hard-drive` (1): `externaldrive.connected.to.line.below.fill`
- `hard-drive-upload` (1): `mount.fill`
- `heart-off` (1): `heart.badge.bolt.slash.fill`
- `heater` (1): `heat.waves.circle.fill`
- `image-play` (1): `pano.badge.play.fill`
- `images` (1): `rectangle.fill.on.rectangle.angled.fill`
- `info` (1): `info.square.fill`
- `key-round` (1): `key.2.on.ring.fill`
- `keyboard-off` (1): `keyboard.chevron.compact.down.fill`
- `layout-panel-left` (1): `squares.leading.rectangle.fill`
- `layout-template` (1): `square.grid.3x1.below.line.grid.1x2.fill`
- `library` (1): `books.vertical.circle.fill`
- `link-2` (1): `personalhotspot.circle.fill`
- `list` (1): `list.bullet.rectangle.fill`
- `list-music` (1): `music.note.square.stack.fill`
- `lock-open` (1): `lock.slash.fill`
- `mail-clock` (1): `envelope.open.badge.clock.fill`
- `menu` (1): `line.3.horizontal.button.angledtop.vertical.right.fill`
- `message-circle-warning` (1): `exclamationmark.bubble.circle.fill`
- `message-square-more` (1): `ellipsis.vertical.bubble.fill`
- `mic-signal` (1): `apple.podcasts.pages.fill`
- `microwave` (1): `oven.fill`
- `milk` (1): `waterbottle.fill`
- `monitor-off` (1): `tv.slash.fill`
- `motorbike` (1): `moped.fill`
- `mouse-pointer-click` (1): `dot.circle.and.hand.point.up.left.fill`
- `move` (1): `arrowkeys.fill`
- `music` (1): `apple.classical.pages.fill`
- `navigation-2` (1): `location.north.line.fill`
- `newspaper` (1): `magazine.fill`
- `notebook-text` (1): `menucard.fill`
- `package` (1): `shippingbox.and.arrow.backward.fill`
- `panel-bottom` (1): `square.bottomhalf.fill`
- `pause` (1): `pause.rectangle.fill`
- `pencil` (1): `applepencil.adapter.usb.c.fill`
- `phone-call` (1): `phone.and.waveform.fill`
- `plus` (1): `plus.capsule.fill`
- `pound-sterling` (1): `lirasign.square.fill`
- `printer` (1): `faxmachine.fill`
- `reply` (1): `arrowshape.turn.up.backward.badge.clock.fill`
- `rotate-ccw` (1): `restart.circle.fill`
- `ruler` (1): `level.fill`
- `sailboat` (1): `oar.2.crossed.circle.fill`
- `scan-face` (1): `face.dashed.fill`
- `scan-line` (1): `scanner.fill`
- `scooter` (1): `skateboard.fill`
- `settings` (1): `gearshape.2.fill`
- `shield-plus` (1): `staroflife.shield.fill`
- `ship` (1): `car.ferry.fill`
- `ship-wheel` (1): `steeringwheel.circle.fill`
- `shower-head` (1): `shower.sidejet.fill`
- `shuffle` (1): `line.3.crossed.swirl.circle.fill`
- `spray-can` (1): `inhaler.fill`
- `square-chevron-left` (1): `lessthanorequalto.square.fill`
- `square-chevron-right` (1): `greaterthanorequalto.square.fill`
- `square-dashed` (1): `square.dashed.inset.fill`
- `square-function` (1): `f.cursive.circle.fill`
- `square-pen` (1): `square.and.pencil.circle.fill`
- `square-play` (1): `play.square.stack.fill`
- `square-slash` (1): `square.split.diagonal.fill`
- `square-square` (1): `button.programmable.square.fill`
- `square-star` (1): `star.square.on.square.fill`
- `square-terminal` (1): `apple.terminal.on.rectangle.fill`
- `sticky-note` (1): `greetingcard.fill`
- `stone` (1): `figure.curling.circle.fill`
- `sun` (1): `sun.max.trianglebadge.exclamationmark.fill`
- `swords` (1): `figure.fencing.circle.fill`
- `test-tube` (1): `cross.vial.fill`
- `text-cursor-input` (1): `placeholdertext.fill`
- `thumbs-down` (1): `hand.thumbsdown.slash.fill`
- `timer` (1): `figure.highintensity.intervaltraining.circle.fill`
- `toy-brick` (1): `teddybear.fill`
- `trash` (1): `document.on.trash.fill`
- `trending-down` (1): `chart.line.downtrend.xyaxis.circle.fill`
- `triangle-right` (1): `righttriangle.split.diagonal.fill`
- `trophy` (1): `figure.track.and.field.circle.fill`
- `user-x` (1): `person.slash.fill`
- `volume-1` (1): `speaker.minus.fill`
- `volume-2` (1): `speaker.plus.fill`
- `wallet` (1): `wallet.sensor.tag.radiowaves.left.and.right.fill`
- `wallet-cards` (1): `wallet.pass.fill`
- `webcam` (1): `deskview.fill`
- `wifi` (1): `wifi.exclamationmark.circle.fill`
- `workflow` (1): `app.connected.to.app.below.fill`
