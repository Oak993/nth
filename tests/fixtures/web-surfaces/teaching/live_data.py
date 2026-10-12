# Web surfaces W24, live-answer tools (DESIGN.md 7.3, 14 row W24; ED-NEW-web-w24-5). The answers five providers' models are recorded as
# giving when they change a live view they showed earlier (ui_update, or the whole block again with the same id) and when they make a small
# HTML widget (html_widget, or a fenced nth-html block). Marrow-authored synthetic transcripts in our own words, never captured from a live
# provider; generate.py turns this data into each provider's wire format (live-<provider>.jsonl), and LiveAnswerLiveRecordingTests replays
# them after setting up the earlier view they change. A Local model has no HTML widget (the lite subset), so it has no widget recording.
#
# A live prompt expects: update (the earlier view changes in place, or the same id is shown again) or widget (one HTML widget).

LIVE_PROMPTS = [
    ("11-update", "Make that five hundred a month, and say what it changes.", "update"),
    ("12-widget", "Make me a quick tap counter game.", "widget"),
]

# The view the earlier turn showed. The tests set it up before they replay an update.
SAVINGS_BASE = r'''card "Savings goal" icon=piggy-bank
  state goal = 12000
  state monthly = 400
  let months = ceil(goal / monthly)
  slider monthly label="Per month" min=50 max=2000 step=50 unit="$"
  stat "Months to goal" = months
  text "At 400 a month it takes 30 months." id=note
'''

# What the model asks for to change it: the control jumps to 500 and the sentence under it is replaced.
UPDATE = {
    "id": "savings",
    "patch": {
        "state": {"monthly": 500},
        "nodes": {"note": 'text "At 500 a month it takes 24 months, six months sooner." id=note'},
    },
}

# The same view written out again for a model that writes fenced blocks (the same id replaces it).
SAVINGS_AGAIN = r'''card "Savings goal" icon=piggy-bank
  state goal = 12000
  state monthly = 500
  let months = ceil(goal / monthly)
  slider monthly label="Per month" min=50 max=2000 step=50 unit="$"
  stat "Months to goal" = months
  text "At 500 a month it takes 24 months, six months sooner." id=note
'''

SAVINGS_AGAIN_LITE = r'''card "Savings goal"
  state monthly = 500
  let months = ceil(12000 / monthly)
  slider monthly label="Per month" min=50 max=2000 step=50 unit="$"
  stat "Months to $12,000" = months
'''

WIDGET_HTML = r'''<!doctype html>
<html>
<head>
<meta charset="utf-8">
<title>Tap counter</title>
<style>
  body { font: 16px system-ui, sans-serif; display: grid; place-items: center; height: 100vh; margin: 0; background: #1c1c1f; color: #f2f2f4; }
  button { font: inherit; padding: 18px 34px; border: 0; border-radius: 16px; background: #4b9ffc; color: #08121f; }
  output { display: block; text-align: center; font-size: 56px; margin-bottom: 14px; }
</style>
</head>
<body>
<main>
  <output id="n">0</output>
  <button id="tap">Tap</button>
</main>
<script>
  let n = 0;
  document.getElementById("tap").addEventListener("click", () => {
    n += 1;
    document.getElementById("n").textContent = String(n);
  });
</script>
</body>
</html>
'''

WIDGET_ARGUMENTS = {"html": WIDGET_HTML, "height": 300, "title": "Tap counter"}

LIVE_LEADS = {
    "claude": {
        "11-update": "At 500 a month you get there in 24 months, six months sooner. I moved the slider and updated the line under it.",
        "12-widget": "Here is a counter you can tap. It keeps the count while this page is open.",
    },
    "codex": {
        "11-update": "Five hundred a month reaches the goal in 24 months. I changed the slider and the sentence below it.",
        "12-widget": "Here is a tap counter. Press the button and the number goes up.",
    },
    "copilot": {
        "11-update": "At 500 a month it takes 24 months, six months sooner. Here is the plan again with the slider moved.",
        "12-widget": "Here is a tap counter; press the button to count.",
    },
    "deepseek": {
        "11-update": "With 500 a month the goal takes 24 months, six months sooner. I set the slider and changed the line below it.",
        "12-widget": "Here is a small tap counter. Each press adds one.",
    },
    "local": {
        "11-update": "At 500 a month it takes 24 months. Here is the plan again with the slider moved.",
    },
}
