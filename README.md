# ![Wretched Whispers Logo](logo.png)

# ABOUT

An experiment in fusing AI Agents with an Expert System to emulate the doomed world of MÖRK BORG.

The rules draw primarily from the MÖRK BORG core book, though some have been bent, broken, or simplified in the name of
playability (for now). Treat this system as a tool, a toy, or a trap — use it at your own discretion.

Remember: the world is never more than seven Miseries away from its inevitable end. Every roll, every scar, every broken
body brings the apocalypse closer.

Play with it, or play against it.
Play if you dare.

# Run with Docker

The default image is the single-user `StandaloneContainer` profile. It has no login and stores its
SQLite database and settings in `/data`.

```bash
docker run -p 8080:8080 -v ww-data:/data -e OPENAI_API_KEY=sk-... ghcr.io/arst/wretched-whispers
```

Open http://localhost:8080 and play. Or run with no key and paste it in the browser on first run:

```bash
docker run -p 8080:8080 -v ww-data:/data ghcr.io/arst/wretched-whispers
```

| Env var | Meaning | Default |
|---|---|---|
| `OPENAI_API_KEY` | Your OpenAI-compatible API key | unset → first-run screen asks |
| `OPENAI_MODEL` | Chat model | `gpt-4o` |
| `OPENAI_BASE_URL` | OpenAI-compatible gateway (e.g. OpenRouter) | OpenAI |

When `OPENAI_API_KEY` is set on the container, it always wins over a key saved in the browser — including after a restart.

Azure OpenAI works through its OpenAI-compatible endpoint: set `OPENAI_BASE_URL` to
`https://<resource>.openai.azure.com/openai/v1` and `OPENAI_MODEL` to your deployment name.

Game data (SQLite + settings) lives in the `/data` volume. The container serves plain HTTP on
port 8080 — put Caddy/Traefik/nginx in front if you expose it beyond your machine.

# Deployment profiles

| Profile | Build | Runtime |
|---|---|---|
| `Server` | `docker build --build-arg DEPLOYMENT_PROFILE=Server -t wretched-whispers:server .` | Identity, PostgreSQL-ready API, bundled UI; no writable volume required |
| `StandaloneContainer` | `docker build -t wretched-whispers:standalone .` | Local user, settings UI, SQLite in `/data` |
| `Desktop` | `./build-desktop.sh [rid]` | Local user, settings UI, SQLite in OS app-data, Photino window |

All release profiles set `NEXT_PUBLIC_DEPLOYMENT_PROFILE` and bundle the static export into the
ASP.NET app. Application endpoints live under `/api`; health probes remain at `/health`.

# Local development

```bash
./dev.sh              # API on :5007 + Next dev server on :3000 (Ctrl-C stops both)
./dev.sh --api-only   # or --web-only
```

`dev.sh` builds the default `Server` profile in Development: leave `NEXT_PUBLIC_DEPLOYMENT_PROFILE`
unset, and the API applies pending SQLite migrations on startup. See
[docs/database-migrations.md](docs/database-migrations.md) for PostgreSQL, which is never migrated
by the API.

# DISCLAIMER

Wretched Whispers is an independent production by Artem Startsev and is not affiliated with Ockult Örtmästare Games or
Stockholm Kartell. It is published under the MÖRK BORG Third Party License. MÖRK BORG is copyright Ockult Örtmästare
Games and Stockholm Kartell.
