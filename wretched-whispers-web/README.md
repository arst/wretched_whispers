# Wretched Whispers — web client

Next.js front end for the Wretched Whispers game server. The API lives in
`../wretched-whispers-server`; start it first (`dotnet run`) or point
`NEXT_PUBLIC_API_URL` at a running instance.

```bash
npm run dev     # http://localhost:3000
npm test        # vitest
npm run lint
npm run build
```

Copy `.env.example` to `.env.local`. `NEXT_PUBLIC_DEPLOYMENT_PROFILE` stays unset for local
development; setting it to `Server`, `StandaloneContainer`, or `Desktop` switches the build to a
static export (`.next-export/`) and, for the two standalone profiles, skips the login gate in
favour of the backend's fixed local user.
