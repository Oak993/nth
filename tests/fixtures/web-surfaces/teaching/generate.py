# Web surfaces W17, Teaching (ED-NEW-web-w17-1). Writes claude.jsonl, codex.jsonl, copilot.jsonl, deepseek.jsonl and local.jsonl from
# teaching_data.py: one recording per line, {"id", "prompt", "expect", "events"}, the events in the provider's own wire format
# (stream-json messages for claude, app-server notifications for codex, session events for copilot, chat-completion chunks for
# deepseek and local). Marrow-authored synthetic transcripts, never captured from a live provider. Run:  python generate.py
import json
import os
import sys

sys.dont_write_bytecode = True
from teaching_data import ARTIFACTS, LEADS, PLAIN, PROMPTS, STYLE, VIEWS

HERE = os.path.dirname(os.path.abspath(__file__))
PROVIDERS = ["claude", "codex", "copilot", "deepseek", "local"]


def compact(value):
    return json.dumps(value, ensure_ascii=True, separators=(",", ":"))


def chunks_of_words(text, size=48):
    """Text in pieces of about `size` characters, cut after a space, the way a model streams."""
    pieces, current = [], ""
    for word in text.split(" "):
        piece = word if not current and not pieces else " " + word
        if len(current) + len(piece) > size and current:
            pieces.append(current)
            current = piece if pieces else word
        else:
            current += piece
    if current:
        pieces.append(current)
    assert "".join(pieces) == text, "chunking lost text"
    return pieces


def chunks_of(text, size=64):
    return [text[i:i + size] for i in range(0, len(text), size)] or [""]


def answer_of(provider, prompt_id, expect):
    """("plain", text) or ("view", lead, view) or ("artifact", lead, spec)."""
    if expect == "plain":
        return ("plain", PLAIN[provider][prompt_id])
    if expect == "view":
        return ("view", LEADS[provider][prompt_id], VIEWS[prompt_id][STYLE[provider]])
    lead, spec = ARTIFACTS[provider][prompt_id]
    return ("artifact", lead, spec)


def tool_call(answer):
    """The tool and its arguments for a view or an artifact."""
    if answer[0] == "view":
        return "ui_show", {"ui": answer[2]}
    spec = dict(answer[2])
    spec.pop("id", None)
    return "artifact_create", spec


def fenced_text(answer):
    """The answer text for a provider that writes fenced blocks."""
    if answer[0] == "plain":
        return answer[1]
    lead = answer[1]
    if answer[0] == "view":
        return lead + "\n\n```nth-ui\n" + answer[2].rstrip("\n") + "\n```"
    spec = answer[2]
    head = f'nth-artifact id={spec["id"]} kind={spec["kind"]} title="{spec["title"]}"'
    if spec["kind"] != "project":
        return lead + "\n\n```" + head + "\n" + spec["content"].rstrip("\n") + "\n```"
    inner = []
    for path, content in spec["files"].items():
        language = path.rsplit(".", 1)[-1]
        inner.append("```" + language + " path=" + path + "\n" + content.rstrip("\n") + "\n```")
    return lead + "\n\n````" + head + "\n" + "\n".join(inner) + "\n````"


def spoken_text(answer):
    return answer[1]


# ----------------------------------------------------------------------------------------------------------------------
# One format per provider.
# ----------------------------------------------------------------------------------------------------------------------

def claude(index, answer):
    msg = f"msg_{index:02d}"
    events = [{"type": "stream_event", "event": {"type": "message_start", "message": {"id": msg}}}]
    content = []
    block = 0
    text = spoken_text(answer)
    events.append({"type": "stream_event", "event": {"type": "content_block_start", "index": block, "content_block": {"type": "text", "text": ""}}})
    for piece in chunks_of_words(text):
        events.append({"type": "stream_event", "event": {"type": "content_block_delta", "index": block, "delta": {"type": "text_delta", "text": piece}}})
    events.append({"type": "stream_event", "event": {"type": "content_block_stop", "index": block}})
    content.append({"type": "text", "text": text})
    if answer[0] != "plain":
        name, arguments = tool_call(answer)
        tool_id = f"toolu_{index:02d}"
        full = "mcp__nth-ui__" + name
        block += 1
        events.append({"type": "stream_event", "event": {"type": "content_block_start", "index": block, "content_block": {"type": "tool_use", "id": tool_id, "name": full, "input": {}}}})
        for piece in chunks_of(compact(arguments)):
            events.append({"type": "stream_event", "event": {"type": "content_block_delta", "index": block, "delta": {"type": "input_json_delta", "partial_json": piece}}})
        events.append({"type": "stream_event", "event": {"type": "content_block_stop", "index": block}})
        content.append({"type": "tool_use", "id": tool_id, "name": full, "input": arguments})
    events.append({"type": "assistant", "message": {"id": msg, "model": "claude-fixture", "content": content}})
    return events


def codex(index, answer):
    base = {"threadId": "t1", "turnId": "turn-1"}
    message = f"m{index:02d}"
    text = spoken_text(answer)
    events = [{"method": "turn/started", "params": {"threadId": "t1", "turn": {"id": "turn-1"}}},
              {"method": "item/started", "params": {**base, "item": {"type": "agentMessage", "id": message, "text": ""}}}]
    for piece in chunks_of_words(text):
        events.append({"method": "item/agentMessage/delta", "params": {**base, "itemId": message, "delta": piece}})
    events.append({"method": "item/completed", "params": {**base, "item": {"type": "agentMessage", "id": message, "text": text}}})
    if answer[0] != "plain":
        name, arguments = tool_call(answer)
        call = f"call{index:02d}"
        item = {"type": "mcpToolCall", "id": call, "server": "nth-ui", "tool": name, "arguments": arguments}
        events.append({"method": "item/started", "params": {**base, "item": {**item, "status": "inProgress"}}})
        events.append({"method": "item/completed", "params": {**base, "item": {**item, "status": "completed", "result": {"content": [{"type": "text", "text": "Done."}]}}}})
    return events


def copilot(index, answer):
    message = f"cm{index:02d}"
    text = fenced_text(answer)

    def event(kind, data):
        return {"method": "session.event", "params": {"sessionId": "s-1", "event": {"type": kind, "data": data}}}

    events = [event("assistant.message_delta", {"messageId": message, "deltaContent": piece}) for piece in chunks_of_words(text)]
    events.append(event("assistant.message", {"messageId": message, "content": text}))
    return events


def completion(delta):
    return {"choices": [{"delta": delta}]}


def deepseek(index, answer):
    events = [completion({"role": "assistant", "content": ""})]
    for piece in chunks_of_words(spoken_text(answer)):
        events.append(completion({"content": piece}))
    if answer[0] != "plain":
        name, arguments = tool_call(answer)
        call = f"call_{index:02d}"
        events.append(completion({"tool_calls": [{"index": 0, "id": call, "type": "function", "function": {"name": name, "arguments": ""}}]}))
        for piece in chunks_of(compact(arguments), 48):
            events.append(completion({"tool_calls": [{"index": 0, "function": {"arguments": piece}}]}))
    return events


def local(index, answer):
    events = []
    for piece in chunks_of_words(fenced_text(answer), 40):
        events.append(completion({"content": piece}))
    return events


FORMATS = {"claude": claude, "codex": codex, "copilot": copilot, "deepseek": deepseek, "local": local}


def main():
    for provider in PROVIDERS:
        lines = []
        for index, (prompt_id, prompt, expect) in enumerate(PROMPTS, start=1):
            answer = answer_of(provider, prompt_id, expect)
            lines.append(compact({"id": prompt_id, "prompt": prompt, "expect": expect, "events": FORMATS[provider](index, answer)}))
        with open(os.path.join(HERE, provider + ".jsonl"), "w", encoding="ascii", newline="\n") as handle:
            handle.write("\n".join(lines) + "\n")
        print(provider, len(lines), "recordings")


if __name__ == "__main__":
    main()
