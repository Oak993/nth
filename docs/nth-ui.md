# nth-ui reference

nth-ui is the small language the assistant writes when an answer is better shown than told: a card with a table that
recomputes, a chart you can drag, a checklist, a form. The reply carries the view as plain lines of text; nth reads
them as they stream in and draws each line the moment it is complete. Nothing in a view can reach the network, the
disk or another program by itself. Everything a view does outside itself (copy, open a link, send a message, call a
tool) goes back to nth as a request, and nth decides.

This page is the reference for the language as nth implements it (`Marrow.Core/NthUi`). Every block marked `nth-ui`
below is parsed by the test suite and must stay clean, so the examples are known to work.

```nth-ui
card "Hosting estimate" icon=server
  state instances = 3
  state traffic = 40
  state region = "eu"
  let compute = instances * {eu: 0.046, us: 0.042, ap: 0.051}[region] * 730
  let egress = traffic * 30 * 0.02
  stepper instances label="Instances" min=1 max=24
  slider traffic label="Traffic" min=0 max=200 step=5 unit="GB/day"
  segmented region options="eu:Europe,us:US,ap:Asia"
  table cols="Item,Monthly"
    row "Compute" = fmt(compute, "$0")
    row "Egress" = fmt(egress, "$0")
    row "Storage" = "$9"
    row total "Total per month" = fmt(compute + egress + 9, "$0")
  chart bar x="Compute,Egress,Storage" y=[compute, egress, 9]
  actions
    copy "Copy as table" value=table
    send "Plan for twice the traffic"
followups
  chip "Compare with one large instance"
  chip "Add a CDN"
```

With the starting values the rows read $101, $24, $9 and $134. Move the stepper to 4 and every number that depends on
`instances` recomputes; nothing else is redrawn.

## 1. Lines

A view is a list of lines. Each line is one node: a component, a part of one, an action, or a declaration.

- **Indent** says where a line belongs. A line indented deeper than the line above it is that line's child. Two spaces
  per level is the convention; any deeper indent works, and a tab counts as two spaces.
- **Blank lines** are ignored, except inside the body of `code`, `svg` and `diagram`, where they are kept.
- **Fence lines** that start with three backticks are ignored, so a view wrapped in a Markdown fence reads the same.
- There are **no comments**. A line that is not a component shows as text (see section 9).

Every line has the same shape:

```text
row total "Total per month" = fmt(compute + egress + 9, "$0")
^^^ ^^^^^ ^^^^^^^^^^^^^^^^^   ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
head flag  positional value   tail (after the = on its own)
```

- The **head** is the first word and names the kind. It is not case sensitive.
- **Positional values** fill the kind's slots in order (for `stepper` the first slot is the state it sets; for `card`
  it is the title). Any slot can also be given by name.
- **Attributes** are written `name=value` with no spaces around the `=`.
- **Flags** are bare words the kind knows, such as `total`, `primary`, `done` or `stacked`. `flag=true` and
  `flag=false` also work.
- The **tail** is everything after an `=` that is not part of an attribute or of `==`. It is always an expression,
  and it fills the kind's main value: the text of a `text`, the value of a `stat`, the last cell of a table `row`, the
  value of a `state`.

Two attributes work on every kind:

| Attribute | Meaning |
| --- | --- |
| `id=name` | A stable name for the node, so a later update can find it. Ids are unique in a view; a repeated id is left out with a warning. |
| `when=condition` | The node shows only while the condition is true. A hidden action does nothing. |

### Values

| Written | Read as |
| --- | --- |
| `"text"` or `'text'` | A text. Escapes: `\"`, `\'`, `\\`, `\n`, `\t`, `\r`, `\uXXXX`. |
| `42`, `-3.5`, `1e3`, `.5` | A number. |
| `true`, `false` | A boolean. |
| `bar`, `points`, `arrow-up` | A word: a flag, a choice, an icon name, or the name of a state or let. |
| `(expression)` | Any expression, for example `when=(not reveal)`. |
| `[a, b, c]` | A list expression. |
| `{key: value}` | An object expression. |
| `name(arguments)` | A function call, for example `y=(sum(demand))` or just `value=fmt(total, "$0")`. |

Where an attribute takes a list (`x`, `y`, `cols`, `items`, `via`), it accepts either a list expression or a text of
comma-separated items, `"Jan,Feb,Mar"`; write `\,` for a comma inside an item. `options` also takes `"value:Label"`
pairs, `"eu:Europe,us:US"`, or a list of pairs or of `{value, label}` objects.

A number attribute outside its range is brought into range (`grid cols=9` draws 6 columns). A text slot takes a quoted
text, a number, or an expression; a bare word there is only read as free text (section 5).

## 2. Expressions

Expressions compute values from states and lets. They have no loops, no recursion, no way to define functions, and no
input or output. They always finish: a view's work is counted and stops at a fixed budget.

| Kind | Syntax |
| --- | --- |
| Literals | numbers, `"text"`, `true`, `false`, `null`, lists `[1, 2]`, objects `{name: "a", size: 2}` (keys are names or quoted texts) |
| Names | a state, a let, or inside `map`, `filter` and `sort` the item (`x => x * 2`, or `it` when no name is given) |
| Arithmetic | `+ - * / %` and unary `-`. `+` joins texts when either side is a text, and joins lists. |
| Comparison | `== != < <= > >=`; a single `=` also compares. Numbers compare with numbers, texts with texts; lists and objects compare for equality only. |
| Logic | `and`, `or`, `not`, also written `&&`, `\|\|`, `!` |
| Choice | `condition ? a : b`, which nests to the right |
| Access | `list[0]`, `list[-1]` (from the end), `object["key"]`, `object.key`, `list.length` |

Precedence, loosest first: `? :`, `or`, `and`, `not`, comparisons, `+ -`, `* / %`, unary `-`, then calls, indexing
and `.`.

**Errors are values.** Dividing by zero, a missing name, an index outside a list or a function given the wrong kind of
value gives an error value instead of stopping the view. An error shows as a dash (—) and flows through anything
built on it, so one bad cell never blanks the rest of the card. An expression that cannot be parsed at all makes its
line degrade to text (section 9).

### Functions

| Function | Result |
| --- | --- |
| `min(a, b, ...)`, `max(a, b, ...)` | The smallest or largest number; also takes one list. |
| `round(x)`, `round(x, digits)` | Rounds half away from zero: `round(2.5)` is 3, `round(-2.5)` is -3. |
| `floor(x)`, `ceil(x)`, `abs(x)`, `sqrt(x)` | As in arithmetic. |
| `pow(x, y)` | x to the power y. |
| `clamp(x, low, high)` | x kept between low and high. |
| `fmt(value, pattern)` | A display text (section 3). |
| `len(x)` | Items in a list, characters in a text, fields in an object. |
| `sum(list)`, `avg(list)` | Over a list of numbers. |
| `map(list, x => ...)` | Each item transformed. |
| `filter(list, x => ...)` | The items for which the condition is true. |
| `sort(list)`, `sort(list, x => key)` | Numbers or texts in ascending order, or ordered by a key. |
| `reverse(x)` | A list or a text backwards. |
| `slice(x, start)`, `slice(x, start, end)` | Part of a list or a text; negative positions count from the end. |
| `range(n)`, `range(a, b)`, `range(a, b, step)` | Numbers from a (default 0) up to but not including b. |
| `join(list)`, `join(list, separator)` | One text; the separator defaults to `", "`. |
| `contains(x, item)` | Whether a list holds an item, or a text holds a text. |
| `str(x)`, `num(x)` | A value as text; a text as a number. |

A lambda (`x => ...` or `(x) => ...`) is only allowed as the second value of `map`, `filter` and `sort`. Without one,
the second value is read over `it`: `map(prices, it * 1.2)`. Function names and the words `true`, `false`, `null`,
`and`, `or`, `not` and `it` cannot name a state, a let or a lambda's value.

## 3. Formatting with fmt

`fmt(value, pattern)` turns a number or a date into display text. The pattern reads like a spreadsheet format.

| Pattern | 1234.5 | Notes |
| --- | --- | --- |
| `0` | 1235 | Whole number, rounded half away from zero. |
| `0.00` | 1234.50 | Fixed decimals. |
| `0.##` | 1234.5 | Up to two decimals. |
| `#,##0` | 1,235 | A comma turns on thousands grouping. |
| `$0` | $1,235 | A currency sign before or after the number also groups thousands. |
| `$#,##0.00` | $1,234.50 | |
| `0.0a` | 1.2k | `a` shortens with k, M, B and T. |
| `0%` | 123450% | `%` multiplies by 100, so `fmt(0.256, "0.0%")` is 25.6%. Write `\%` for a plain percent sign. |
| `0 GB/day` | 1235 GB/day | Text around the number is kept. |

Negative numbers put the minus before the currency sign (-$1,007). A pattern with no digit placeholders shows the plain
value. A text that is a number is formatted as that number; any other value shows as it is.

A text holding an ISO date (`"2026-11-02"` or `"2026-11-16T14:30"`) takes .NET-style date patterns: `yyyy`, `MM`,
`MMM`, `MMMM`, `d`, `dd`, `ddd`, `dddd`, `HH`, `mm`. Text in single quotes inside the pattern is kept as written.
`fmt("2026-11-02", "dddd, MMMM d")` is Monday, November 2.

Formatting is the same on every machine: invariant separators and English month names. A host that knows the reader's
culture can pass it to the view, and then separators and names follow that culture.

## 4. State and let

A **state** is a value the reader can change. A **let** is a value computed from states and other lets.

```nth-ui
card "Paint"
  state width = 4
  state height = 2.5
  let area = width * height
  let tins = ceil(area / 10)
  stepper width label="Width" min=1 max=20 unit="m"
  slider height label="Height" min=2 max=4 step=0.1 unit="m"
  stat "Wall" = fmt(area, "0.0") + " square metres"
  stat "Tins" = tins
```

- `state name = value` declares a state with a starting value. Its type comes from the value, or from
  `type=number|text|bool|list|selection`. A later change keeps that type: a number state set from a text field holds
  the number the text reads as, and a change that cannot be read as the type is ignored.
- `let name = expression` declares a computed value. A let recomputes when anything it reads changes, and only then.
- Names are a letter or `_` followed by letters, digits or `_`, at most 64 characters, and are case sensitive.
- A name is declared once. A let cannot be set by a control or by `set`; such a line degrades.
- Order does not matter. A state may be declared after the control that sets it, and a let may read a let declared
  later. A let that reads itself, directly or through others, is an error value, not a hang.
- A control whose state is never declared makes one with a default: a `stepper` or `slider` starts at its `min` (or
  0), a `toggle` at false, a `segmented` or `select` at its first option, a text field empty, a `chart` with `bind`
  at its `y` values.

```nth-ui
card "Quick check"
  text "What does HTTP status 418 mean?"
  toggle reveal label="Show the answer"
  callout "I'm a teapot." tone=info when=reveal
  text muted "Try another one below." when=(not reveal)
```

A reader's states can be saved as JSON and restored when the conversation is opened again. Only states are saved; lets
are recomputed. A saved value of the wrong type is ignored, and the view's own starting value is used.

## 5. Components

The tables list each kind's positional slots in order, its attributes, its flags, its tail (`= value`), and what it
holds. Every kind also takes `id=` and `when=`.

### Text and layout

| Kind | Slots | Attributes | Flags | Tail | Holds |
| --- | --- | --- | --- | --- | --- |
| `text` | text | | `muted` `strong` `small` | text | |
| `heading` | text | `level` (1-3), `icon` | | text | |
| `card` | title | `subtitle`, `icon` | | | blocks |
| `section` | title | `icon` | | | blocks |
| `stack` | | `gap` (0-64) | | | blocks, top to bottom |
| `row` | | `gap`, `align` (start, center, end, between) | | | blocks, side by side |
| `grid` | | `cols` (1-6), `gap` | | | blocks in columns |
| `divider` | | | | | |
| `badge` | text | `tone` | | text | |
| `stat` | label | `value`, `unit`, `delta`, `icon`, `tone` | | value | |
| `callout` | text | `title`, `tone`, `icon` | | text | |

`tone` is one of neutral, info, success, warning, danger. An `icon` is a lower-case name such as `server`, `chart` or
`arrow-up`; an icon name that is not well formed is left out with a warning, and the line still draws.

`text`, `heading`, `badge` and `callout` also take free text: `text Each one is independent.` reads the rest of the
line as the text when it is not a quoted value. The same holds for `item`, `event`, `chip` and `send`.

```nth-ui
heading "Build status" level=2 icon=check
row gap=8
  badge "main" tone=success
  badge "3 warnings" tone=warning
divider
grid cols=3
  stat "Visitors" value="12,480" delta="+8%" icon=users
  stat "Sign-ups" value=342 delta="-2%" tone=danger
  stat "Revenue" delta="+12%" tone=success = fmt(18230, "$#,##0")
callout "Two tests are flaky on Windows." title="Heads up" tone=warning
```

### Media

| Kind | Slots | Attributes | Holds |
| --- | --- | --- | --- |
| `image` | src | `alt`, `width`, `height` (1-4096), `fit` (contain, cover) | |
| `svg` | | `alt`, `width`, `height` | its SVG markup, as an indented body |
| `diagram` | title | `alt` | a Mermaid diagram, as an indented body |
| `code` | text | `lang`, `title` | source code, as an indented body |

An image loads only from an `https:` address, a `data:image/png|jpeg|gif|webp` address, or an `artifact:` asset. The
body of `svg`, `diagram` and `code` is every following line indented deeper than the head line, taken as written
(the head's indent plus two spaces is removed). Body lines are not parsed as nth-ui, and they stream in line by line.

```nth-ui
code lang=python title="fib.py"
  def fib(n):
      a, b = 0, 1
      for _ in range(n):
          a, b = b, a + b
      return a
diagram "Request flow" alt="Client to server to database"
  graph LR
    A[Client] --> B[Server]
    B --> C[(Database)]
```

### Data

| Kind | Slots | Attributes | Flags | Tail | Holds |
| --- | --- | --- | --- | --- | --- |
| `table` | title | `cols`, `rows` | | | `row` lines |
| `row` (in a table) | cells | | `total` `muted` | last cell | |
| `chart` | type | `title`, `x`, `y`, `series`, `unit`, `min`, `max`, `step`, `editable`, `bind` | `stacked` | | `series` lines |
| `series` | name | `x`, `y` | | y | |
| `list` | title | `items` | `ordered` | | `item` lines |
| `checklist` | title | `bind` | | | `item` lines |
| `item` | text | `value`, `detail` | `done` | text | |
| `timeline` | title | | | | `event` lines |
| `event` | title | `time`, `detail`, `tone` | `done` | detail | |
| `map` | title | `zoom` (0-20) | | | `stop` and `route` lines |
| `stop` | label | `lat`, `lon` (both required), `detail` | | | |
| `route` | label | `via` (required) | | | |

A table `row` takes any number of cells, then the tail as the last cell. `rows=` builds the rows from an expression
instead, as a list of lists. A chart's `type` is line, bar, area, pie or scatter. Its values come from `y` (one list,
or a list of lists with `series` naming each), or from `series` lines; `x` labels the points (on a scatter the numbers in `x`
position them).

```nth-ui
card "Savings plan"
  state monthly = 200
  state rate = 5
  slider monthly label="Monthly" min=50 max=2000 step=50 unit="$"
  slider rate label="Return" min=0 max=12 step=0.5 unit="%"
  table cols=["Year", "Saved", "Balance"] rows=(map(range(1, 11), y => [y, fmt(monthly * 12 * y, "$#,##0"), fmt(monthly * 12 * ((pow(1 + rate / 100, y) - 1) / (rate / 100 + 0.000001)), "$#,##0")]))
chart area x="Q1,Q2,Q3,Q4" series="Web,Mobile" y=[[120, 140, 160, 210], [80, 95, 130, 170]] stacked
chart scatter title="Price and rating"
  series "Phones" x=[199, 349, 499, 799, 999] y=[3.9, 4.2, 4.4, 4.6, 4.5]
  series "Tablets" x=[249, 449, 649] y=[4.0, 4.3, 4.7]
map "Lisbon to Paris" zoom=4
  stop "Lisbon" lat=38.72 lon=-9.14
  stop "Madrid" lat=40.42 lon=-3.70 detail="Overnight"
  stop "Paris" lat=48.86 lon=2.35
  route "By train" via="Lisbon,Madrid,Paris"
```

### How the diagram, line art and map are drawn

A `diagram` is drawn natively as vector, in the thread's own light or dark palette. nth reads its Mermaid source as it
streams and asks its renderer for a drawing at most every fifth of a second, once a whole statement has arrived; a
statement Mermaid cannot read yet leaves the last drawing in place, and a source that cannot be drawn at the end shows its
text with a note. "Show source" swaps the drawing for the Mermaid text, and "Copy" copies it. Where no renderer is
available (no web runtime on the machine) the source shows from the start. The drawing never uses a live web view.

An `svg` is sanitised before anything is drawn. Only shapes (`rect`, `circle`, `ellipse`, `line`, `polyline`, `polygon`,
`path`), `text` and `tspan`, groups, gradients, markers and `use` of an element of the same picture are kept, with their
presentation attributes (fill, stroke, opacity, dashes, transforms, fonts). Scripts, `foreignObject`, style sheets,
images, filters, clips, masks, patterns, animation, event handlers, classes and every address are dropped, and the picture
is limited to 262,144 characters, 8,000 parts and 32 levels. It scales to fit its room and never stretches. A stroke or a
text colour that would be lost on the thread (black on dark) is moved along its own hue until it reads: 3 to 1 for strokes,
4.5 to 1 for text. `currentColor` is the primary text colour.

A `map` draws on a built-in world map (Natural Earth, public domain) that works offline: countries and their borders,
lakes, labels for countries as the zoom reaches them and for the 300 largest cities. `zoom` is the usual map zoom of the
first view (0 shows the whole world, 4 about western Europe); the map never zooms deeper than 10, where the built-in data
stops adding detail, and the view always keeps every stop in sight until the reader moves the map. A `route` names its
stops by their labels (case and surrounding spaces ignored; a name that is not a stop is skipped) and is drawn along the
great circles between them, the short way across the date line. The reader pans by dragging, zooms with Ctrl and the wheel,
the plus and minus buttons or the `+` and `-` keys, moves with the arrow keys, and returns to every stop with the third button
or Home; "Show as list" shows the stops and routes as a plain list. Street tiles are used only if the reader adds a tile
source in Settings, since that sends locations to a third party.

### Editable charts: editable and bind

`chart ... editable=points bind=name` lets the reader drag the chart's points. The chart reads its values from the list
state `name` and writes each drag back to it, so every let and cell built on that state recomputes while the point
moves. A moved point stays between `min` and `max` and snaps to `step`; a keyboard nudge moves it by one step.

```nth-ui
card "Demand plan" icon=chart
  state demand = [12, 18, 25, 30, 28, 35]
  let total = sum(demand)
  let peak = max(demand)
  text "Drag a point to change a month."
  chart line x="Jan,Feb,Mar,Apr,May,Jun" editable=points bind=demand min=0 max=60 step=1 unit="units"
  grid cols=2
    stat "Total" = fmt(total, "#,##0")
    stat "Peak" = peak
```

`editable=points` without `bind` draws a normal chart and adds a warning. `bind` without an existing state makes a
list state from the chart's `y`. Only a line, bar or area chart of one series can be edited; on a scatter or a pie the
word is ignored and the chart is drawn as a normal one.

### How a chart is drawn

nth draws every chart natively, in the thread's monochrome line art: hairline gridlines, tabular numerals, series told
apart by tone, dash and marker shape (never by colour alone), and the accent only for the point the reader is on.

- A `bar` chart of one series with up to twelve categories is drawn as the bar list: each row has the name, a bar as long
  as its share of the largest value, and the value, with the largest bar in the accent. More categories, several series,
  `stacked` or an editable chart are drawn as vertical bars.
- A `pie` is a ring with a key (name, value and share of each slice) and the total, or the slice the reader is on, in
  the middle. Only the first series counts, and a value that is not above zero is not a slice.
- The axis of a `bar` or `area` chart rests on zero. A `line` or `scatter` chart starts at zero when its values are not
  far above it, else it follows the data. `min` and `max` fix the ends. The ticks are round numbers (1, 2, 2.5 or 5 times
  a power of ten) with the same decimals on every label, and a scatter plots `y` against the numbers in `x`.
- Marks draw in once, left to right, when a chart arrives; a series that arrives later draws in beside the ones already
  there. When a state changes, every mark springs to its new value, the axis range springs, and the old gridlines fade as
  the new ones fade in. Under Reduce Motion this is a short fade.
- Pointing at a column or a point (or walking a chart with the keys Left, Right, Up, Down, Home and End, which the chart
  takes as one stop in the tab order) shows a readout of its values. A pie lifts the slice under the pointer and lights
  its row in the key.
- On an editable chart a press picks the column under the pointer and a drag moves its point exactly with the pointer;
  the state keeps the value snapped to `step` and clamped to `min` and `max`, and every `let` and stat that reads it
  follows in the same frame. The axis holds still under the finger and makes room when it is let go. Each point is also a
  range control: Up and Down change it by `step`, Page Up and Page Down by ten steps.
- Every chart carries a text summary (its description for a screen reader) and a quiet "Show as table" button under it that
  swaps the drawing for a table of its numbers, one row a point and one column a series. The table follows the data while
  it is shown.

### Choices and steps

| Kind | Slots | Attributes | Flags | Holds |
| --- | --- | --- | --- | --- |
| `chips` | title | `bind` | `single` | `chip` lines |
| `chip` | label | `value` | `selected` | |
| `tabs` | | `bind` | | `tab` lines |
| `tab` | label | `value` | | blocks |
| `steps` | title | `bind` | | `step` lines |
| `step` | title | | | blocks |
| `followups` | | | | `chip` lines |

- `chips` is a filter: its state is the list of selected chip values (a chip's value defaults to its label). With
  `single` it is one text instead.
- A `checklist`'s state is the list of done item values.
- `tabs` holds the value of the open tab (the first tab to start with); `steps` holds the index of the current step,
  starting at 0.
- Without `bind` the choice is still kept, under a name of its own that expressions cannot read.
- `followups` sits at the top level, after the view. Each chip there sends its text as the reader's next message.

```nth-ui
card "Languages"
  chips "Show" bind=langs
    chip "C#" value=cs selected
    chip "Swift" value=swift
    chip "Rust" value=rust
  table cols="Language,Typing"
    row "C#" "Static" when=(contains(langs, "cs"))
    row "Swift" "Static" when=(contains(langs, "swift"))
    row "Rust" "Static" when=(contains(langs, "rust"))
  text muted = len(langs) + " shown"
checklist "Before launch" bind=done
  item "Run the test suite" value=tests done
  item "Update the changelog" value=changelog
text = len(done) + " of 2 done"
tabs bind=plan
  tab "Starter" value=starter
    stat "Price" = "$9 per month"
  tab "Team" value=team
    stat "Price" = "$29 per month"
steps "Setup"
  step "Install"
    code lang=bash
      npm install
  step "Run"
    code lang=bash
      npm run dev
timeline "Release 2.4"
  event "Code freeze" time="Mon 9:00" done
  event "Staged rollout" time="Wed" tone=warning
```

### Inputs

Every input's first slot is the state it sets. All take `label=`.

| Kind | Attributes | Flags | Value |
| --- | --- | --- | --- |
| `stepper` | `min`, `max`, `step`, `unit` | | a number |
| `slider` | `min`, `max`, `step`, `unit` | | a number |
| `segmented` | `options` (required) | | the chosen option's value |
| `select` | `options` (required) | | the chosen option's value |
| `toggle` | | | true or false |
| `textfield` | `placeholder` | `multiline` | a text (at most 4000 characters) |
| `date` | `min`, `max` (ISO dates) | | an ISO date text |
| `form` | `title` slot, `submit` | | holds inputs and blocks; tail = message |

A stepper and slider keep their value between `min` and `max` and on the `step` grid. Without `step`, a stepper moves
by 1 and a slider by about a hundredth of its range. A `form` draws a submit button; submitting sends the form's values as the reader's message, or the tail
expression when the form has one.

```nth-ui
form "Book a table" submit="Request booking"
  textfield name label="Name" placeholder="Your name"
  stepper guests label="Guests" min=1 max=10
  date day label="Day" min="2026-10-12" max="2026-12-31"
  select time label="Time" options="early:6 pm,mid:7 pm,late:8 pm"
  toggle outside label="Outside if the weather allows"
```

## 6. Actions

Actions sit inside `actions`, where each one draws as a button, or inside a `button`, where they run in order when it
is pressed.

| Kind | Slots | Attributes | Tail | Does |
| --- | --- | --- | --- | --- |
| `actions` | | | | holds actions, drawn as a row of buttons |
| `button` | label | | | runs the actions inside it, in order; flag `primary` |
| `set` | target, label | | value (required) | sets a state |
| `toggle` (in actions or a button) | target, label | | | flips a true/false state |
| `copy` | label | `value` | value | copies text; `value=table` copies the nearest table as tab-separated rows, `value=view` the whole view as text |
| `open` | label, url | | | asks nth to open an http or https address |
| `export` | format (csv, png), label | `value` | | saves the nearest table as CSV, or the nearest card as an image |
| `send` | text | | message | sends a message as the reader |
| `context` | key | | value (required) | attaches a value to the reader's next message, as JSON under the key |
| `call` | tool, args, label | | | asks nth to run a tool, `server.tool`, with an object of arguments |

Only `set` and `toggle` change the view. Every other action is a request that nth carries out with its usual rules
and approvals; a view cannot run a tool, open an address or send a message without nth.

```nth-ui
card "Release r42" icon=rocket
  state approved = false
  toggle approved label="I checked the staging build"
  actions
    button "Promote" primary when=approved
      call shipyard.promote {"release": "r42", "channel": "stable"}
      set approved = false
    open "Release notes" url="https://example.com/releases/r42"
    export csv "Download the table"
    context "release" = {release: "r42", approved: approved}
    send "Roll back r42"
  table cols="Check,Status"
    row "Tests" "Passed"
    row "Canary" = approved ? "Approved" : "Waiting"
```

## 7. Streaming

nth parses a view while it is still arriving.

- A line is placed when its line break arrives (or when the reply ends). Placed nodes never move and never change
  kind, so a view grows downward without reflowing what is already drawn.
- The line still arriving is shown early when it is text: a `text`, `heading` or `callout` line, or the next line of a
  `code`, `svg` or `diagram` body.
- Each node has a key: its `id` when it has one, otherwise its position (`0.2.1` is the second child of the third
  child of the first top-level node). Keys stay the same while the view grows.
- Values recompute as their states arrive. A control that streams in before its `state` line starts at its default
  and takes the declared value when that line arrives, unless the reader has already changed it.
- When a state changes, only the nodes that read it, directly or through lets, are redrawn.

## 8. Lite

Some places in nth (a compact reply, a narrow panel) draw only the lite subset:

`card`, `text`, `table` with its `row` lines, `chart` with its `series`, `stepper`, `slider`, `chips` with its `chip`
lines, `checklist` with its `item` lines, `followups`, `state` and `let`.

Any other kind degrades to text in a lite view. A view written within the subset draws the same in both.

```nth-ui lite
card "Room temperature"
  state target = 21
  let cost = (target - 18) * 4.5
  stepper target label="Target" min=16 max=26 unit="C"
  slider target label="Fine tune" min=16 max=26 step=0.5
  text = "About " + fmt(cost, "$0") + " a month above 18 degrees."
  table cols="Setting,Cost"
    row "Target" = fmt(target, "0.0")
    row total "Monthly" = fmt(cost, "$0")
  chart bar x="Heating" y=[cost]
  chips "Rooms" bind=rooms
    chip "Living room" selected
    chip "Bedroom"
  checklist "Tips"
    item "Close the curtains at night"
    item "Bleed the radiators" done
followups
  chip "How do I bleed a radiator?"
```

## 9. When a line is wrong

A view never fails as a whole. A line that cannot be used **degrades**: it shows as its own text, in the nearest place
that can show text, and nth records why. The rest of the view is unaffected.

A line degrades when:

- its head is not a kind (`gauge`, or a sentence that does not start with a known word);
- its kind cannot sit where it is written (a `chip` at the top level, a `row` with cells outside a table);
- a value does not fit (`chart donut`, `open "file:///etc"`), a required value is missing (`stop "Lisbon"` without
  `lat` and `lon`), a value is given twice, or an expression does not parse;
- it declares a name that is already declared, or sets a let;
- it is longer than 8000 characters or holds more than 256 values;
- the view is lite and the kind is not.

Before a line degrades, nth tries the line's ancestors: a line that its parent cannot hold, but a grandparent can, is
placed under the grandparent. So a `text` written under a `table` lands in the card that holds the table.

Some problems only warn, and the line still draws: an attribute the kind does not have is left out; a badly formed
icon name is left out; `editable` without `bind` is dropped; a repeated `id` is left out; a name that is never
declared shows a dash where it is used.

```nth-ui degrades
card "Plan"
  gauge cpu = 0.4
  text "The gauge line above shows as text inside the card."
chip "A chip needs chips or followups around it"
let total = 3
stepper total label="A let cannot be set"
```

```nth-ui lite degrades
card "Release"
  timeline "Not in the lite subset"
    event "Code freeze"
```

## 10. Limits

| Limit | Value |
| --- | --- |
| Line length | 8000 characters |
| Values on one line | 256 |
| Nodes in one view | 2000 (later lines are left out, with a note) |
| Nesting of nodes | 24 levels (deeper lines join the deepest allowed container) |
| Body of one `code`, `svg` or `diagram` | 262,144 characters |
| Expression length | 8000 characters |
| Brackets inside one expression | 48 levels |
| Lets built on lets | 64 levels |
| List length | 10,000 items |
| Text length | 20,000 characters |
| fmt pattern | 64 characters |
| Work per redraw | 1,000,000 steps; an expression that needs more is an error value |
