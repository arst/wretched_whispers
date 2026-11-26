"""Measures failure modes across the exported turns: counts per game and a few examples of each.

    python3 simulation/analysis/analyze.py [voice] [dup] [silver] [hp] [world] [items] [tools] [fixes] [size]

No arguments runs every section. Reads simulation/runs/export/turns.jsonl (see sim.sh export), or the
file named by WW_TURNS. The checks are text patterns over the narration compared with the recorded
state, so the counts are approximate: read the printed examples before quoting a number.
"""
import collections
import difflib
import json
import os
import re
import sys

PATH = os.environ.get("WW_TURNS") or os.path.join(os.path.dirname(__file__), "..", "runs", "export", "turns.jsonl")
turns = [json.loads(line) for line in open(PATH, encoding="utf-8")]
turns.sort(key=lambda t: (t["campaignId"], t["timestamp"]))
ok = [t for t in turns if t["outcome"] == "committed"]

# The state the NEXT committed turn of the same character saw = this turn's resulting state.
after = {}
for a, b in zip(ok, ok[1:]):
    if a["chatSessionId"] == b["chatSessionId"]:
        after[a["turnId"]] = b["gameState"]

TOOLS = sorted({tool["name"] for t in ok if t.get("request") for tool in t["request"]["tools"]} - {"Rest", "Roll"})
which = set(sys.argv[1:])


def show(title, hits, n=3, width=230):
    by = collections.Counter(t["persona"] for t, _ in hits)
    print(f"\n== {title}: {len(hits)} of {len(ok)} turns, {len(by)} of {len({t['campaignId'] for t in ok})} games  {dict(by.most_common())}")
    for t, excerpt in hits[:n]:
        print(f"   [{t['persona']} #{t['orderIndex']}] {t['playerMessage'][:70]!r} -> {excerpt[:width]!r}")


def around(text, match, before=80, length=230):
    return text[max(match.start() - before, 0): match.start() - before + length + before]


def find(pattern, flags=0):
    rx = re.compile(pattern, flags)
    out = []
    for t in ok:
        m = rx.search(t["narrative"])
        if m:
            out.append((t, around(t["narrative"], m)))
    return out


def calls(t, name):
    return [c for c in t["toolCalls"] if c["name"] == name]


def results(t, name):
    out = []
    for r in t["toolResults"]:
        if r["name"] == name:
            try:
                out.append(json.loads(r["result"]) if isinstance(r["result"], str) else r["result"])
            except (ValueError, TypeError):
                out.append({"raw": r["result"]})
    return out


if not which or "voice" in which:
    names = "|".join(TOOLS)
    show("Names a tool", find(rf"\b({names})\b"))
    show("Talks about calling/using tools",
         find(r"\b(I will|I'll|I can|I must|I would|let me) (call|invoke|apply)\b|\bthe tools?\b (say|report|appl|took|return|record)"
              r"|\b(rest|item-use|buy|purchase|shop) tool\b|\bcall the\b.{0,30}\b(tool|action)\b", re.I))
    show("Field names from tool results", find(r"\b(consequenceOnFailure|currentHp|isDead|isBroken|damageTaken|isSuccess|silverCost|challengeDr)\b"))
    show("Run-on: writes about the conversation itself",
         find(r"we are GM|The final output should|last (user|assistant) (message|query)|as the assistant|the user (has|hasn't|said|asked|wants)|system prompt says"
              r"|\bthe assistant\b|ChatGPT|two (identical |assistant |\"final\" )?(final )?messages|previous message already ended"
              r"|I repeated last message|Assistant notes|must respond (as GM|to the player)|await player's input", re.I), n=4)
    show("'the party' in a solo game", find(r"\bthe party\b", re.I), n=2)
    # Since the fix of 23 November 2026 the game cuts a second message off the reply and keeps it here.
    show("Second message cut off the reply (kept from the player)",
         [(t, t["suppressedNarrative"].strip()) for t in ok if t.get("suppressedNarrative")], n=5, width=160)

if not which or "dup" in which:
    hits = []
    for t in ok:
        paras = [p.strip() for p in re.split(r"\n\s*\n", t["narrative"]) if len(p.strip()) >= 120]
        for i, p in enumerate(paras):
            twin = next((q for q in paras[i + 1:] if difflib.SequenceMatcher(None, p, q).ratio() > 0.75), None)
            if twin:
                hits.append((t, p))
                break
    show("Repeats a paragraph within one reply", hits, n=2, width=160)
    multi = [t for t, _ in hits if [e["type"] for e in t["events"] or []].count("narrative") > 1]
    print(f"   of those, replies written in more than one piece around tool calls: {len(multi)}")
    pieces = [t for t in ok if [e["type"] for e in t["events"] or []].count("narrative") > 1]
    print(f"   all replies written in more than one piece: {len(pieces)}")

if not which or "silver" in which:
    hits = []
    total = 0
    for t in ok:
        for c in calls(t, "AddItemToCharacterInventory"):
            desc = str(c["arguments"].get("itemDescription", ""))
            if re.search(r"silver|coin|purse|pouch|money", desc, re.I) and not re.search(r"kingdom that never existed", desc):
                hits.append((t, f"{desc} x{c['arguments'].get('quantity')}"))
                total += 1
    show("Adds silver/coins as an inventory item", hits, n=8, width=120)
    # Narration states a silver total that the next turn's sheet does not show.
    rx = re.compile(r"(?:[Ss]ilver(?: is| now|:| stands at| total(?: is)?)\s*(\d+)\b|you (?:now )?(?:have|hold|carry) (\d+) silver|(\d+) silver (?:remain|left|in your purse))")
    wrong, checked = [], 0
    for t in ok:
        post = after.get(t["turnId"])
        if not post:
            continue
        claims = {int(next(g for g in m.groups() if g)) for m in rx.finditer(t["narrative"])}
        if claims:
            checked += 1
            if post["characterSilver"] not in claims:
                wrong.append((t, f"narrated {sorted(claims)} / sheet after {post['characterSilver']}"))
    print(f"\n   replies that state a silver total: {checked}")
    show("States a silver total the sheet does not have", wrong, n=6, width=80)
    gains = []
    rx_gain = re.compile(r"(pays? you|hands? you|gives? you|you (?:receive|earn|pocket|collect|take|find|gain)|presses|slides|counts out)[^.]{0,60}?\b(\d+) silver", re.I)
    for t in ok:
        m = rx_gain.search(t["narrative"])
        if m and (t["turnDelta"] or {}).get("silverChange", 0) <= 0:
            gains.append((t, around(t["narrative"], m, 20, 160)))
    show("Narrates silver gained while the silver count did not rise", gains, n=5, width=180)
    names = collections.Counter(tool["name"] for t in ok[:1] for tool in t["request"]["tools"])
    print("   tools that can change silver:", [tool["name"] for tool in ok[1]["request"]["tools"] if re.search(r"silver", json.dumps(tool), re.I)])

if not which or "hp" in which:
    rx = re.compile(r"\bHP(?: is| now| falls to| drops to|:)?\s*(?:now )?(\d+)\s*/\s*(\d+)|(\d+)\s*/\s*(\d+) HP|(\d+) of (\d+) HP")
    wrong, checked = [], 0
    for t in ok:
        post = after.get(t["turnId"])
        if not post or post.get("characterHp") is None:
            continue
        claims = {int(next(g for g in m.groups() if g)) for m in rx.finditer(t["narrative"])}
        if claims:
            checked += 1
            if post["characterHp"] not in claims:
                wrong.append((t, f"narrated HP {sorted(claims)} / sheet after {post['characterHp']}/{post['characterMaxHp']}"))
    print(f"\n   replies that state an HP total (and have a following turn): {checked}")
    show("States an HP total the sheet does not have", wrong, n=6, width=80)

    died = [t for t in ok if (t["turnDelta"] or {}).get("died")]
    print(f"\n   turns on which the character died: {len(died)}")
    denial = [(t, around(t["narrative"], m)) for t in died for m in [re.search(r"not dead|not yet dead|still (alive|breath)|you (live|survive)", t["narrative"], re.I)] if m]
    show("Died this turn, narration says alive", denial, n=4)
    silent = [(t, t["narrative"][-200:]) for t in died if not re.search(r"\b(dead|die[sd]?|dies|slain|death|killed|do not rise|corpse|last breath|gone)\b", t["narrative"], re.I)]
    show("Died this turn, narration never says so", silent, n=4)
    false_death = [(t, around(t["narrative"], m)) for t in ok if not (t["turnDelta"] or {}).get("died")
                   for m in [re.search(r"\byou are dead\b|\byou die\b|\bis slain\b|\byou are slain\b", t["narrative"], re.I)] if m]
    show("Narration says dead, character did not die", false_death, n=4)

    zero = []
    for t in ok:
        if t["gameState"].get("characterHp") == 0 and not t["gameState"].get("isDead"):
            dmg = [r.get("damageTaken") for name in ("ChallengeCharacter",) for r in results(t, name) if r.get("damageTaken")]
            zero.append((t, f"acts at 0 HP; damage this turn {dmg}; died {(t['turnDelta'] or {}).get('died')}"))
    show("Character acting at 0 HP", zero, n=8, width=90)

if not which or "world" in which:
    per = collections.defaultdict(lambda: collections.Counter())
    for t in ok:
        p = per[t["persona"]]
        p["turns"] += 1
        p["moves"] += len(calls(t, "SetPartyLocation"))
        p["poi"] += len(calls(t, "RecordPointOfInterest"))
        p["time"] += len(calls(t, "AdvanceTime"))
        p["hours"] += (t["turnDelta"] or {}).get("hoursElapsed", 0)
    print("\n== Location and clock, per game")
    for persona, p in sorted(per.items()):
        locs = collections.Counter(t["gameState"].get("currentLocationName") for t in ok if t["persona"] == persona)
        print(f"   {persona:18} turns {p['turns']:3}  SetPartyLocation {p['moves']:2}  places charted {p['poi']:2}  AdvanceTime {p['time']:2}  hours {p['hours']:3}  locations on sheet {len(locs)}")
    rx = re.compile(r"\b[Hh]our(?: is| now|:)?\s*(?:now )?(\d+)\b")
    wrong, checked = [], 0
    for t in ok:
        post = after.get(t["turnId"])
        if not post:
            continue
        claims = {int(m.group(1)) for m in rx.finditer(t["narrative"])}
        if claims:
            checked += 1
            day_hour = post["currentHour"]
            if day_hour not in claims and (post["currentDay"] - 1) * 24 + day_hour not in claims:
                wrong.append((t, f"narrated hour {sorted(claims)} / sheet after day {post['currentDay']} hour {day_hour}"))
    print(f"   replies that state the hour: {checked}")
    show("States an hour the sheet does not have", wrong, n=5, width=80)
    passed = [(t, around(t["narrative"], m, 30, 150)) for t in ok
              for m in [re.search(r"\b(an|one|two|three|several|a few|\d+) hours? (pass|later|slip|of|go by)|after an hour|for an hour|spent an hour", t["narrative"], re.I)]
              if m and (t["turnDelta"] or {}).get("hoursElapsed", 0) == 0]
    show("Narrates hours passing while the clock did not move", passed, n=4, width=170)

if not which or "items" in which:
    biggest = max(ok, key=lambda t: len(t["gameState"].get("characterInventory") or []))
    print(f"   longest carried list: {len(biggest['gameState']['characterInventory'])} entries ({biggest['persona']})")

if not which or "tools" in which:
    failed = collections.Counter()
    examples = {}
    for t in ok:
        for r in t["toolResults"]:
            text = r["result"] if isinstance(r["result"], str) else json.dumps(r["result"])
            if re.search(r'"(error|isSuccess)"\s*:\s*(false|")|\bError\b|not enough|cannot|does not have|insufficient|Invalid', text) and '"isSuccess": true' not in text and r["name"] != "ChallengeCharacter":
                failed[r["name"]] += 1
                examples.setdefault(r["name"], (t, text.replace("\n", " ")[:200]))
    print("\n== Tool calls that came back refused or failed:", dict(failed.most_common()))
    for name, (t, text) in examples.items():
        print(f"   {name} [{t['persona']} #{t['orderIndex']}]: {text}")
    used = collections.Counter(c["name"] for t in ok for c in t["toolCalls"])
    print("   tool usage:", dict(used.most_common()))
    opening = [(t, str([c["name"] for c in t["toolCalls"]])) for t in ok if t["playerMessage"] == "begin"
               and any(c["name"] in ("ResolveCombatRound", "ChallengeCharacter", "BuyItem", "StartEncounter", "CreateEncounter", "AdvanceTime", "UseItemFromCharacterInventory") for c in t["toolCalls"])]
    show("Opening turn that already acts for the player", opening, n=6, width=200)

if not which or "fixes" in which:
    # What the first run's capability fixes should have moved (findings 1, 6, 7, 8, 9, 10).
    print("\n== Capability fixes")
    delta = lambda t: t["turnDelta"] or {}
    tools = lambda t: [c["name"] for c in t["toolCalls"]]
    show("Silver rose", [(t, f"+{delta(t)['silverChange']} via {tools(t)}") for t in ok if delta(t).get("silverChange", 0) > 0], width=120)
    sells = [t for t in ok if re.search(r"\b(sell|sold|pawn|trade)\b", t["playerMessage"], re.I)]
    # A sale and a purchase in one turn can net to a loss, so a sale that went through counts as paid.
    sold = lambda t: any(r["name"] in ("SellItem", "GainSilver") and not str(r["result"]).startswith("Error") for r in t["toolResults"])
    paid = [t for t in sells if delta(t).get("silverChange", 0) > 0 or sold(t)]
    print(f"   turns where the player sells or trades: {len(sells)}; paid in silver: {len(paid)}")
    grants = [(t, f"{c['arguments']}") for t in ok for c in calls(t, "GainSilver")]
    show("Silver granted (read each: is the source in the fiction?)", grants, n=12, width=110)
    show("Sells or trades, silver did not rise", [(t, f"tools {tools(t)}; ...{t['narrative'][-140:]}") for t in sells if t not in paid], n=5)

    uses = [(t, str(r["result"])) for t in ok for r in t["toolResults"] if r["name"] == "UseItemFromCharacterInventory"]
    print(f"   use-item calls: {len(uses)}")
    show("Use-item call failed on the item's name", [(t, text[:120]) for t, text in uses if "No item matching" in text], width=120)

    words = {"losteye": "eye", "stabbedlung": "lung", "brokenhand": "hand", "crushedfoot": "foot", "severedarm": "arm",
             "smashedface": "face|jaw|nose|teeth", "infected": "infect|fester|rot"}
    injuries = [(t, a, words[k]) for t in ok for a in delta(t).get("newAfflictions") or []
                for k in [str(a).replace(" ", "").lower()] if k in words]
    print(f"   new injuries: {len(injuries)}")
    show("New injury not named in that turn's narration",
         [(t, f"{a}; tools {tools(t)}") for t, a, word in injuries if not re.search(word, t["narrative"], re.I)], n=6, width=120)

    show("Sheet carries a scroll named 'random'",
         [(t, str(t["gameState"]["characterScrolls"])) for t in ok if re.search(r"\brandom\b", str(t["gameState"].get("characterScrolls")), re.I)], n=2)
    show("Cast failed on the scroll's name", [(t, str(r["result"])[:150]) for t in ok for r in t["toolResults"]
                                              if r["name"] == "CastScroll" and "No scroll matching" in str(r["result"])], width=150)
    casts = [(t, json.dumps(r)[:200]) for t in ok for r in results(t, "CastScroll")]
    show("Scroll casts", casts, n=4, width=200)

    begins = [t for t in ok if t["playerMessage"] == "begin" and t["stage"] != "CampaignSetup"]
    print(f"   successors' opening turns: {len(begins)}")
    show("Successor opens in Combat", [(t, f"tools {tools(t)}") for t in begins if t["stage"] == "Combat"])

    hashes = collections.Counter((t["stage"], t["promptHash"]) for t in ok)
    print(f"   distinct prompt hashes: {len(hashes)} over {len(ok)} turns")

if not which or "size" in which:
    lens = sorted(len(t["narrative"]) for t in ok)
    print(f"\n== Reply length (chars): median {lens[len(lens)//2]}, 90th pct {lens[int(len(lens)*.9)]}, max {lens[-1]}")
    short = [t for t in ok if len(t["playerMessage"]) <= 25 and t["playerMessage"] != "begin"]
    sl = sorted(len(t["narrative"]) for t in short)
    print(f"   replies to messages of 25 chars or fewer ({len(short)}): median {sl[len(sl)//2]}")
    it = collections.Counter(t["iterations"] for t in ok)
    print("   model requests per turn:", dict(sorted(it.items())))
    seen_error = collections.Counter()
    last = {}
    for t in turns:
        last[t["turnId"]] = t
    for t in last.values():
        if t["outcome"] == "failed":
            seen_error[t["persona"]] += 1
    retried = len({t["turnId"] for t in turns if t["outcome"] == "failed"}) - sum(seen_error.values())
    print(f"   turns the player saw fail: {sum(seen_error.values())} {dict(seen_error)}; turns that failed first and then recovered on a retry: {retried}")
