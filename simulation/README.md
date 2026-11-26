# Simulated players

Plays many games against the real server with scripted personas, to produce turn traces for building
evals. Each player is a small Claude agent that reads what a player's screen would show and types
what that kind of player would type.

This finds failure modes; it does not measure how often they happen in real play. Synthetic players
are more coherent and more cooperative than people.

## What is here

| Path | What it is |
|---|---|
| `sim.sh` | Starts and stops the stack the players play against, and exports the traces. |
| `sim-player.mjs` | The player's hands: `new`, `say`, `successor`. Talks to the game's HTTP API and prints the player's screen. |
| `personas/` | One file per kind of player: who they are, how they write, what they want. |
| `analysis/analyze.py` | Measures failure modes across an export: counts per game, with examples. |
| `analysis/verify_first_run.py` | The incident checks behind the first run's findings. |
| `findings/` | What each run turned up, ranked. |
| `runs/` | Git-ignored. Session cookies, `manifest.jsonl` (which persona played which session), the server log and the exports. Earlier waves are kept in `runs/first-run/` and `runs/second-run/`, each with a database dump; point `WW_TURNS` at their `export/turns.jsonl` to analyse them. |
| `../.claude/agents/ww-player.md` | The player agent itself. Claude Code only loads agents from that folder, so it lives there. |

## Running a wave

```bash
./simulation/sim.sh up          # Postgres on :5433, migrations, the API on :5101, five turns at once
```

Then, in Claude Code, spawn one `ww-player` agent per persona, all in one message so they run side by
side. The prompt for each is:

```
Play one game of Wretched Whispers. Run every command from the repository root, and prefix every
command with `WW_API=http://localhost:5101 ` because the game server for this run is on that port.

Persona slug: <slug>
Turn limit: 30
<the persona file, from "Start with" to the end>
```

When they have all reported back:

```bash
./simulation/sim.sh export      # runs/export/traces.json and runs/export/turns.jsonl
python3 simulation/analysis/analyze.py
./simulation/sim.sh down        # the games stay in the ww-sim-pg docker volume
```

`turns.jsonl` has one line per turn attempt, labelled with its persona: the player's message, the
narration, tool calls and results, the state before, the delta after, the full request sent to the
model, per-request token usage, and the ordered list of what the player was shown.

## Things to know

- **Four players at a time, at most.** Seven at once got HTTP 429 from the gpt-5-mini deployment on
  26 turn attempts. Five got none in the first run, 8 in the second and 9 in the first wave of the
  fourth, so the fourth's later waves and the fifth run used four.
  `sim.sh up 10` raises the server's limit, but the model's quota is the real ceiling.
- **Haiku is enough for the player.** A game costs the player roughly 20k to 60k tokens. The game's
  own model uses far more: about 500k input tokens for a 30-turn game.
- **A player's report is a lead, not a finding.** In the first run several reports were wrong about
  what happened. Check each against the traces before acting on it.
- **The exports hold every message and the full prompts.** `runs/` is git-ignored; keep it that way.
- **Starting clean.** Games accumulate in the `ww-sim-pg` volume across waves. To start over, first
  keep the last wave: `docker exec ww-sim-pg pg_dump -U postgres ww | gzip > runs/<name>/ww.sql.gz` and
  move the export, log, manifest and cookies beside it. Then `docker rm -f ww-sim-pg && docker volume rm ww-sim-pg`.
- **`promptHash` identifies the prompt version.** It covers the instructions without the game state,
  so turns in the same stage, difficulty and class share a hash until the prompt text changes. Use it
  to compare runs before and after a prompt change. Traces from the first run (23 November 2026)
  predate this: their hash includes the game state and differs on almost every turn.

## Adding a persona

Add a file to `personas/`. Say who the player is, how they write (length, spelling, language), what
they want, what they do when their character dies, and what to watch for. The slug becomes the
`persona` label on every turn of that game.
