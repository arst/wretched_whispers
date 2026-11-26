#!/usr/bin/env node
// Plays Wretched Whispers over its HTTP API, one command per player action. Made for simulated
// players (see simulation/README.md): it prints what a player would see on screen and nothing
// else, and records which persona played which session in simulation/runs/manifest.jsonl.
//
//   node simulation/sim-player.mjs new --persona <slug> --name <character name> [--class <Class>]
//                                 [--difficulty <Difficulty>] [--max-turns 30]
//   node simulation/sim-player.mjs say <game>            (the message is read from stdin)
//   node simulation/sim-player.mjs successor <game> --name <character name> [--class <Class>]
//
// Needs the API running in the Server profile (dev: http://localhost:5007, override with WW_API).
import { appendFileSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { randomUUID } from "node:crypto";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { parseArgs } from "node:util";

const API = process.env.WW_API ?? "http://localhost:5007";
const DIR = process.env.WW_SIM_DIR ?? join(dirname(fileURLToPath(import.meta.url)), "runs");
const PASSWORD = "sim-player-password";

const { positionals, values: flags } = parseArgs({
  allowPositionals: true,
  options: {
    persona: { type: "string", default: "unnamed" },
    name: { type: "string" },
    class: { type: "string" },
    difficulty: { type: "string" },
    "max-turns": { type: "string", default: "30" },
  },
});
const [command, gameId] = positionals;

const gamePath = (id) => join(DIR, `${id}.json`);
const save = (game) => writeFileSync(gamePath(game.id), JSON.stringify(game, null, 2));
const fail = (message) => {
  console.error(message);
  process.exit(1);
};

async function api(game, path, { method = "GET", body } = {}) {
  const headers = { Cookie: Object.entries(game.cookies).map(([k, v]) => `${k}=${v}`).join("; ") };
  if (body !== undefined) headers["Content-Type"] = "application/json";
  if (method !== "GET" && game.csrf) headers["X-CSRF-TOKEN"] = game.csrf;
  const response = await fetch(`${API}/api${path}`, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  for (const cookie of response.headers.getSetCookie()) {
    const [pair] = cookie.split(";");
    const at = pair.indexOf("=");
    game.cookies[pair.slice(0, at)] = pair.slice(at + 1);
  }
  return response;
}

async function ok(game, path, options) {
  const response = await api(game, path, options);
  if (!response.ok) {
    const text = await response.text();
    let problem = text;
    try {
      const json = JSON.parse(text);
      problem = json.detail ?? json.title ?? text;
    } catch {}
    fail(`The game refused that (${response.status}): ${problem}`);
  }
  return response;
}

/** Sends one player message and returns the narration it produced, or throws the game's error. */
async function turn(game, message) {
  const submitted = await ok(game, `/sessions/${game.sessionId}/turns`, {
    method: "POST",
    body: { requestId: randomUUID(), message },
  });
  const { turnId, error } = await submitted.json();
  if (error) throw new Error(error);

  const stream = await ok(game, `/turns/${turnId}/events`);
  let narration = "";
  let buffer = "";
  for await (const chunk of stream.body.pipeThrough(new TextDecoderStream())) {
    buffer += chunk.replaceAll("\r\n", "\n");
    let end;
    while ((end = buffer.indexOf("\n\n")) >= 0) {
      const frame = buffer.slice(0, end);
      buffer = buffer.slice(end + 2);
      const type = /^event: ?(.*)$/m.exec(frame)?.[1];
      const data = [...frame.matchAll(/^data: ?(.*)$/gm)].map((m) => m[1]).join("\n");
      if (type === "narrative") narration += JSON.parse(data).text ?? "";
      else if (type === "reset") narration = ""; // a retried attempt: what streamed before is void
      else if (type === "error") throw new Error(JSON.parse(data).message ?? "The turn failed.");
      else if (type === "done") return narration.trim();
    }
  }
  throw new Error("The connection closed before the turn finished.");
}

/** What the player's side panel shows, as one line. */
async function screen(game) {
  const detail = await (await ok(game, `/sessions/${game.sessionId}`)).json();
  const s = detail.state;
  const conditions = Object.entries({
    "lost eye": s.hasLostEye, "stabbed lung": s.hasStabbedLung, "broken hand": s.hasBrokenHand,
    "crushed foot": s.hasCrushedFoot, "severed arm": s.hasSeveredArm, "smashed face": s.hasSmashedFace,
    infected: s.isInfected, dizzy: s.isDizzyFromMagic, encumbered: s.isEncumbered,
  }).filter(([, on]) => on).map(([name]) => name);
  const parts = [
    `${s.characterName}${s.characterClass ? ` the ${s.characterClass}` : ""}`,
    `day ${s.currentDay}, hour ${s.currentHour}`,
    s.currentLocationName && `at ${s.currentLocationName}`,
    `HP ${s.characterHp}/${s.characterMaxHp}`,
    `STR ${s.characterStrength} AGI ${s.characterAgility} PRE ${s.characterPresence} TGH ${s.characterToughness}`,
    `${s.characterSilver} silver`,
    `omens ${s.characterOmens}`,
    `weapon: ${s.characterWeapon ?? "none"}`,
    `armor: ${s.characterArmor ?? "none"}${s.hasShield ? (s.isShieldBroken ? " + broken shield" : " + shield") : ""}`,
    `carrying: ${carried(s.characterInventory) || "nothing"}`,
    s.characterScrolls?.length && `scrolls: ${s.characterScrolls.join(", ")}`,
    conditions.length && `conditions: ${conditions.join(", ")}`,
    `miseries ${s.miseryCount}/7`,
    s.stage,
  ].filter(Boolean);

  let note = `Turn ${game.turns} of ${game.maxTurns}.`;
  if (s.worldEnded || detail.status === "ended") note = "THE GAME IS OVER. Stop and report.";
  else if (s.isDead) note = "YOUR CHARACTER IS DEAD. Roll a successor (successor command) or stop and report.";
  else if (game.turns >= game.maxTurns) note = "Turn limit reached. Stop and report.";
  return `[${parts.join(" | ")}]\n${note}`;
}

async function play(game, message) {
  try {
    console.log(await turn(game, message));
  } catch (error) {
    console.log(`(The game showed an error: ${error.message})`);
  }
  save(game);
  console.log(`\n${await screen(game)}`);
}

mkdirSync(DIR, { recursive: true });

if (command === "new") {
  if (!flags.name) fail("--name is required.");
  const id = `${flags.persona}-${randomUUID().slice(0, 6)}`;
  const game = {
    id, persona: flags.persona, email: `sim-${id}@sim.local`, cookies: {}, csrf: null,
    turns: 0, maxTurns: Number(flags["max-turns"]),
  };
  const credentials = { email: game.email, password: PASSWORD };
  await ok(game, "/auth/register", { method: "POST", body: credentials });
  await ok(game, "/auth/login?useCookies=true", { method: "POST", body: credentials });
  game.csrf = (await (await ok(game, "/auth/csrf")).json()).token;
  const created = await ok(game, "/sessions", {
    method: "POST",
    body: { characterName: flags.name, difficulty: flags.difficulty, characterClass: flags.class },
  });
  game.sessionId = (await created.json()).sessionId;
  save(game);
  appendFileSync(join(DIR, "manifest.jsonl"), JSON.stringify({
    game: id, persona: game.persona, sessionId: game.sessionId, email: game.email,
    difficulty: flags.difficulty ?? "Grim", class: flags.class ?? "rolled", startedAt: new Date().toISOString(),
  }) + "\n");
  console.log(`GAME ${id}\n`);
  await play(game, "begin"); // what the web client sends, silently, to get the opening narration
} else if (command === "say" || command === "successor") {
  if (!gameId) fail("Which game? Pass the GAME id printed by `new`.");
  let game;
  try {
    game = JSON.parse(readFileSync(gamePath(gameId), "utf8"));
  } catch {
    fail(`No game called ${gameId}.`);
  }
  if (command === "successor") {
    if (!flags.name) fail("--name is required.");
    await ok(game, `/sessions/${game.sessionId}/successor`, {
      method: "POST",
      body: { characterName: flags.name, characterClass: flags.class },
    });
    await play(game, "begin");
  } else {
    if (game.turns >= game.maxTurns) fail("Turn limit reached. Stop and report.");
    const message = readFileSync(0, "utf8").trim();
    if (!message) fail("Nothing to say: pipe the message on stdin.");
    game.turns++;
    await play(game, message);
  }
} else {
  fail("Usage: sim-player.mjs new|say|successor (see the header of this file).");
}

// One entry per unit, as the state sends it; grouped "×N" as the web UI's InventoryList shows it. Shown raw,
// players of the fourth run reported "the same item listed seven times".
function carried(items) {
  const counts = new Map();
  for (const item of items ?? []) counts.set(item, (counts.get(item) ?? 0) + 1);
  return [...counts].map(([item, n]) => (n > 1 ? `${item} ×${n}` : item)).join(", ");
}
