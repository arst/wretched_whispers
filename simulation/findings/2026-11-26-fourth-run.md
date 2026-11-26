# Fourth run: 26 November 2026

The same eleven personas, one game each, 30-turn limit, on `main` at `93bf7fd`: Tasks 17 to 21 of the
plan merged (pull requests #212 to #215). Game model: gpt-5-mini on Azure. Players: Claude Haiku.
Clean database.

This run answers: did the death verdict, the hours on a test, the scroll and food tools and the rolled
silver hold in play, and does the story still run ahead of the sheet late in a game (Task 21)?

## The run

| | Third run | Fourth run |
|---|---|---|
| Games (characters) | 11 (26) | 11 (18) |
| Turn records, completed | 327, 316 | 335, 319 |
| Failed on HTTP 429 | 3 attempts | 11 attempts |
| Turns the player saw fail | 4 | 6, all on 429 |
| Refused because the character was dead | 6 | 5 |
| Game model usage | about 6.2M in, 0.4M out | about 7.5M in, 0.5M out |
| Turn time, completed turn (mean) | 28 s | 31 s |

Five players at a time drew nine 429s in the first wave, so the rest ran four at a time. Five is no
longer safe on this deployment.

## What moved

Counts are from `analysis/analyze.py`; every example below was read in the trace.

| Check | Third run | Fourth run |
|---|---|---|
| Death told as survival, or not told | 4 of 16 deaths | 0 of 10 |
| Replies stating an hour the sheet does not have | 11 of 55 | 2 of 54, one game |
| Cast failed on the scroll's name | 9 | 0 |
| Names a tool | 3 turns, 3 games | 1 turn |
| Talks about calling or using tools | 9 turns, 5 games | 9 turns, 5 games |
| Reply cut: second messages removed, real text lost | 9, 2 lost | 8, 0 lost |
| Silver granted on a demand from outside the story | none, on 2 | none, on 2 |
| Successors whose opening turn is in Combat | 0 of 15 | 0 of 7 |
| Median reply to a message of 25 characters or fewer | 853 chars | 693 chars |

Notes:

- **Deaths.** All ten were told as deaths, and the status line agreed each time. Four came outside
  combat, where the third run's four failures all were: three failed tests and one failed cast, all
  told as deaths. The other six came in combat rounds. The death-seeker went through four
  characters; each "I get up anyway" was refused.
- **The reply cut.** Of the eight second messages removed, four were an exact copy of the reply, two
  were filler ("(The previous message already concluded the turn.)") and two told an earlier scene
  again. The player saw every reply whole.
- **The clock.** 131 hours passed; 27 of them came from `hoursTaken` on 67 tests. The "hours told,
  clock still" check found 19 turns, but 17 are item text ("steadies the hands for an hour") or an
  offer ("it will take an hour"). Two are real: "You walk for an hour" with no move, and "One hour
  passes while you eat".
- **New tools in use.** One found scroll went through `AddScrollToCharacter`; four rations were
  eaten; `GainSilver` rolled eight finds, from 3 to 17 silver.

## What is still wrong

### 1. Combat results are read as the enemy's rolls

Verified. A retaliation in `ResolveCombatRound`'s result carries `roll`, `modifier` and `dr`, but those
are the player's defence against the attack, not the enemy's attack. The narrator reads them the other
way round:

- `roll 13, dr 12, avoided true` (the defence held) told as "It must beat DR 12 and it rolls 13 ...
  it snaps; still ... the bite deals 0".
- `roll 19, dr 12, avoided true` told as "19 rolled against DR 12, but the attack fails to connect".
- `roll 14, modifier -1, dr 18, avoided false, armorReduction 4, damageDealt 0` told as "14, -1, to beat
  18 ... its kukri opens for 1, eaten by your heavy armor".

The players saw a roll that beat the DR and missed, and one that missed the DR and hit. The same goes
for `fumbleDoubleDamage`, which is the player's natural 1 on the dodge: it was told as "its strike
fumbles into monstrous damage". That the roll is the player's Agility dodge is written only in a C#
comment on `RetaliationDto`, which the model never sees. The third run's "enemies given dice the game
does not roll" is the same misreading. The fix is in the result's names, not the prompt.

### 2. The story still runs ahead of the sheet, late in a game

Verified incidents (Task 21's slips, which did not show in short evals):

- The cheater declared "a chest of 300 silver I remember from last week". The narrator found the chest
  and wrote "Gain: 300 silver", with only a test called. Silver did not move. Next turn it called
  `GainSilver(Cache)` and told the 17 the dice gave. Two rules broken in one turn: a stash the player
  declares is not in the world, and no sum is named before `GainSilver` returns. The dice held.
- The multi-action player stole a pan and sold it in the same message: "You now have 10 silver. The pan
  is gone", with only a test called. Two turns later the narrator paid it through `GainSilver(Purse)`.
- The haggler's purchase of jerky and bread was told done after a haggle test; nothing was bought.
  Next turn: "Mechanically, no purchase tool was called".
- Items handed over stayed in the pack: a vellum strip traded to a relic-dealer (occultist; the turn
  bought a scroll and removed nothing), a feather laid on a threshold (purple-prose; removed 13 turns
  later), gravebloom traded for a secret (rules-lawyer; removed only after the player pointed at it).

### 3. `GainSilver` used for the wrong thing

- The cautious-survivor paid 12 for a draught. The narrator called `GainSilver(Purse, "middling draught
  sale (merchant took coin)")`: a purchase made silver rise. The silver the player saw went 30, 36, 24.
- The multi-action sale above was paid through `GainSilver` instead of `SellItem`.

This is a side effect of Task 20. Before it, a payment pushed through `GainSilver` carried a negative
amount and was refused (-20 in the second run, -10 in the third). With no amount to give, the same
mistake now pays a rolled purse. `BuyItem` was there for the purchase; the pan was never in the pack,
so `SellItem` could not take it. There is also no tool that pays silver for a service or a gift.

### 4. Tool talk

- Asked what tools it can call, the narrator refused and then listed what it does by name: "I'll call
  SellItem to remove the item and pay silver", "I will call Rest for 8 hours" (meta-poker).
- The same shape as before elsewhere: "I will call the tool that spends it", "the market tool returned
  a younger accounting" (cautious-survivor), "call the tool that gives you silver" (multi-action).
- The narrator answered "17 × 23" and "the capital of France", and wrote a spring poem on the second
  ask.

### 5. The sheet does not show food or castings

Food days and the day's scroll castings are on the character and in the narration, but not in the
state the screen gets. The rules-lawyer asked for it three times; the narrator answered "The status
line is corrected here and now: Food: 1 day", which it cannot do.

### Smaller

- A stray token ended one reply: "What do you do?ondheid" (confused-newbie).
- A rest at full health (1 of 1) was explained as "the roll gave you nothing", then as "already at
  your maximum" (cautious-survivor). Rests still return `hpHealed: 0` with no reason.

## Player reports that did not hold

- **"The same item is listed five or seven times"** (four players): the state sends one entry per unit
  on purpose, and the web UI groups them as "lantern + oil ×7". The simulation's screen
  (`sim-player.mjs`) prints the raw list, so players saw what a real player does not. The analysis
  check "Sheet lists the same item more than once" counts the same thing and means nothing.
- **"Narration says dead, character did not die"** (two turns): a rules summary ("if you die, it is
  permanent") and an opening line ("choose how you die").
- **"Enemy rolls contradict the outcome"**: true as seen, but the dice and the domain agree; finding 1
  is the narrator reading the defence roll as the attack.

Reported, not checked: the narrator doing the first action of a chain and asking to confirm the rest
(multi-action; plan item 3.8); silver scattered on a threshold and poured on a ledger while the sheet
stayed at 110 (purple-prose; the ledger turn's narration does not say coins were taken); the omen rule
stated two ways in one game (rules-lawyer).

The multi-action player's "purchases I never asked for" did not hold: both turns' messages asked for
them ("then buy armor if they have any", "buy a hot meal with the 32 silver").

## Limits

- One game per persona, synthetic players. The large moves (deaths, the hour, scroll casts) are
  evidence; a difference of a few turns is not.
- Food and castings missing from the screen is a finding about the web UI too, since the simulation's
  screen and the UI read the same state.
- English only.
