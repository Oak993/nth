# Web surfaces W17, Teaching (DESIGN.md 13, 14 row W17; ED-NEW-web-w17-1). The answers five providers' models are recorded
# as giving to ten prompts, in our own words. Marrow-authored synthetic transcripts, never captured from a live provider
# (the same convention as tests/Marrow.Services.Tests/Port/Skills/Providers/Fixtures). generate.py turns this data into
# each provider's wire format; the tests replay those files through the real session code and check every block.
#
# A prompt expects one of: plain (no view and no artifact), view (exactly one live view, after a sentence) or artifact.

PROMPTS = [
    ("01-capital", "What is the capital of Australia?", "plain"),
    ("02-savings", "How long until I reach a 12,000 savings goal if I put 400 aside each month?", "view"),
    ("03-databases", "Compare Postgres and SQLite for a small web app.", "view"),
    ("04-moving", "I move house in four weeks. What should I do when?", "view"),
    ("05-study", "Help me plan my study hours this week. I want to adjust them myself.", "view"),
    ("06-oauth", "How does signing in with OAuth work?", "view"),
    ("07-mutex", "Explain what a mutex is.", "plain"),
    ("08-pomodoro", "Make me a Pomodoro timer page I can keep using.", "artifact"),
    ("09-bakery", "Build a small landing page for my bakery, with its own stylesheet, that I can keep editing.", "artifact"),
    ("10-laptop", "Help me choose a laptop.", "view"),
]

# ----------------------------------------------------------------------------------------------------------------------
# The views. R is the rich one, T the terse one, L the lite one for a small local model.
# ----------------------------------------------------------------------------------------------------------------------

SAVINGS_R = r'''card "Savings goal" icon=piggy-bank
  state goal = 12000
  state monthly = 400
  let months = ceil(goal / monthly)
  stepper goal label="Goal" min=1000 max=100000 step=1000 unit="$"
  slider monthly label="Per month" min=50 max=2000 step=50 unit="$"
  stat "Months to goal" = months
  table cols="Milestone,Month"
    row "A quarter" = ceil(goal * 0.25 / monthly)
    row "Halfway" = ceil(goal * 0.5 / monthly)
    row total "Done" = months
  chart line title="Saved over the first year" x=(map(range(1, 13), m => "M" + m)) y=(map(range(1, 13), m => m * monthly))
followups
  chip "What if I add a lump sum now?"
'''

SAVINGS_T = r'''card "Savings goal"
  state monthly = 400
  let months = ceil(12000 / monthly)
  slider monthly label="Per month" min=50 max=2000 step=50 unit="$"
  stat "Months to $12,000" = months
'''

SAVINGS_L = r'''card "Savings goal"
  state monthly = 400
  let months = ceil(12000 / monthly)
  slider monthly label="Per month" min=50 max=2000 step=50 unit="$"
  table cols="Item,Value"
    row "Goal" = "$12,000"
    row total "Months" = months
followups
  chip "Add some interest"
'''

DATABASES_R = r'''card "Postgres or SQLite?" subtitle="For a small web app"
  grid cols=2
    stat "Postgres" = "A server"
    stat "SQLite" = "One file"
  table cols="Question,Postgres,SQLite"
    row "Runs where" "Its own server" "Inside your app"
    row "Several writers" "Yes" "One at a time"
    row "Backup" "Dump the database" "Copy the file"
  callout "Start with SQLite and move to Postgres when a second service needs the data." tone=info
followups
  chip "How do I migrate later?"
  chip "Which is cheaper to host?"
'''

DATABASES_T = r'''table cols="Question,Postgres,SQLite"
  row "Runs where" "Its own server" "Inside your app"
  row "Several writers" "Yes" "One at a time"
  row "Backup" "Dump" "Copy the file"
'''

DATABASES_L = r'''card "Postgres or SQLite"
  chips "Show" bind=picked
    chip "Postgres" value=pg selected
    chip "SQLite" value=lite selected
  table cols="Question,Postgres,SQLite"
    row "Runs where" "Its own server" "Inside your app"
    row "Several writers" "Yes" "One at a time"
followups
  chip "How do I migrate later?"
'''

MOVING_R = r'''tabs bind=week
  tab "Week 1" value=w1
    checklist "Do now" bind=w1done
      item "Book movers" value=movers
      item "Sort paperwork" value=paper
  tab "Week 2" value=w2
    checklist "Pack" bind=w2done
      item "Rarely used rooms" value=rooms
      item "Garage and attic" value=garage
  tab "Week 3" value=w3
    checklist "Admin" bind=w3done
      item "Change address" value=address
      item "Move utilities" value=utilities
  tab "Week 4" value=w4
    checklist "Move out" bind=w4done
      item "Final pack" value=final
      item "Clean" value=clean
      item "Hand over keys" value=keys
'''

MOVING_T = r'''checklist "Before the move" bind=todo
  item "Book movers" value=movers
  item "Order boxes" value=boxes
  item "Change address" value=address
  item "Move utilities" value=utilities
text muted = len(todo) + " of 4 done"
'''

MOVING_L = r'''card "Moving in four weeks"
  checklist "Week by week" bind=todo
    item "Week 1: book movers" value=w1
    item "Week 2: pack spare rooms" value=w2
    item "Week 3: change address" value=w3
    item "Week 4: final pack and keys" value=w4
  text muted = len(todo) + " of 4 done"
'''

STUDY_R = r'''card "This week's study" icon=book
  state hours = [1, 2, 2, 3, 2, 0, 1]
  let total = sum(hours)
  let perday = round(total / 7, 1)
  text "Drag a point to change a day."
  chart line x="Mon,Tue,Wed,Thu,Fri,Sat,Sun" editable=points bind=hours min=0 max=8 step=0.5 unit="h"
  grid cols=2
    stat "Total" = fmt(total, "0.#") + " h"
    stat "Per day" = fmt(perday, "0.0") + " h"
'''

STUDY_T = r'''state hours = [1, 2, 2, 3, 2, 0, 1]
chart bar x="Mon,Tue,Wed,Thu,Fri,Sat,Sun" editable=points bind=hours min=0 max=8 step=1
text = "Total: " + sum(hours) + " h"
'''

STUDY_L = r'''card "Study plan"
  state hours = [1, 2, 2, 3, 2, 0, 1]
  chart bar x="Mon,Tue,Wed,Thu,Fri,Sat,Sun" unit="h" min=0 max=8 step=1 editable=points bind=hours
  text muted = "Total: " + sum(hours) + " hours"
'''

OAUTH_R = r'''diagram "OAuth sign-in" alt="The app sends you to the provider, which returns a code the app swaps for a token"
  graph LR
    A[App] -->|redirect| B[Provider]
    B -->|approve| C[You]
    C -->|code| A
    A -->|code and secret| B
    B -->|token| A
callout "The app never sees your password." tone=info
'''

OAUTH_T = r'''steps "OAuth sign-in"
  step "Redirect"
    text "The app sends you to the provider."
  step "Approve"
    text "You sign in there and approve access."
  step "Code"
    text "The provider sends the app a one-time code."
  step "Token"
    text "The app swaps the code for a token."
'''

OAUTH_L = r'''card "OAuth sign-in"
  table cols="Step,What happens"
    row "1" "The app sends you to the provider"
    row "2" "You sign in and approve"
    row "3" "The provider returns a one-time code"
    row "4" "The app swaps the code for a token"
'''

LAPTOP_R = r'''form "Laptop finder" submit="Suggest laptops"
  segmented os label="System" options="win:Windows,mac:Mac,any:No preference"
  stepper ram label="Memory" min=8 max=64 step=8 unit="GB"
  slider budget label="Budget" min=500 max=4000 step=100 unit="$"
  toggle battery label="All-day battery matters"
  textfield notes label="Anything else" multiline
'''

LAPTOP_T = r'''form "Laptop finder" submit="Suggest"
  slider budget label="Budget" min=500 max=4000 step=100 unit="$"
  select use label="Mostly for" options="work:Work,study:Study,games:Games"
'''

LAPTOP_L = r'''card "Laptop budget"
  state budget = 1200
  let tier = budget < 800 ? "Entry" : budget < 1600 ? "Mid-range" : "High end"
  slider budget label="Budget" min=500 max=4000 step=100 unit="$"
  table cols="Item,Value"
    row "Budget" = fmt(budget, "$#,##0")
    row "Tier" = tier
followups
  chip "Suggest laptops in this range"
'''

# ----------------------------------------------------------------------------------------------------------------------
# The artifacts.
# ----------------------------------------------------------------------------------------------------------------------

POMODORO_R = r'''<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Pomodoro</title>
<style>
body{font-family:system-ui,sans-serif;display:grid;place-items:center;min-height:100vh;margin:0;background:#101418;color:#eef}
.t{font-size:5rem;font-variant-numeric:tabular-nums}
button{font:inherit;padding:.5rem 1rem;margin:.25rem;border-radius:.5rem;border:0}
</style>
</head>
<body>
<main>
<div class="t" id="t">25:00</div>
<button id="go">Start</button><button id="reset">Reset</button>
</main>
<script>
let left=25*60,timer=null;
const t=document.getElementById('t');
function draw(){t.textContent=String(Math.floor(left/60)).padStart(2,'0')+':'+String(left%60).padStart(2,'0')}
document.getElementById('go').onclick=()=>{if(timer){clearInterval(timer);timer=null;return}timer=setInterval(()=>{left=Math.max(0,left-1);draw();if(!left){clearInterval(timer);timer=null}},1000)};
document.getElementById('reset').onclick=()=>{left=25*60;draw()};
</script>
</body>
</html>
'''

POMODORO_T = r'''<!doctype html>
<meta charset="utf-8">
<title>Pomodoro</title>
<h1 id="t">25:00</h1>
<button onclick="start()">Start</button>
<script>
let s=1500;
function start(){setInterval(()=>{s--;t.textContent=Math.floor(s/60)+':'+String(s%60).padStart(2,'0')},1000)}
</script>
'''

BAKERY_INDEX = r'''<!doctype html>
<html lang="en">
<head><meta charset="utf-8"><title>Rye and Co. Bakery</title><link rel="stylesheet" href="style.css"></head>
<body>
<header><h1>Rye and Co.</h1><p>Sourdough, pastries and coffee since 2014.</p></header>
<main>
<section><h2>Today's bakes</h2><ul><li>Country loaf</li><li>Rye with caraway</li><li>Cardamom buns</li></ul></section>
<section><h2>Visit us</h2><p>12 Mill Lane. Open Tuesday to Sunday, 7am to 3pm.</p></section>
</main>
</body>
</html>
'''

BAKERY_CSS = r'''body{margin:0;font-family:Georgia,serif;background:#fbf6ee;color:#3b2a1a}
header{padding:3rem 1.5rem;text-align:center;background:#e9d8bd}
main{max-width:40rem;margin:0 auto;padding:1.5rem}
h1,h2{font-weight:600}
'''

BAKERY_TERSE_INDEX = r'''<!doctype html>
<meta charset="utf-8">
<title>Rye and Co.</title>
<link rel="stylesheet" href="style.css">
<h1>Rye and Co. Bakery</h1>
<p>Open Tuesday to Sunday, 7am to 3pm.</p>
'''

BAKERY_TERSE_CSS = r'''body{font-family:Georgia,serif;background:#fbf6ee;text-align:center}
'''

# ----------------------------------------------------------------------------------------------------------------------
# What each provider's model writes. A view answer is a lead sentence and a view; an artifact answer a lead sentence and
# an artifact; a plain answer just text. "style" picks R, T or L. Providers: claude and codex call nth-ui's tools through
# MCP; deepseek calls the same tools as native functions; copilot writes fenced blocks; local writes lite fenced blocks.
# ----------------------------------------------------------------------------------------------------------------------

VIEWS = {
    "02-savings": {"R": SAVINGS_R, "T": SAVINGS_T, "L": SAVINGS_L},
    "03-databases": {"R": DATABASES_R, "T": DATABASES_T, "L": DATABASES_L},
    "04-moving": {"R": MOVING_R, "T": MOVING_T, "L": MOVING_L},
    "05-study": {"R": STUDY_R, "T": STUDY_T, "L": STUDY_L},
    "06-oauth": {"R": OAUTH_R, "T": OAUTH_T, "L": OAUTH_L},
    "10-laptop": {"R": LAPTOP_R, "T": LAPTOP_T, "L": LAPTOP_L},
}

STYLE = {"claude": "R", "codex": "T", "copilot": "T", "deepseek": "R", "local": "L"}

LEADS = {
    "claude": {
        "02-savings": "At 400 a month you reach 12,000 in 30 months. Move the sliders to try other amounts.",
        "03-databases": "For a small app SQLite is the least work and Postgres is the safest to grow into. Here they are side by side.",
        "04-moving": "Four weeks is enough if you spread it out. Here is each week as a checklist you can tick off.",
        "05-study": "Here is a starting plan of 11 hours. Drag any point to change a day; the totals follow.",
        "06-oauth": "The app never sees your password: it sends you to the provider, gets a one-time code back and trades it for a token.",
        "10-laptop": "A few details will narrow it down. Fill in what matters and I will suggest some.",
    },
    "codex": {
        "02-savings": "30 months at 400 a month.",
        "03-databases": "SQLite for a small app; Postgres when it grows.",
        "04-moving": "A checklist for the move.",
        "05-study": "11 hours to start with; drag the points to change them.",
        "06-oauth": "OAuth in four steps.",
        "10-laptop": "Two questions first.",
    },
    "copilot": {
        "02-savings": "At 400 a month it takes 30 months. Try other amounts below.",
        "03-databases": "Here is how the two compare for a small web app.",
        "04-moving": "Here is a simple checklist for the four weeks.",
        "05-study": "This plan has 11 hours in total. Drag the bars to adjust it.",
        "06-oauth": "These are the four steps of an OAuth sign-in.",
        "10-laptop": "Tell me a little about what you need.",
    },
    "deepseek": {
        "02-savings": "It takes 30 months: 12,000 divided by 400. Adjust the monthly amount to compare.",
        "03-databases": "Postgres is the stronger database; SQLite is simpler. This table shows the trade-offs.",
        "04-moving": "I split the move into four weeks. Tick items off as you go.",
        "05-study": "I started you at 11 hours across the week. Drag a point to change any day.",
        "06-oauth": "OAuth lets an app sign you in without ever seeing your password. The flow looks like this.",
        "10-laptop": "To suggest a laptop I need a few details.",
    },
    "local": {
        "02-savings": "At 400 a month it takes 30 months.",
        "03-databases": "SQLite is simpler, Postgres scales further.",
        "04-moving": "Here is a plan for each week.",
        "05-study": "You can change the hours below.",
        "06-oauth": "These are the steps of OAuth.",
        "10-laptop": "Here is a quick way to size your budget.",
    },
}

PLAIN = {
    "claude": {
        "01-capital": "Canberra. Sydney is bigger, but Canberra has been the capital since 1913.",
        "07-mutex": "A mutex is a lock that lets one thread at a time into a piece of code, so two threads cannot change the same data together.\n\nIn Python it looks like this:\n\n```python\nimport threading\n\nlock = threading.Lock()\ncount = 0\n\ndef add():\n    global count\n    with lock:\n        count += 1\n```\n\nWithout the lock, two threads could both read the old value of `count` and one increment would be lost.",
    },
    "codex": {
        "01-capital": "Canberra.",
        "07-mutex": "A mutex is a lock that lets only one thread at a time use a shared resource.",
    },
    "copilot": {
        "01-capital": "The capital of Australia is Canberra.",
        "07-mutex": "A mutex (mutual exclusion lock) makes sure only one thread runs a critical section at a time, so shared data is never changed by two threads together.",
    },
    "deepseek": {
        "01-capital": "Canberra is the capital of Australia, not Sydney or Melbourne.",
        "07-mutex": "A mutex is a lock around shared data. A thread takes it before touching the data and releases it afterwards; any other thread that wants it waits.",
    },
    "local": {
        "01-capital": "Canberra.",
        "07-mutex": "A mutex is a lock. Only one thread can hold it at a time, so only one thread changes the shared data at a time.",
    },
}

ARTIFACTS = {
    "claude": {
        "08-pomodoro": ("I made the timer as a page you can keep. It counts down from 25 minutes.",
                        {"kind": "html", "title": "Pomodoro timer", "content": POMODORO_R}),
        "09-bakery": ("Here is a two-file landing page. The stylesheet is separate, so you can restyle it without touching the page.",
                      {"kind": "project", "title": "Bakery landing page", "files": {"index.html": BAKERY_INDEX, "style.css": BAKERY_CSS}}),
    },
    "codex": {
        "08-pomodoro": ("A minimal Pomodoro timer.", {"kind": "html", "title": "Pomodoro timer", "content": POMODORO_T}),
        "09-bakery": ("A landing page with its own stylesheet.",
                      {"kind": "project", "title": "Bakery page", "files": {"index.html": BAKERY_TERSE_INDEX, "style.css": BAKERY_TERSE_CSS}}),
    },
    "copilot": {
        "08-pomodoro": ("Here is a Pomodoro timer you can keep.", {"kind": "html", "title": "Pomodoro timer", "content": POMODORO_T, "id": "pomodoro"}),
        "09-bakery": ("Here is a landing page with its own stylesheet.",
                      {"kind": "project", "title": "Bakery page", "id": "bakery", "files": {"index.html": BAKERY_TERSE_INDEX, "style.css": BAKERY_TERSE_CSS}}),
    },
    "deepseek": {
        "08-pomodoro": ("I will make the timer as a page you can keep and edit.",
                        {"kind": "html", "title": "Pomodoro timer", "content": POMODORO_R}),
        "09-bakery": ("I will build the landing page with a separate stylesheet.",
                      {"kind": "project", "title": "Bakery landing page", "files": {"index.html": BAKERY_INDEX, "style.css": BAKERY_CSS}}),
    },
    "local": {
        "08-pomodoro": ("Here is a timer page.", {"kind": "html", "title": "Pomodoro timer", "content": POMODORO_T, "id": "pomodoro"}),
        "09-bakery": ("Here is the landing page with its stylesheet.",
                      {"kind": "project", "title": "Bakery page", "id": "bakery", "files": {"index.html": BAKERY_TERSE_INDEX, "style.css": BAKERY_TERSE_CSS}}),
    },
}
