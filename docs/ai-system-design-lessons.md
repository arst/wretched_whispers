# Designing Systems Around AI — Lessons & Reasoning

Captured from the Wretched Whispers build (AI game master over a deterministic
TTRPG domain). These are the decisions that held up and the reasoning behind
them — written to be re-read before designing the next AI-driven system.

The through-line: **the model is a narrator, not a source of truth.** Almost
every hard-won lesson is a corollary of that one sentence.

---

## 1. Draw a hard line between domain authority and model narration

**Decision:** The domain owns all state (HP, silver, inventory, stage, dice
outcomes). The model only *describes* what tools return. It never invents an
outcome, and it never mutates state directly.

**Why:** An LLM will fluently narrate things that never happened — "you spend 4
silver and pocket the map" — with total confidence. If narration is allowed to
*imply* state, the game state silently diverges from the story. Players trust
the prose; the prose lies. The only defense is to make the domain the single
writer of truth and reduce the model to a read-and-describe role.

**How it shows up:** derived stage machine (stage is computed from state, not
chosen by the model), combat resolved as one domain round per action, dice
rolled in the domain, difficulty (not the model) owns the dawn die.

**Remember:** For any state that matters, ask "can the model change this by
*saying* it changed?" If yes, that's a fabrication vector. Close it.

---

## 2. The fabrication bug class: a state-changing tool the prompt doesn't mandate

**The pattern (hit three times: BuyItem, then Rest and CastScroll):** a tool
exists and is available in a stage, but the stage prompt only mentions the
*activity* in passing ("the player can buy items, cast scrolls, rest"). The
model does the activity in prose and skips the tool. Silver never leaves, the
scroll is never spent, HP never heals.

**Root cause was never "the model is dumb."** It was a prompt gap: the tool was
available but not *mandated*. The fix is always "when the player does X, call
tool Y — never narrate the effect unless Y applied it."

**Remember:** Every state-mutating tool available in a context needs an
explicit, imperative mandate in that context's prompt. Availability ≠
instruction. Maintain the invariant: *tool surface and prompt coverage are two
lists that must match.*

---

## 3. Audit the whole surface — don't patch the reported case

**Decision:** After the BuyItem report, instead of fixing only BuyItem, we
built the full **tool × stage × prompt-coverage matrix** and found Rest and
CastScroll had the identical gap — before anyone hit them in play.

**Why:** Reactive per-tool fixes are whack-a-mole. A single bug report is a
sample from a class, not an isolated defect. The cheap move is to enumerate the
whole surface once and check the invariant across all of it.

**Remember:** When a bug is an *instance of a rule being violated*, grep for
every other place the same rule applies. One report → audit the class.

---

## 4. Guardrail prompts: state the principle, not an enumeration

**Decision:** The anti-fabrication rule started as a list — "never narrate an
item gained, consumed, thrown, destroyed; silver spent." "Bought" wasn't in the
list, so BuyItem slipped through. We rewrote it to lead with the *invariant*:
**"if a change is not in a tool's result, it did not happen"** — with the list
demoted to examples, explicitly "not limits."

**Why:** An enumerated guardrail always has a next hole. Every new tool needs a
new list entry, and the one you forget is the next bug. A principle covers
cases you haven't built yet.

**Remember:** Write guardrails as invariants the model can *derive* from, not
checklists it must match against. Examples illustrate; they must never be the
whole rule. This is the difference between a rule that scales with the system
and one that rots.

---

## 5. Enforce with architecture where you can, prompt where you must

**Decision:** Tools are physically scoped to stages (least-privilege: an agent
in Combat is *built* with only combat tools). The model literally cannot call a
tool that isn't in its current stage set.

**Why:** A prompt is a request; a missing tool is a wall. Anything you can make
*impossible* is stronger than anything you can make *discouraged*. This is what
killed the earlier "stage machine runaway" — not better wording, but removing
the tools that let it run away.

**Remember:** Layer the defenses. Architecture (tool scoping, domain
validation) is the wall; prompts are the guidance for what remains. Never rely
on a prompt for something the architecture could guarantee. But you still need
the prompt for *when* to use the tools the model does have — see #2.

---

## 6. Every prompt fix ships with an eval that encodes the failure

**Decision:** The workflow was always "prompt + eval." Each fix got a live eval
that reproduces the exact failure scenario ("buy the map for 4 silver" → assert
BuyItem was called) and fails without the fix.

**Why:** Prompt behavior is invisible and regresses silently — a later prompt
edit can reintroduce the bug with no compile error and no obvious symptom. The
eval is the only thing that makes the fix durable. It also *documents the
failure mode* better than any comment: the assertion message is the lesson.

**Remember:** A prompt change without an eval is an untested change. The eval is
not overhead — it's the regression guard and the living spec of the behavior.
Treat "prompt + eval" as one atomic unit of work.

---

## 7. Move decisions out of the model when determinism matters

**Decision:** Things that should be *consistent and fair* were pulled from the
model into the domain: the dawn die is set by difficulty, not chosen per-turn
by the model; stages are derived from state; combat resolution is deterministic
domain logic.

**Why:** Models are good at *variety* (prose, judgment calls, flavor) and bad
at *consistency* (the same input should reliably produce the same mechanical
result). Give the model the creative surface; keep the rules engine
deterministic. The model's discretion is a feature for narration and a bug for
mechanics.

**Remember:** For each responsibility, ask "do I want variety or consistency
here?" Consistency → domain. Variety → model. Don't let the model hold a knob
that needs to be reliable.

---

## 8. Testing against nondeterministic dependencies needs isolation

**Decision:** When a test asserted "the severity die was rolled once," it broke
because *character creation itself* rolls dice, polluting the mock's invocation
count. Fix: clear the mock's invocations after setup, before the assertion.

**Why:** AI/game systems are full of injected randomness (dice, sampling).
Shared nondeterministic dependencies leak between setup and the code under
test. Isolate the *specific* interaction you're asserting on.

**Remember:** Root-cause test failures too — don't loosen the assertion to make
it pass (that papers over the collision). Find *why* the count is off and
isolate it.

---

## Meta-lesson

The recurring shape of every problem here: **the model did something plausible
that wasn't backed by state.** The recurring shape of every good fix: **make
the domain authoritative, make the guardrail a principle, and encode the
failure as a test.** Design the next AI system so that the model's confident
fluency can never be mistaken for a fact.
