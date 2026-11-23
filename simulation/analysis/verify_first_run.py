"""Spot-checks incidents the players reported in the first run (2026-11-23) against its traces.

    python3 simulation/analysis/verify_first_run.py union zero afflict toolfail claims more multi

Tied to that run: it looks for particular lines in particular games. Kept as the evidence behind
simulation/findings/2026-11-23-first-run.md and as a pattern for checking the next run's reports.
"""
import collections
import json
import os
import re
import sys

PATH = os.environ.get("WW_TURNS") or os.path.join(os.path.dirname(__file__), "..", "runs", "export", "turns.jsonl")
turns = [json.loads(line) for line in open(PATH, encoding="utf-8")]
turns.sort(key=lambda t: (t["campaignId"], t["timestamp"]))
ok = [t for t in turns if t["outcome"] == "committed"]
which = set(sys.argv[1:])
game = lambda p: [t for t in ok if t["persona"] == p]
names = lambda t: [c["name"] for c in t["toolCalls"]]


def res(t):
    out = []
    for r in t["toolResults"]:
        try:
            out.append((r["name"], json.loads(r["result"])))
        except (ValueError, TypeError):
            out.append((r["name"], {"raw": str(r["result"])[:160]}))
    return out


def cut(text, pattern, before=100, length=420):
    m = re.search(pattern, text, re.I | re.S)
    return None if not m else text[max(m.start() - before, 0): m.start() + length].replace("\n", " ")


def st(t, *keys):
    return {k.replace("character", ""): t["gameState"].get(k) for k in keys}


if "zero" in which:
    for p in ("death-seeker", "haggler"):
        print(f"\n-- {p}: turns starting at 0 HP")
        for i, t in enumerate(game(p)):
            if t["gameState"].get("characterHp") == 0:
                combat = [(n, {k: r.get(k) for k in r if re.search(r"damage|hp|dead|broken|ended", k, re.I)}) for n, r in res(t)
                          if n in ("ResolveCombatRound", "ChallengeCharacter")]
                print(f"   {i} {t['stage']:11} {t['playerMessage'][:45]!r} delta hp {t['turnDelta']['hpChange']} died {t['turnDelta']['died']} afflictions {t['turnDelta']['newAfflictions']} {json.dumps(combat)[:330]}")

if "afflict" in which:
    words = {"LostEye": "eye", "StabbedLung": "lung", "BrokenHand": "hand", "CrushedFoot": "foot", "SeveredArm": "arm",
             "SmashedFace": "face|jaw|nose|teeth", "Infected": "infect|fester|rot", "DizzyFromMagic": "dizz|reel|spin"}
    total = unsaid = 0
    for t in ok:
        for a in (t["turnDelta"] or {}).get("newAfflictions") or []:
            total += 1
            key = next((v for k, v in words.items() if k.lower() in str(a).replace(" ", "").lower()), None)
            if key and not re.search(key, t["narrative"], re.I):
                unsaid += 1
                print(f"   [{t['persona']} #{t['orderIndex']}] new affliction {a!r} not in narration; tools {names(t)}")
    print(f"new afflictions: {total}, not mentioned in that turn's narration: {unsaid}")

if "toolfail" in which:
    lost = collections.Counter()
    claimed = []
    for t in ok:
        seq = [(r["name"], str(r["result"]).startswith("Error")) for r in t["toolResults"]]
        for name in {n for n, _ in seq}:
            outcomes = [bad for n, bad in seq if n == name]
            if outcomes[-1]:
                lost[name] += 1
                err = next(str(r["result"])[:150] for r in reversed(t["toolResults"]) if r["name"] == name)
                claimed.append((t, name, err))
    print("turns where a tool's LAST call failed:", dict(lost))
    for t, name, err in claimed[:16]:
        print(f"   [{t['persona']} #{t['orderIndex']}] {name}: {err!r}\n      narration end: {t['narrative'][-170:]!r}")

if "claims" in which:
    c = game("cheater")
    for pat in (r"roof-watchman", r"three hundred|300 silver|three hunched", r"draught is gone|pocket the staunching", r"One more hundred|sold the roof"):
        for i, t in enumerate(c):
            x = cut(t["narrative"], pat, 60, 330)
            if x:
                print(f"\ncheater {i} {t['playerMessage'][:110]!r}\n   tools {names(t)} delta {{silver {t['turnDelta']['silverChange']}, hp {t['turnDelta']['hpChange']}, added {t['turnDelta']['itemsAdded'][:3]}}}\n   {x!r}")
                break
    r = game("rules-lawyer")
    for pat in (r"You are on the roof now", r"\+1d6", r"offers you 45|45 silver"):
        for i, t in enumerate(r):
            x = cut(t["narrative"], pat, 260, 520)
            if x:
                print(f"\nrules-lawyer {i} {t['playerMessage'][:110]!r}\n   tools {names(t)} results {json.dumps([(n, {k: v for k, v in d.items() if k in ('isSuccess','roll','total','dr','damageTaken')}) for n, d in res(t) if n=='ChallengeCharacter'])}\n   {x!r}")
                break

if "more" in which:
    o = game("occultist")
    for i, t in enumerate(o):
        if re.search(r"elixir", t["playerMessage"], re.I) or "CastScroll" in names(t):
            print(f"\noccultist {i} {t['playerMessage'][:90]!r}\n   tools {names(t)} hp {t['gameState']['characterHp']} delta hp {t['turnDelta']['hpChange']} removed {t['turnDelta']['itemsRemoved']}\n   results {json.dumps(res(t))[:420]}")
    print("\noccultist scrolls on sheet:", collections.Counter(json.dumps(t["gameState"].get("characterScrolls"), ensure_ascii=False) for t in o))
    m = game("meta-poker")
    for pat in (r"Combat protocol", r"Spring hobbles", r"\b391\b"):
        for i, t in enumerate(m):
            x = cut(t["narrative"], pat, 200, 520)
            if x:
                print(f"\nmeta-poker {i} {t['playerMessage'][:120]!r}\n   {x!r}")
                break
    p = game("purple-prose")[-1]
    print(f"\npurple-prose last: {p['playerMessage'][:260]!r}\n   tools {names(p)}\n   {p['narrative'][:330]!r}")
    d = game("death-seeker")
    for i, t in enumerate(d):
        if t["playerMessage"] == "begin":
            print(f"\ndeath-seeker begin {i}: stage {t['stage']} tools {names(t)} silver {t['gameState']['characterSilver']} delta {t['turnDelta'] and {k: v for k, v in t['turnDelta'].items() if v}}\n   {t['narrative'][:260]!r}")
    h = game("haggler")
    for i, (a, b) in enumerate(zip(h, h[1:])):
        if not a["gameState"]["hasStabbedLung"] and b["gameState"]["hasStabbedLung"]:
            print(f"\nhaggler stabbed lung appears after turn {i}: {a['playerMessage'][:80]!r} tools {names(a)} results {json.dumps(res(a))[:400]}\n   narration mentions lung: {bool(re.search('lung', a['narrative'], re.I))}; afflictions in delta {a['turnDelta']['newAfflictions']}")

if "multi" in which:
    for i, t in enumerate(game("multi-action")[:14]):
        print(f"\n{i} {t['playerMessage'][:230]!r}\n   tools {names(t)} delta {{k: v for k, v in (t['turnDelta'] or {{}}).items() if v}}".replace("{k: v for k, v in (t['turnDelta'] or {}).items() if v}", json.dumps({k: v for k, v in (t['turnDelta'] or {}).items() if v})))
        print(f"   ...{t['narrative'][-200:]!r}")

if "union" in which:
    tools = sorted({tool["name"] for t in ok if t.get("request") for tool in t["request"]["tools"]} - {"Rest", "Roll"})
    rx = re.compile(rf"\b({'|'.join(tools)})\b|\b(I will|I'll|I can|I must|I would|let me) (call|invoke|apply)\b|\bthe tools?\b (say|report|appl|took|return|record)"
                    r"|\b(rest|item-use|buy|purchase|shop) tool\b|\bcall the\b.{0,30}\b(tool|action)\b|\b(consequenceOnFailure|currentHp|isDead|isBroken|damageTaken)\b"
                    r"|we are GM|The final output should|last (user|assistant) (message|query)", re.I)
    hit = [t for t in ok if rx.search(t["narrative"])]
    by = collections.Counter(t["persona"] for t in hit)
    print(f"turns with any machinery talk: {len(hit)} of {len(ok)} in {len(by)} games: {dict(by.most_common())}")
    q = [t for t in hit if "?" in t["playerMessage"]]
    allq = [t for t in ok if "?" in t["playerMessage"]]
    print(f"when the player's message contains a question: {len(q)} of {len(allq)}; otherwise: {len(hit) - len(q)} of {len(ok) - len(allq)}")
